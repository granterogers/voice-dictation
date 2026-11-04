// ============================================================================
// VoiceDictation — Win+\ to record, auto-stops on silence, transcribes & polishes
// C# / .NET 8 / Windows Forms / NAudio
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;

namespace VoiceDictation;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "VoiceDictation_SingleInstance", out bool isNew);
        if (!isNew) return;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApp());
    }
}

// Diagnostic trace, written next to the exe. Deliberately never records API keys or
// credentials - only pipeline stage names, sound file names, and transcript text.
static class Log
{
    private static readonly object Gate = new object();
    public static readonly string Path =
        System.IO.Path.Combine(AppContext.BaseDirectory, "voice-dictation.log");

    public static void Write(string stage, string detail = "")
    {
        try
        {
            lock (Gate)
                File.AppendAllText(Path,
                    $"{DateTime.Now:HH:mm:ss.fff}  {stage}{(detail.Length > 0 ? "  |  " + detail : "")}{Environment.NewLine}");
        }
        catch { }
    }
}

static class SoundPlayback
{
    // Fire-and-forget WAV playback with volume control (System.Media.SoundPlayer has no
    // volume API, so this uses NAudio instead). Each call owns its own reader/output pair,
    // disposed when playback finishes so overlapping sounds don't step on each other.
    public static void Play(string path, int volumePercent)
    {
        try
        {
            if (!File.Exists(path)) { Log.Write("SOUND-MISSING", path); return; }
            var reader = new NAudio.Wave.AudioFileReader(path)
            {
                Volume = Math.Clamp(volumePercent, 0, 100) / 100f
            };
            var output = new NAudio.Wave.WaveOutEvent();
            output.Init(reader);
            output.PlaybackStopped += (_, _) => { output.Dispose(); reader.Dispose(); };
            output.Play();
        }
        // Swallowing this used to mean a sound that silently never played — with MP3
        // in the mix (which needs a decoder NAudio resolves at runtime) that would be
        // indistinguishable from sounds being switched off.
        catch (Exception ex) { Log.Write("SOUND-FAILED", $"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}"); }
    }
}

static class AppVersion
{
    public static readonly string Current =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "v0.0.0-dev";
}

// ============================================================================
// Settings
// ============================================================================

class AppSettings
{
    public string SoundStart { get; set; } = "enter_voice_mode.mp3";
    public string SoundDone { get; set; } = "exit_voice_mode.mp3";
    public string SoundError { get; set; } = "disconnected.mp3";
    public bool SoundsEnabled { get; set; } = true;
    public int SoundVolume { get; set; } = 60;
    public string SoundScheme { get; set; } = "Claude";

    // Curated presets. Chosen by measuring every WAV in C:\Windows\Media for duration
    // and RMS level, then picking short + quiet combinations; the defaults above were
    // 0.84s/1.07s/2.11s, which is why the old set felt loud and drawn out. Each scheme
    // pairs a lighter "start" against a slightly more settled "done" so the two stay
    // distinguishable even at low volume.
    public static readonly (string Name, string Start, string Done, string Error)[] SoundSchemes =
    {
        // name        start                           done                       error
        // Claude's own voice-mode SFX, extracted from the installed desktop app into
        // sounds\. Local use only — these are Anthropic assets, never commit them.
        ("Claude",    "enter_voice_mode.mp3",         "exit_voice_mode.mp3",      "disconnected.mp3"),
        ("Minimal",   "Windows Navigation Start.wav", "Windows Menu Command.wav", "Windows Information Bar.wav"),
        ("Soft",      "Windows Feed Discovered.wav",  "ding.wav",                 "Windows Pop-up Blocked.wav"),
        ("Crisp",     "Windows Startup.wav",          "Windows Recycle.wav",      "Windows Error.wav"),
        ("Window",    "Windows Restore.wav",          "Windows Minimize.wav",     "Windows Recycle.wav"),
        ("Classic",   "Speech On.wav",                "Speech Off.wav",           "Windows Foreground.wav"),
    };
    public float OverlayFontSize { get; set; } = 10.5f;
    public int OverlayBgR { get; set; } = 24;
    public int OverlayBgG { get; set; } = 24;
    public int OverlayBgB { get; set; } = 28;
    public int OverlayOpacity { get; set; } = 75;
    public int TextColorR { get; set; } = 230;
    public int TextColorG { get; set; } = 230;
    public int TextColorB { get; set; } = 230;
    public int StatusColorR { get; set; } = 120;
    public int StatusColorG { get; set; } = 180;
    public int StatusColorB { get; set; } = 255;
    public int InputDeviceIndex { get; set; } = -1;
    public string PolishPrompt { get; set; } =
        "You are a dictation cleanup assistant. Fix punctuation, capitalization, and sentence structure. Keep the user's tone and meaning exactly. Do NOT add, remove, or change any content beyond fixing grammar.\n\n" +
        "Format the result like a well-structured email or message:\n" +
        "- If there is a greeting (e.g. \"Hi Sarah\", \"Hey team\"), it ALWAYS goes alone on the first line, followed by a blank line — even in a short message.\n" +
        "- If there is a closing/sign-off (e.g. \"Thanks\", \"Best wishes\", \"Best regards\", a name), it ALWAYS goes alone on its own line at the end, separated from the body by a blank line — even in a short message.\n" +
        "- The body sits between the greeting and sign-off. NEVER write more than 2-3 sentences in a row without breaking to a new paragraph. Break to a new paragraph at every step, topic shift, or transition word (\"But\", \"So\", \"Also\", \"Then\", \"After that\", \"Thereafter\", \"However\"). Separate every paragraph with exactly one blank line. A long explanation must become several short paragraphs — never one dense block of text.\n" +
        "- If there is no greeting or sign-off at all, don't invent one — just apply the same 2-3-sentence paragraph rule to the whole text.\n\n" +
        "Example 1 input: \"Hi Sergio thanks for reaching out if you could kindly provide me a phone number to reach you I will give you a call best wishes\"\n" +
        "Example 1 output:\n" +
        "Hi Sergio,\n\nThanks for reaching out. If you could kindly provide me a phone number to reach you, I will give you a call.\n\nBest wishes.\n\n" +
        "Example 2 input: \"Hi Dylan thanks for your patience while I looked over this yes so if you're using the default saw optimizer then that should by default include perfect graining so you shouldn't really need to use the cut right template pattern names but if you really feel like you have to then what you need to do is within the processing station settings you need to switch from microvellum sawing optimizer to the cut right ASCII option thereafter you should be able to drag across the template patterns you wish\"\n" +
        "Example 2 output:\n" +
        "Hi Dylan,\n\nThanks for your patience while I looked over this. Yes, if you're using the default saw optimizer, that should by default include perfect graining, so you shouldn't really need to use the Cut Right template pattern names.\n\nBut if you really feel like you have to, within the processing station settings you need to switch from the Microvellum sawing optimizer to the Cut Right ASCII option.\n\nThereafter, you should be able to drag across the template patterns you wish.\n\n" +
        "Output ONLY the cleaned text, including the blank lines described above. Do NOT write anything before or after it. Do NOT explain what you changed.";

    public static readonly string MediaDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    // Sounds shipped next to the exe. Not committed to the repo — see .gitignore.
    public static readonly string LocalSoundsDir = Path.Combine(AppContext.BaseDirectory, "sounds");

    // The default scheme points at files in sounds\, which is gitignored — so on a
    // fresh clone they are absent. Rather than going silently mute, fall back to this
    // all-Windows scheme, which exists on every machine.
    private static readonly (string Start, string Done, string Error) FallbackScheme =
        ("Windows Feed Discovered.wav", "ding.wav", "Windows Pop-up Blocked.wav");

    // A stored sound is either an absolute path, a file in sounds\, or a Windows media
    // name. Checking sounds\ first lets a scheme reference a bundled file by bare name.
    private static string Locate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
        var local = Path.Combine(LocalSoundsDir, name);
        if (File.Exists(local)) return local;
        var media = Path.Combine(MediaDir, name);
        return File.Exists(media) ? media : null;
    }

    private static string ResolveSound(string name, string fallback) =>
        Locate(name) ?? Locate(fallback) ?? "";

    public Color OverlayBgColor => Color.FromArgb(OverlayBgR, OverlayBgG, OverlayBgB);
    public Color TextColor => Color.FromArgb(TextColorR, TextColorG, TextColorB);
    public Color StatusColor => Color.FromArgb(StatusColorR, StatusColorG, StatusColorB);

    public string SoundStartPath => ResolveSound(SoundStart, FallbackScheme.Start);
    public string SoundDonePath => ResolveSound(SoundDone, FallbackScheme.Done);
    public string SoundErrorPath => ResolveSound(SoundError, FallbackScheme.Error);

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");
    private static string SettingsPathAlt => Path.Combine(Directory.GetCurrentDirectory(), "settings.json");

    public static AppSettings Load()
    {
        foreach (var p in new[] { SettingsPath, SettingsPathAlt })
        {
            if (File.Exists(p))
            {
                try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p)) ?? new(); }
                catch { }
            }
        }
        return new();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            try { File.WriteAllText(SettingsPath, json); }
            catch { File.WriteAllText(SettingsPathAlt, json); }
        }
        catch { }
    }
}

// ============================================================================
// Settings form
// ============================================================================

sealed class SettingsForm : Form
{
    private readonly AppSettings _s;
    private const string CUSTOM_SCHEME = "Custom";
    private readonly ComboBox _cmbStart, _cmbDone, _cmbError, _cmbDevice, _cmbScheme;
    // Set while ApplyScheme() drives the three sound combos, so their change handlers
    // don't immediately flip the scheme back to "Custom".
    private bool _applyingScheme;
    private readonly CheckBox _chkSounds;
    private readonly TrackBar _trkVolume;
    private readonly Label _lblVolume;
    private NAudio.Wave.WaveOutEvent _volumePreviewOutput;
    private NAudio.Wave.AudioFileReader _volumePreviewReader;
    private readonly NumericUpDown _numFont;
    private readonly TextBox _txtPrompt;
    private readonly TrackBar _trkOpacity;
    private readonly Label _lblOpacity;
    private readonly Panel _pnlBg, _pnlText, _pnlStatus, _prevPanel;
    private readonly Label _prevStatus, _prevText;
    private Color _bgColor, _textColor, _statusColor;

    // WAV file names (just the filename, e.g. "Windows Notify.wav")
    private readonly string[] _wavNames;

    public SettingsForm(AppSettings settings)
    {
        _s = settings;
        _bgColor = _s.OverlayBgColor;
        _textColor = _s.TextColor;
        _statusColor = _s.StatusColor;

        // Gather WAV filenames
        if (Directory.Exists(AppSettings.MediaDir))
        {
            var paths = Directory.GetFiles(AppSettings.MediaDir, "*.wav");
            _wavNames = new string[paths.Length];
            for (int i = 0; i < paths.Length; i++)
                _wavNames[i] = Path.GetFileName(paths[i]);
            Array.Sort(_wavNames, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            _wavNames = Array.Empty<string>();
        }

        // Sounds shipped beside the exe (sounds\) are offered alongside the Windows ones.
        // Listed first so they are easy to find; AppSettings resolves a bare filename
        // against this folder before falling back to C:\Windows\Media.
        if (Directory.Exists(AppSettings.LocalSoundsDir))
        {
            var local = Directory.GetFiles(AppSettings.LocalSoundsDir)
                .Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (local.Length > 0)
                _wavNames = local.Concat(_wavNames).ToArray();
        }

        Text = $"Voice Dictation — Settings ({AppVersion.Current})";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(520, 694);

        var y = 10;

        // === INPUT DEVICE ===
        Lbl("Input Device", 12, y, true); y += 22;
        _cmbDevice = new ComboBox
        {
            Location = new Point(12, y), Size = new Size(496, 24),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbDevice.Items.Add("(System default)");
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            _cmbDevice.Items.Add(WaveInEvent.GetCapabilities(i).ProductName);
        _cmbDevice.SelectedIndex = Math.Min(_s.InputDeviceIndex + 1, _cmbDevice.Items.Count - 1);
        if (_cmbDevice.SelectedIndex < 0) _cmbDevice.SelectedIndex = 0;
        Controls.Add(_cmbDevice);
        y += 32;

        // === SOUNDS ===
        Lbl("Sounds", 12, y, true); y += 22;
        _chkSounds = new CheckBox
        {
            Text = "Enable sounds", Checked = _s.SoundsEnabled,
            Location = new Point(12, y), AutoSize = true
        };
        Controls.Add(_chkSounds);

        Lbl("Volume:", 160, y + 2);
        _trkVolume = new TrackBar
        {
            Location = new Point(220, y - 2), Size = new Size(160, 30),
            Minimum = 0, Maximum = 100, Value = _s.SoundVolume,
            TickFrequency = 10, SmallChange = 5
        };
        _lblVolume = new Label { Location = new Point(384, y + 2), AutoSize = true, Text = _s.SoundVolume + "%" };
        _trkVolume.ValueChanged += (_, _) => { _lblVolume.Text = _trkVolume.Value + "%"; PreviewVolume(_trkVolume.Value); };
        Controls.Add(_trkVolume); Controls.Add(_lblVolume);
        y += 40;

        Lbl("Scheme:", 12, y + 2);
        _cmbScheme = new ComboBox
        {
            Location = new Point(60, y), Size = new Size(170, 24),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbScheme.Items.Add(CUSTOM_SCHEME);
        foreach (var s in AppSettings.SoundSchemes) _cmbScheme.Items.Add(s.Name);
        _cmbScheme.SelectedItem = _cmbScheme.Items.Contains(_s.SoundScheme) ? _s.SoundScheme : CUSTOM_SCHEME;
        _cmbScheme.SelectedIndexChanged += (_, _) => ApplyScheme();
        Controls.Add(_cmbScheme);

        var btnPreviewScheme = new Button { Text = "Preview", Size = new Size(80, 25), Location = new Point(240, y - 1) };
        btnPreviewScheme.Click += (_, _) => PreviewScheme();
        Controls.Add(btnPreviewScheme);
        y += 30;

        Lbl("Start:", 12, y + 2);
        _cmbStart = SndCombo(_s.SoundStart, 60, y); y += 28;
        Lbl("Done:", 12, y + 2);
        _cmbDone = SndCombo(_s.SoundDone, 60, y); y += 28;
        Lbl("Error:", 12, y + 2);
        _cmbError = SndCombo(_s.SoundError, 60, y); y += 36;

        // === OVERLAY ===
        Lbl("Overlay", 12, y, true); y += 26;

        // Row 1: Font size
        Lbl("Font size:", 12, y + 2);
        _numFont = new NumericUpDown
        {
            Location = new Point(80, y), Size = new Size(60, 24),
            Minimum = 8, Maximum = 24, DecimalPlaces = 1, Increment = 0.5m,
            Value = (decimal)_s.OverlayFontSize
        };
        _numFont.ValueChanged += (_, _) => RefreshPreview();
        Controls.Add(_numFont);
        y += 30;

        // Row 2: Colors
        Lbl("Background:", 12, y + 2);
        _pnlBg = ColorSwatch(_bgColor, 100, y + 1, c => { _bgColor = c; RefreshPreview(); });
        Lbl("Text:", 160, y + 2);
        _pnlText = ColorSwatch(_textColor, 200, y + 1, c => { _textColor = c; RefreshPreview(); });
        Lbl("Status:", 260, y + 2);
        _pnlStatus = ColorSwatch(_statusColor, 315, y + 1, c => { _statusColor = c; RefreshPreview(); });
        y += 30;

        // Row 3: Opacity
        Lbl("Bg opacity:", 12, y + 2);
        _trkOpacity = new TrackBar
        {
            Location = new Point(90, y - 2), Size = new Size(180, 30),
            Minimum = 30, Maximum = 100, Value = _s.OverlayOpacity,
            TickFrequency = 10, SmallChange = 5
        };
        _lblOpacity = new Label { Location = new Point(274, y + 2), AutoSize = true, Text = _s.OverlayOpacity + "%" };
        _trkOpacity.ValueChanged += (_, _) => { _lblOpacity.Text = _trkOpacity.Value + "%"; RefreshPreview(); };
        Controls.Add(_trkOpacity); Controls.Add(_lblOpacity);
        y += 40;

        // Preview
        _prevPanel = new Panel { Location = new Point(12, y), Size = new Size(496, 60), BorderStyle = BorderStyle.FixedSingle };
        _prevStatus = new Label { Dock = DockStyle.Top, Height = 20, BackColor = Color.Transparent, Padding = new Padding(6, 3, 0, 0) };
        _prevText = new Label { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(6, 1, 6, 0), TextAlign = ContentAlignment.TopLeft };
        _prevPanel.Controls.Add(_prevText);
        _prevPanel.Controls.Add(_prevStatus);
        Controls.Add(_prevPanel);
        RefreshPreview();
        y += 68;

        // === AI PROMPT ===
        Lbl("AI Cleanup Prompt", 12, y, true); y += 20;
        var desc = new Label
        {
            Text = "This prompt tells the AI how to clean up your dictated speech before pasting. Customise the\ntone or style, e.g. \"rewrite formally\", \"keep it casual\", \"use British English\".",
            Location = new Point(12, y), AutoSize = true,
            ForeColor = Color.FromArgb(110, 110, 110),
            Font = new Font("Segoe UI", 8.25f)
        };
        Controls.Add(desc);
        y += 34;

        _txtPrompt = new TextBox
        {
            Location = new Point(12, y), Size = new Size(496, 90),
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            Text = _s.PolishPrompt,
            Font = new Font("Segoe UI", 12f)
        };
        Controls.Add(_txtPrompt);
        y += 98;

        // === BUTTONS ===
        var lblVersion = new Label
        {
            Text = AppVersion.Current, AutoSize = true,
            ForeColor = Color.FromArgb(130, 130, 130),
            Font = new Font("Segoe UI", 8f),
            Location = new Point(12, y + 8)
        };
        Controls.Add(lblVersion);

        var btnSave = new Button { Text = "Save", Size = new Size(80, 30), Location = new Point(340, y) };
        btnSave.Click += (_, _) => { DoSave(); DialogResult = DialogResult.OK; Close(); };
        Controls.Add(btnSave);

        var btnCancel = new Button { Text = "Cancel", Size = new Size(80, 30), Location = new Point(428, y) };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnCancel);

        FormClosed += (_, _) => StopVolumePreview();
    }

    // Drives the three sound combos from the selected preset. Selecting "Custom" leaves
    // whatever is already chosen alone, so it never clobbers a hand-picked combination.
    private void ApplyScheme()
    {
        var name = _cmbScheme.SelectedItem as string;
        if (string.IsNullOrEmpty(name) || name == CUSTOM_SCHEME) return;

        foreach (var s in AppSettings.SoundSchemes)
        {
            if (s.Name != name) continue;
            _applyingScheme = true;
            try
            {
                SelectWav(_cmbStart, s.Start);
                SelectWav(_cmbDone, s.Done);
                SelectWav(_cmbError, s.Error);
            }
            finally { _applyingScheme = false; }
            PreviewScheme();
            return;
        }
    }

    // SndCombo stores display names without the .wav extension, so match on that.
    private static void SelectWav(ComboBox cmb, string wavName)
    {
        var target = Path.GetFileNameWithoutExtension(wavName);
        for (int i = 0; i < cmb.Items.Count; i++)
        {
            if (string.Equals(cmb.Items[i] as string, target, StringComparison.OrdinalIgnoreCase))
            { cmb.SelectedIndex = i; return; }
        }
    }

    // Plays start then done back to back so a scheme can be judged as a pair.
    private void PreviewScheme()
    {
        StopVolumePreview();
        var vol = _trkVolume.Value;
        var start = Path.Combine(AppSettings.MediaDir, SelectedWav(_cmbStart));
        var done = Path.Combine(AppSettings.MediaDir, SelectedWav(_cmbDone));
        SoundPlayback.Play(start, vol);
        var t = new System.Windows.Forms.Timer { Interval = 900 };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); SoundPlayback.Play(done, vol); };
        t.Start();
    }

    // Restarts the preview sound at the new volume on every slider change, stopping any
    // still-playing previous preview first so a fast drag doesn't overlap garbled sounds.
    private void PreviewVolume(int volumePercent)
    {
        StopVolumePreview();
        try
        {
            var path = Path.Combine(AppSettings.MediaDir, SelectedWav(_cmbStart));
            if (!File.Exists(path)) return;
            _volumePreviewReader = new NAudio.Wave.AudioFileReader(path) { Volume = Math.Clamp(volumePercent, 0, 100) / 100f };
            _volumePreviewOutput = new NAudio.Wave.WaveOutEvent();
            _volumePreviewOutput.Init(_volumePreviewReader);
            _volumePreviewOutput.Play();
        }
        catch { }
    }

    private void StopVolumePreview()
    {
        try { _volumePreviewOutput?.Stop(); _volumePreviewOutput?.Dispose(); _volumePreviewReader?.Dispose(); } catch { }
        _volumePreviewOutput = null; _volumePreviewReader = null;
    }

    private void RefreshPreview()
    {
        float fs = (float)_numFont.Value;
        int alpha = (int)(_trkOpacity.Value / 100.0 * 255);
        _prevPanel.BackColor = Color.FromArgb(alpha, _bgColor.R, _bgColor.G, _bgColor.B);
        _prevStatus.ForeColor = _statusColor;
        _prevStatus.Font = new Font("Segoe UI", Math.Max(fs - 1.5f, 8f), FontStyle.Bold);
        _prevStatus.Text = "\U0001F3A4 Listening...";
        _prevText.ForeColor = _textColor;
        _prevText.Font = new Font("Segoe UI", fs);
        _prevText.Text = "This is how your transcript will look.";
    }

    private void DoSave()
    {
        _s.SoundsEnabled = _chkSounds.Checked;
        _s.SoundVolume = _trkVolume.Value;
        _s.SoundScheme = _cmbScheme.SelectedItem as string ?? CUSTOM_SCHEME;
        _s.SoundStart = SelectedWav(_cmbStart);
        _s.SoundDone = SelectedWav(_cmbDone);
        _s.SoundError = SelectedWav(_cmbError);
        _s.OverlayFontSize = (float)_numFont.Value;
        _s.OverlayBgR = _bgColor.R; _s.OverlayBgG = _bgColor.G; _s.OverlayBgB = _bgColor.B;
        _s.OverlayOpacity = _trkOpacity.Value;
        _s.TextColorR = _textColor.R; _s.TextColorG = _textColor.G; _s.TextColorB = _textColor.B;
        _s.StatusColorR = _statusColor.R; _s.StatusColorG = _statusColor.G; _s.StatusColorB = _statusColor.B;
        _s.InputDeviceIndex = _cmbDevice.SelectedIndex - 1;
        _s.PolishPrompt = _txtPrompt.Text;
        _s.Save();
    }

    // --- Helpers ---

    private Label Lbl(string text, int x, int y, bool bold = false)
    {
        var l = new Label
        {
            Text = text, Location = new Point(x, y), AutoSize = true,
            Font = bold ? new Font("Segoe UI", 9.5f, FontStyle.Bold) : Font
        };
        Controls.Add(l); return l;
    }

    private ComboBox SndCombo(string currentFile, int x, int y)
    {
        var cmb = new ComboBox
        {
            Location = new Point(x, y), Size = new Size(380, 24),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        int selIdx = -1;
        for (int i = 0; i < _wavNames.Length; i++)
        {
            cmb.Items.Add(Path.GetFileNameWithoutExtension(_wavNames[i]));
            if (string.Equals(_wavNames[i], currentFile, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(_wavNames[i]), currentFile, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_wavNames[i], Path.GetFileName(currentFile), StringComparison.OrdinalIgnoreCase))
                selIdx = i;
        }
        if (selIdx < 0 && !string.IsNullOrEmpty(currentFile))
        {
            string justName = Path.GetFileName(currentFile);
            for (int i = 0; i < _wavNames.Length; i++)
            {
                if (string.Equals(_wavNames[i], justName, StringComparison.OrdinalIgnoreCase))
                { selIdx = i; break; }
            }
        }
        if (cmb.Items.Count > 0)
            cmb.SelectedIndex = Math.Max(selIdx, 0);

        // Hand-picking any individual sound means the selection no longer matches a
        // preset, so drop the scheme to "Custom" — unless ApplyScheme is driving us.
        cmb.SelectedIndexChanged += (_, _) =>
        {
            if (_applyingScheme || _cmbScheme == null) return;
            _cmbScheme.SelectedItem = CUSTOM_SCHEME;
        };
        Controls.Add(cmb);

        var btn = new Button
        {
            Text = "\u25B6", Size = new Size(26, 24), Location = new Point(x + 384, y),
            Font = new Font("Segoe UI", 7f)
        };
        btn.Click += (_, _) =>
        {
            var path = Path.Combine(AppSettings.MediaDir, SelectedWav(cmb));
            SoundPlayback.Play(path, _trkVolume.Value);
        };
        Controls.Add(btn);
        return cmb;
    }

    private string SelectedWav(ComboBox cmb)
    {
        if (cmb.SelectedIndex < 0 || cmb.SelectedIndex >= _wavNames.Length) return "";
        return _wavNames[cmb.SelectedIndex];
    }

    private Panel ColorSwatch(Color c, int x, int y, Action<Color> onChange)
    {
        var p = new Panel
        {
            Location = new Point(x, y), Size = new Size(30, 20),
            BackColor = c, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand
        };
        p.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = p.BackColor, FullOpen = true };
            if (dlg.ShowDialog() == DialogResult.OK) { p.BackColor = dlg.Color; onChange(dlg.Color); }
        };
        Controls.Add(p); return p;
    }
}

// ============================================================================
// Overlay window
// ============================================================================

sealed class OverlayForm : Form
{
    private readonly Label _statusLabel;
    private readonly Label _textLabel;
    private readonly Panel _panel;
    private readonly Button _btnClose;

    // X clicked: abandon the cycle and paste nothing, exactly like Escape.
    public event Action CloseClicked;

    // Body clicked: the user has finished speaking — stop the mic and process what
    // was captured, same as a second tray click.
    public event Action BodyClicked;

    public OverlayForm(AppSettings s)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(380, 124);
        try { int pref = 2; DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int)); } catch { }

        _panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 12) };

        _statusLabel = new Label
        {
            Dock = DockStyle.Top, Height = 24,
            BackColor = Color.Transparent,
            AutoSize = false, Text = ""
        };

        _textLabel = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            AutoSize = false, TextAlign = ContentAlignment.TopLeft, Text = ""
        };

        _btnClose = new Button
        {
            Text = "✕",
            Size = new Size(28, 28),
            Location = new Point(Width - 36, 6),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            TabStop = false,
            Cursor = Cursors.Hand
        };
        _btnClose.FlatAppearance.BorderSize = 0;
        _btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 60, 60);
        _btnClose.Click += (_, _) => CloseClicked?.Invoke();

        _panel.Controls.Add(_textLabel);
        _panel.Controls.Add(_statusLabel);
        Controls.Add(_btnClose);
        Controls.Add(_panel);
        _btnClose.BringToFront();

        // Clicking the body means "I've finished speaking". A Click on a child Label or
        // Panel does not bubble to the Form, so every visible surface is wired
        // individually. The X button is deliberately excluded — it cancels instead.
        foreach (var c in new Control[] { this, _panel, _statusLabel, _textLabel })
        {
            c.Cursor = Cursors.Hand;
            c.Click += (_, _) => BodyClicked?.Invoke();
        }

        ApplyColors(s);

        // WS_EX_NOACTIVATE means BringToFront() can't lift us above other apps, and any
        // app going full-screen (or another topmost window appearing) silently drops us
        // out of the topmost band. Re-assert the z-order on a timer while visible.
        _topmostTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _topmostTimer.Tick += (_, _) => AssertTopmost();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private readonly System.Windows.Forms.Timer _topmostTimer;

    // Push the window back into the topmost band without stealing focus. Setting
    // TopMost alone is not enough — WinForms treats it as a no-op when the property
    // is already true, even after the OS has demoted the window.
    private void AssertTopmost()
    {
        if (!Visible || !IsHandleCreated) return;
        try
        {
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        catch { }
    }

    public void ApplyColors(AppSettings s)
    {
        if (InvokeRequired) { Invoke(() => ApplyColors(s)); return; }
        BackColor = s.OverlayBgColor;
        _panel.BackColor = s.OverlayBgColor;
        Opacity = s.OverlayOpacity / 100.0;
        _statusLabel.ForeColor = s.StatusColor;
        _statusLabel.Font = new Font("Segoe UI", Math.Max(s.OverlayFontSize - 1.5f, 8f), FontStyle.Bold);
        _btnClose.BackColor = s.OverlayBgColor;
        _btnClose.ForeColor = s.TextColor;
        _textLabel.ForeColor = s.TextColor;
        _textLabel.Font = new Font("Segoe UI", s.OverlayFontSize);
    }

    public void ShowOverlay(string status)
    {
        if (InvokeRequired) { Invoke(() => ShowOverlay(status)); return; }
        _statusLabel.Text = status;
        _textLabel.Text = "";

        // Show on the monitor the user is actually working on, not always the primary
        // one — otherwise the overlay renders off on another display and looks absent.
        var screen = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen!;
        var wa = screen.WorkingArea;
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;

        TopMost = true;
        Show();
        AssertTopmost();
        _topmostTimer.Start();
    }

    // Roughly what fits in the smaller overlay at the default font.
    private const int MAX_PREVIEW_CHARS = 260;

    // A top-aligned Label clips the END of long text, which in a live preview hides the
    // words just spoken. Show the tail instead; the clipboard still gets the full text.
    private static string TailFit(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MAX_PREVIEW_CHARS) return text;
        var tail = text.Substring(text.Length - MAX_PREVIEW_CHARS);
        var space = tail.IndexOf(' ');
        if (space > 0 && space < 40) tail = tail.Substring(space + 1);
        return "… " + tail;
    }

    public void SetText(string text)
    {
        if (InvokeRequired) { Invoke(() => SetText(text)); return; }
        _textLabel.Text = TailFit(text);
        AssertTopmost();
    }

    public void SetStatus(string status)
    {
        if (InvokeRequired) { Invoke(() => SetStatus(status)); return; }
        _statusLabel.Text = status;
        AssertTopmost();
    }

    public void HideOverlay()
    {
        if (InvokeRequired) { Invoke(() => HideOverlay()); return; }
        _topmostTimer.Stop();
        Hide();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00000080;
            return cp;
        }
    }
}

// ============================================================================
// Audio recorder
// ============================================================================

sealed class AudioRecorder : IDisposable
{
    public const int SAMPLE_RATE = 16000;
    private const double SILENCE_THRESHOLD = 0.005;
    private const double SILENCE_DURATION = 2.0;
    private const int MAX_RECORD_SEC = 120;

    private readonly WaveInEvent _capture;
    private readonly MemoryStream _ms;
    private readonly WaveFileWriter _writer;
    private readonly ManualResetEventSlim _done = new(false);
    private readonly bool _autoStopOnSilence;
    private volatile bool _speechStarted;
    private double _silenceAccum;
    private DateTime _startTime;
    private int _totalBytesWritten;
    private readonly object _lock = new();

    public bool IsFinished => _done.IsSet;
    public int TotalBytesWritten { get { lock (_lock) return _totalBytesWritten; } }

    // True once any chunk exceeded SILENCE_THRESHOLD. Whisper hallucinates confident
    // filler ("thanks for watching", often in another language) when fed near-silence,
    // so the pipeline uses this to discard a recording that never contained speech.
    public bool SpeechDetected => _speechStarted;

    // ---- NEW: snapshot of bytes written at last chunk boundary ----
    private int _bytesAtLastChunk = 0;
    public int BytesSinceLastChunk { get { lock (_lock) return _totalBytesWritten - _bytesAtLastChunk; } }
    public void MarkChunkSent() { lock (_lock) _bytesAtLastChunk = _totalBytesWritten; }

    // autoStopOnSilence: when false the mic runs until StopNow() (or the hard cap).
    // Click-to-toggle dictation uses false so the second click is what ends it.
    public AudioRecorder(int deviceIndex = -1, bool autoStopOnSilence = true)
    {
        _autoStopOnSilence = autoStopOnSilence;
        _ms = new MemoryStream();
        var fmt = new WaveFormat(SAMPLE_RATE, 16, 1);
        _writer = new WaveFileWriter(new IgnoreDisposeStream(_ms), fmt);
        _capture = new WaveInEvent
        {
            WaveFormat = fmt, BufferMilliseconds = 100,
            DeviceNumber = deviceIndex < 0 ? 0 : deviceIndex
        };
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += (_, _) => _done.Set();
    }

    public void Start() { _startTime = DateTime.UtcNow; _capture.StartRecording(); }
    public void StopNow() { try { _capture.StopRecording(); } catch { } }
    public void WaitUntilDone() => _done.Wait();

    public byte[] GetCurrentWav() { lock (_lock) { _writer.Flush(); return _ms.ToArray(); } }
    public byte[] GetFinalWav() { lock (_lock) { _writer.Flush(); return _ms.ToArray(); } }

    private void OnData(object sender, WaveInEventArgs e)
    {
        lock (_lock) { _writer.Write(e.Buffer, 0, e.BytesRecorded); _totalBytesWritten += e.BytesRecorded; }

        double rms = ComputeRms(e.Buffer, e.BytesRecorded);
        double elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
        double chunkSec = (double)e.BytesRecorded / _capture.WaveFormat.AverageBytesPerSecond;

        if (rms > SILENCE_THRESHOLD) { _speechStarted = true; _silenceAccum = 0; }
        else if (_autoStopOnSilence && _speechStarted && elapsed > 2.0)
        {
            _silenceAccum += chunkSec;
            if (_silenceAccum >= SILENCE_DURATION) _capture.StopRecording();
        }

        if (elapsed > MAX_RECORD_SEC) _capture.StopRecording();
    }

    private static double ComputeRms(byte[] buf, int len)
    {
        int n = len / 2; if (n == 0) return 0;
        double sum = 0;
        for (int i = 0; i < len; i += 2) { double v = BitConverter.ToInt16(buf, i) / 32768.0; sum += v * v; }
        return Math.Sqrt(sum / n);
    }

    public void Dispose()
    {
        try { _capture.Dispose(); } catch { }
        try { _writer.Dispose(); } catch { }
        try { _ms.Dispose(); } catch { }
        _done.Dispose();
    }
}

// ============================================================================
// Tray application
// ============================================================================

sealed class TrayApp : ApplicationContext
{
    private static readonly string GROQ_API_KEY = LoadApiKey();
    private const string WHISPER_URL = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string WHISPER_MODEL = "whisper-large-v3-turbo";
    private const string CHAT_URL = "https://api.groq.com/openai/v1/chat/completions";
    private const string CHAT_MODEL = "openai/gpt-oss-120b";

    // Minimum new audio (bytes) before we fire a live-preview transcription.
    // At 16kHz/16bit/mono: 1 byte = 31.25µs → 32000 bytes ≈ 1 second of audio.
    private const int LIVE_PREVIEW_MIN_NEW_BYTES = 32000; // ~1 second

    // Click-to-start with no speech at all: give up rather than hold the overlay open
    // until the recorder's 120s hard cap.
    private const int MANUAL_IDLE_TIMEOUT_SEC = 15;

    private const string TRAY_TEXT_IDLE = "Voice Dictation — click or Win+\\ to dictate";
    private const string TRAY_TEXT_ACTIVE = "Voice Dictation — recording (click to stop)";

    private readonly NotifyIcon _trayIcon;
    private readonly Icon _iconIdle;
    private readonly Icon _iconActive;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly HttpClient _http;
    private readonly OverlayForm _overlay;
    private AppSettings _settings;

    private volatile bool _isRecording;
    private volatile bool _stopRequested;
    private volatile bool _cancelRequested;
    // The done sound has two possible trigger points (manual Win+\ stop, and end of
    // pipeline). Only one of them should ever be heard per dictation cycle.
    private volatile bool _donePlayed;
    private DateTime _lastTrayClick = DateTime.MinValue;
    private volatile bool _manualStopOnly;
    private AudioRecorder? _activeRecorder;

    private const int HOTKEY_ID = 1;
    private const int HOTKEY_ESCAPE = 2;
    private const int MOD_WIN = 0x0008;
    private const int MOD_NOREPEAT = 0x4000;
    private const int VK_OEM_5 = 0xDC;
    private const int VK_ESCAPE = 0x1B;

    public TrayApp()
    {
        _settings = AppSettings.Load();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GROQ_API_KEY);

        _overlay = new OverlayForm(_settings);
        _ = _overlay.Handle;

        // Clicking the overlay body means "done speaking": route it through the same
        // state machine as a second tray click, so Capturing stops and processes and
        // Processing just reports that it is still working.
        _overlay.BodyClicked += () =>
        {
            if (Phase == DictationPhase.Idle) return;
            Log.Write("OVERLAY CLICK", "finish requested");
            ToggleDictation();
        };

        // The overlay's X abandons the cycle, exactly like Escape.
        _overlay.CloseClicked += () =>
        {
            Log.Write("OVERLAY CLOSE", "cancel requested");
            _cancelRequested = true;
            _activeRecorder?.StopNow();
            _overlay.HideOverlay();
        };

        // Drawn rather than extracted from shell32 so idle and recording share one shape
        // and differ only in colour.
        _iconIdle = DrawMicIcon(Color.FromArgb(56, 132, 255));   // #3884FF
        _iconActive = DrawMicIcon(Color.FromArgb(235, 56, 56));  // #EB3838

        _trayIcon = new NotifyIcon
        {
            Icon = _iconIdle, Text = TRAY_TEXT_IDLE,
            Visible = true, ContextMenuStrip = BuildMenu()
        };

        // Assigning ContextMenuStrip only binds the RIGHT button, so left-click is free
        // to act as a start/stop toggle. This handler runs on the UI thread, which the
        // Escape-hotkey registration in StartDictation depends on (see HANDOVER.md 9.1).
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            // Click-started dictation is manual-stop: the mic keeps listening until the
            // next click. Without this the 2s silence auto-stop ends the cycle on its
            // own, and the "switch it off" click lands in Idle and starts a new one.
            _manualStopOnly = true;
            // Tray icons invite double-clicks, and NotifyIcon raises MouseClick for each
            // one — without this guard a double-click would start and instantly stop a
            // cycle, yielding a "no speech detected" error.
            var now = DateTime.UtcNow;
            if ((now - _lastTrayClick).TotalMilliseconds < 400) return;
            _lastTrayClick = now;
            ToggleDictation();
        };

        _hotkeyWindow = new HotkeyWindow(OnHotkeyPressed);
        HotkeyWindow.RegisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID, MOD_WIN, VK_OEM_5);

        Log.Write("APP LAUNCH", AppVersion.Current);
        PlaySound(_settings.SoundStartPath);
    }

    private ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip();
        m.Items.Add("Voice Dictation (Win+\\)").Enabled = false;
        m.Items.Add(AppVersion.Current).Enabled = false;
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Settings...", null, (_, _) => OpenSettings());
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Quit", null, (_, _) =>
        {
            HotkeyWindow.UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID);
            HotkeyWindow.UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ESCAPE);
            _trayIcon.Visible = false; Application.Exit();
        });
        return m;
    }

    private void OpenSettings()
    {
        using var f = new SettingsForm(_settings);
        if (f.ShowDialog() == DialogResult.OK)
        {
            _settings = AppSettings.Load();
            _overlay.ApplyColors(_settings);
        }
    }

    private static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    // The microphone glyph, authored on a 32-unit grid and scaled into a box of
    // `box` pixels centred on (cx, cy).
    private static void DrawMicGlyph(Graphics g, Color c, float cx, float cy, float box, float weight)
    {
        float u = box / 32f;
        float X(float v) => cx + (v - 16f) * u;
        float Y(float v) => cy + (v - 16f) * u;

        using var brush = new SolidBrush(c);
        using var pen = new Pen(c, weight * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        using (var body = RoundRect(X(11), Y(2.5f), 10 * u, 17 * u, 5 * u))
            g.FillPath(brush, body);

        g.DrawArc(pen, X(6.5f), Y(10f), 19 * u, 15 * u, 0, 180);
        g.DrawLine(pen, X(16), Y(25.5f), X(16), Y(28.5f));
        g.DrawLine(pen, X(10.5f), Y(28.5f), X(21.5f), Y(28.5f));
    }

    // Solid rounded-square badge with the mic knocked out in white. A filled shape
    // survives the drop to 16px far better than an outlined one: antialiasing turns a
    // 1px stroke into two half-lit pixels, so thin detail goes grey rather than small.
    private static Icon DrawMicIcon(Color color)
    {
        const float SIZE = 32f;
        var b = new Bitmap((int)SIZE, (int)SIZE); // defaults to 32bpp ARGB
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using (var brush = new SolidBrush(color))
        using (var badge = RoundRect(0.5f, 0.5f, SIZE - 1f, SIZE - 1f, 8f))
            g.FillPath(brush, badge);

        DrawMicGlyph(g, Color.White, SIZE / 2f, SIZE / 2f, SIZE * 0.62f, 2.8f);

        return Icon.FromHandle(b.GetHicon());
    }

    // A dictation cycle has three distinct phases. _isRecording spans the whole cycle,
    // while _activeRecorder is non-null only while the mic is actually capturing — so
    // the two together distinguish "still talking" from "waiting on Groq".
    private enum DictationPhase { Idle, Capturing, Processing }

    private DictationPhase Phase =>
        !_isRecording ? DictationPhase.Idle
        : _activeRecorder != null ? DictationPhase.Capturing
        : DictationPhase.Processing;

    private void OnHotkeyPressed(int id)
    {
        if (id == HOTKEY_ESCAPE)
        {
            if (_isRecording)
            {
                _cancelRequested = true;
                _activeRecorder?.StopNow();
            }
            return;
        }

        // Win+\ keeps the original silence auto-stop behaviour.
        _manualStopOnly = false;
        ToggleDictation();
    }

    // Single entry point for both Win+\ and a left-click on the tray icon. Must be
    // called on the UI thread — StartDictation registers a global hotkey, which is
    // thread-affine to the hotkey window's message queue (see HANDOVER.md 9.1).
    private void ToggleDictation()
    {
        switch (Phase)
        {
            case DictationPhase.Idle:
                StartDictation();
                break;

            case DictationPhase.Capturing:
                Log.Write("TOGGLE", "stop requested while capturing");
                _stopRequested = true;
                _activeRecorder?.StopNow();
                PlayDoneOnce();
                break;

            case DictationPhase.Processing:
                // Mic already stopped, audio in flight to Groq, nothing pasted yet.
                // Acknowledge the click rather than silently swallowing it, but never
                // throw away speech the user has already waited on.
                Log.Write("TOGGLE", "ignored - already processing");
                _overlay.SetStatus("⏳ Still working... (Esc to cancel)");
                break;
        }
    }

    private void StartDictation()
    {
        _isRecording = true;
        _stopRequested = false;
        _cancelRequested = false;
        _donePlayed = false;
        HotkeyWindow.RegisterHotKey(_hotkeyWindow.Handle, HOTKEY_ESCAPE, MOD_NOREPEAT, VK_ESCAPE);
        SetTrayRecording(true);

        Task.Run(async () =>
        {
            try { await RunPipeline(); }
            catch { PlaySound(_settings.SoundErrorPath); _overlay.HideOverlay(); }
            finally
            {
                _isRecording = false;
                SetTrayRecording(false);
                // RegisterHotKey/UnregisterHotKey are thread-affine to the window's message
                // queue — must run on the UI thread, not this background Task.Run thread,
                // or the OS-level registration silently never releases.
                try { _overlay.BeginInvoke(new Action(() => HotkeyWindow.UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ESCAPE))); }
                catch { }
            }
        });
    }

    // Mirrors the cycle state onto the tray icon so a click-driven toggle is visible
    // without the overlay. NotifyIcon is not a Control, so marshal via the overlay's
    // handle when called from the pipeline's background thread.
    private void SetTrayRecording(bool active)
    {
        void Apply()
        {
            try
            {
                _trayIcon.Icon = active ? _iconActive : _iconIdle;
                _trayIcon.Text = active ? TRAY_TEXT_ACTIVE : TRAY_TEXT_IDLE;
            }
            catch { }
        }

        if (_overlay.InvokeRequired)
        {
            try { _overlay.BeginInvoke(new Action(Apply)); } catch { }
        }
        else Apply();
    }

    private async Task RunPipeline()
    {
        Log.Write("=== CYCLE START ===");
        PlaySound(_settings.SoundStartPath);
        _overlay.ShowOverlay("\U0001F3A4 Listening...");

        bool manualStop = _manualStopOnly;
        Log.Write("MODE", manualStop ? "manual stop (click to end)" : "auto-stop on silence");

        using var rec = new AudioRecorder(_settings.InputDeviceIndex, autoStopOnSilence: !manualStop);
        _activeRecorder = rec;
        rec.Start();

        // Best live text we have so far (accumulates across preview calls)
        string liveText = "";

        // True once we've fired at least one successful live preview
        bool hasLivePreview = false;

        // Whether a live-preview transcription is currently in flight
        bool previewInFlight = false;

        var started = DateTime.UtcNow;

        while (!rec.IsFinished)
        {
            await Task.Delay(150);

            // Manual-stop mode waits for a second click, so a stray click would otherwise
            // hold the overlay open for the full 120s cap. If nothing was ever said,
            // give up rather than sit there listening to an empty room.
            if (manualStop && !rec.SpeechDetected
                && (DateTime.UtcNow - started).TotalSeconds > MANUAL_IDLE_TIMEOUT_SEC)
            {
                Log.Write("IDLE TIMEOUT", $"no speech within {MANUAL_IDLE_TIMEOUT_SEC}s");
                _cancelRequested = true;
                rec.StopNow();
            }

            if (_cancelRequested)
            {
                _overlay.HideOverlay();
                _activeRecorder = null;
                return;
            }

            // Fire a new live preview when:
            //   - no preview is already running
            //   - enough new audio has accumulated since the last preview was sent
            //   - total audio is at least ~0.5 s (8000 bytes) so Whisper has something to chew on
            if (!previewInFlight
                && rec.BytesSinceLastChunk >= LIVE_PREVIEW_MIN_NEW_BYTES
                && rec.TotalBytesWritten >= 8000)
            {
                previewInFlight = true;
                rec.MarkChunkSent();

                // Snapshot the WAV right now and fire off transcription concurrently
                var wavSnapshot = rec.GetCurrentWav();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var t = await Transcribe(wavSnapshot);
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            liveText = t;
                            hasLivePreview = true;
                            // Show the live text and update status to make it clear it's a preview
                            _overlay.SetText(liveText);
                            _overlay.SetStatus("\U0001F3A4 Listening...");
                        }
                    }
                    catch { /* silent — final transcription will cover it */ }
                    finally { previewInFlight = false; }
                });
            }
        }

        _activeRecorder = null;

        if (_cancelRequested)
        {
            _overlay.HideOverlay();
            return;
        }

        rec.WaitUntilDone();

        var finalWav = rec.GetFinalWav();

        // Length alone is not enough: a long recording of pure silence still passes it,
        // and Whisper answers near-silence with confident hallucinated filler.
        if (!rec.SpeechDetected)
        {
            Log.Write("NO SPEECH", $"{finalWav.Length} bytes captured, never exceeded threshold");
            PlaySound(_settings.SoundErrorPath);
            _overlay.SetStatus("❌ No speech detected");
            await Task.Delay(1500); _overlay.HideOverlay(); return;
        }

        if (finalWav.Length < 10000)
        {
            PlaySound(_settings.SoundErrorPath);
            _overlay.SetStatus("\u274C No speech detected");
            await Task.Delay(1500); _overlay.HideOverlay(); return;
        }

        _overlay.SetStatus("\U0001F50D Transcribing...");

        string raw;
        try { raw = await Transcribe(finalWav); }
        catch { raw = liveText; }

        if (string.IsNullOrWhiteSpace(raw))
        {
            // Fall back to whatever the live preview had
            if (!string.IsNullOrWhiteSpace(liveText))
                raw = liveText;
            else
            {
                PlaySound(_settings.SoundErrorPath);
                _overlay.SetStatus("\u274C No text returned");
                await Task.Delay(1500); _overlay.HideOverlay(); return;
            }
        }

        _overlay.SetText(raw);
        _overlay.SetStatus("\u2728 Cleaning up before pasting...");

        Log.Write("RAW", JsonSerializer.Serialize(raw));

        string polished = "";
        try
        {
            polished = await Polish(raw);
            Log.Write("POLISHED", JsonSerializer.Serialize(polished));
        }
        catch (Exception ex)
        {
            Log.Write("POLISH-FAILED", ex.GetType().Name + ": " + ex.Message);
            PlaySound(_settings.SoundErrorPath);
        }

        string output = !string.IsNullOrWhiteSpace(polished) ? polished : raw;
        try
        {
            output = EnforceMessageStructure(output);
            Log.Write("FORMATTED", JsonSerializer.Serialize(output));
        }
        catch (Exception ex)
        {
            Log.Write("FORMAT-FAILED", ex.GetType().Name + ": " + ex.Message);
        }
        _overlay.SetText(output);
        _overlay.SetStatus("\u2705 Done!");

        var tcs = new TaskCompletionSource();
        var sta = new Thread(() => { Clipboard.SetText(output); tcs.SetResult(); });
        sta.SetApartmentState(ApartmentState.STA); sta.Start();
        await tcs.Task;

        await Task.Delay(150);
        Log.Write("PASTE (Ctrl+V)");
        SendCtrlV();
        PlayDoneOnce();
        await Task.Delay(2000);
        _overlay.HideOverlay();
        Log.Write("=== CYCLE END ===");
    }

    private async Task<string> Transcribe(byte[] wav)
    {
        using var c = new MultipartFormDataContent();
        c.Add(new StringContent(WHISPER_MODEL), "model");
        var fc = new ByteArrayContent(wav);
        fc.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        c.Add(fc, "file", "recording.wav");
        var r = await _http.PostAsync(WHISPER_URL, c);
        r.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("text").GetString() ?? "";
    }

    private async Task<string> Polish(string text)
    {
        var systemPrompt = _settings.PolishPrompt.TrimEnd()
            + "\n\nREMINDER: Output ONLY the cleaned text. No explanations, no notes, no commentary. Stop immediately after the cleaned text.";

        var body = new
        {
            model = CHAT_MODEL,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = text }
            },
            temperature = 0.15, max_tokens = 2048
        };

        var r = await _http.PostAsync(CHAT_URL,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        r.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var result = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

        var lines = result.Split('\n');
        var trimmed = new System.Collections.Generic.List<string>();
        foreach (var line in lines)
        {
            var lower = line.TrimStart().ToLowerInvariant();
            if (lower.StartsWith("note:") || lower.StartsWith("(note") ||
                lower.StartsWith("please note") || lower.StartsWith("i have") ||
                lower.StartsWith("i've ") || lower.StartsWith("the above") ||
                lower.StartsWith("this is") || lower.StartsWith("i cleaned") ||
                lower.StartsWith("i fixed") || lower.StartsWith("changes made") ||
                lower.StartsWith("i made") || lower.StartsWith("let me know"))
                break;
            trimmed.Add(line);
        }
        return string.Join('\n', trimmed).TrimEnd();
    }

    private static readonly string[] SignOffPhrases =
    {
        "best wishes", "best regards", "kind regards", "warm regards", "all the best",
        "many thanks", "thank you so much", "thanks so much", "thank you", "thanks",
        "sincerely", "cheers", "talk soon", "take care", "regards"
    };

    private static readonly string[] TransitionWords =
    {
        "but", "so", "however", "then", "also", "thereafter", "additionally", "meanwhile"
    };

    // Deterministic typography cleanup. The AI pass is inconsistent about spacing and
    // capitalization no matter how the prompt is worded (see HANDOVER.md s8), so these
    // fixes are applied in code rather than asked for.
    private static string NormalizeTypography(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // Collapse runs of spaces/tabs, and strip trailing space on each line.
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" +$", "", RegexOptions.Multiline);

        // No space BEFORE punctuation; exactly one space AFTER it.
        text = Regex.Replace(text, @"\s+([,.!?;:])", "$1");
        text = Regex.Replace(text, @"([,;:])(?=[A-Za-z0-9])", "$1 ");
        // Sentence punctuation. The lookbehind skips initialisms - a period whose letter
        // is itself preceded by a word boundary ("u.s.a.", "a.m.") is not a sentence end.
        text = Regex.Replace(text, @"(?<!\b[A-Za-z])([.!?])(?=[A-Za-z])", "$1 ");

        // Dictation routinely yields a bare lowercase "i".
        text = Regex.Replace(text, @"(?<![A-Za-z'])i(?![A-Za-z'])", "I");

        // Capitalize the first letter of the text, of every line, and of every sentence.
        text = Regex.Replace(text, @"^[a-z]", m => m.Value.ToUpperInvariant());
        text = Regex.Replace(text, @"(?<=\n)([ \t]*)([a-z])",
            m => m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));
        text = Regex.Replace(text, @"(?<!\b[A-Za-z])([.!?]\s+)([a-z])",
            m => m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));

        // Never more than one blank line between paragraphs.
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    // Deterministic fallback formatting so greeting/body/sign-off structure and paragraph
    // breaks always land, regardless of whether the AI cleanup pass applied them.
    private static string EnforceMessageStructure(string text)
    {
        text = NormalizeTypography(text);
        if (text.Length == 0) return text;

        string greetingLine = null;
        var greetingMatch = Regex.Match(text, @"^(Hi|Hey|Hello|Dear|Greetings)\s+([A-Za-z][A-Za-z'-]{0,30})\s*[,.!]?\s*", RegexOptions.IgnoreCase);
        if (greetingMatch.Success)
        {
            var salutation = greetingMatch.Groups[1].Value;
            var name = greetingMatch.Groups[2].Value;
            greetingLine = $"{char.ToUpper(salutation[0])}{salutation.Substring(1).ToLower()} {char.ToUpper(name[0])}{name.Substring(1)},";
            text = text.Substring(greetingMatch.Length).TrimStart();
            if (text.Length > 0 && char.IsLower(text[0]))
                text = char.ToUpper(text[0]) + text.Substring(1);
        }

        // Peel the sign-off off the tail BEFORE paragraphing, so it never gets glued
        // onto the last body paragraph.
        string signOffLine = null;
        var tailMatch = Regex.Match(text, @"(?:^|(?<=[.!?])\s+|\n)\s*([A-Za-z][A-Za-z ]*?)\s*[,.!]?\s*(?:\n+\s*([A-Z][A-Za-z'-]{1,20}(?: [A-Z][A-Za-z'-]{1,20})?)\s*[.!]?)?\s*$");
        if (tailMatch.Success)
        {
            var candidate = tailMatch.Groups[1].Value.Trim();
            foreach (var phrase in SignOffPhrases)
            {
                if (!candidate.Equals(phrase, StringComparison.OrdinalIgnoreCase)) continue;
                var head = $"{char.ToUpper(candidate[0])}{candidate.Substring(1).ToLower()}";
                var name = tailMatch.Groups[2].Success ? tailMatch.Groups[2].Value.Trim() : "";
                signOffLine = name.Length > 0 ? $"{head},\n{name}" : $"{head}.";
                text = text.Substring(0, tailMatch.Index).TrimEnd();
                break;
            }
        }

        // Respect the paragraph breaks the AI pass already produced — the previous
        // version flattened them with string.Join(" ", ...) and re-split from scratch,
        // which is why well-structured output kept coming back as one block.
        var paragraphs = new System.Collections.Generic.List<string>();
        foreach (var block in Regex.Split(text, @"\n\s*\n"))
        {
            var flat = Regex.Replace(block.Replace('\n', ' '), @"\s+", " ").Trim();
            if (flat.Length == 0) continue;
            paragraphs.AddRange(SplitIntoParagraphs(flat));
        }

        var result = new System.Collections.Generic.List<string>();
        if (greetingLine != null) result.Add(greetingLine);
        result.AddRange(paragraphs);
        if (signOffLine != null) result.Add(signOffLine);

        return result.Count > 0 ? string.Join("\n\n", result) : text;
    }

    // Breaks one dense block into paragraphs of at most 3 sentences, starting a new
    // one at any transition word.
    private static System.Collections.Generic.List<string> SplitIntoParagraphs(string block)
    {
        // Same initialism guard as NormalizeTypography, shifted one char: do not treat
        // the trailing period of "u.s.a." as a sentence boundary.
        var sentences = Regex.Split(block, @"(?<!\b[A-Za-z]\.)(?<=[.!?])\s+")
            .Where(x => x.Trim().Length > 0).ToList();
        var paragraphs = new System.Collections.Generic.List<string>();
        var current = new System.Collections.Generic.List<string>();

        foreach (var raw in sentences)
        {
            var sentence = raw.Trim();
            var firstWord = Regex.Match(sentence, @"^[A-Za-z]+").Value;
            bool startsTransition = TransitionWords.Contains(firstWord, StringComparer.OrdinalIgnoreCase);
            if (current.Count > 0 && (startsTransition || current.Count >= 3))
            {
                paragraphs.Add(string.Join(" ", current));
                current.Clear();
            }
            current.Add(sentence);
        }
        if (current.Count > 0) paragraphs.Add(string.Join(" ", current));
        return paragraphs;
    }

    private static string LoadApiKey()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var p = Path.Combine(dir, "groq_key.txt");
            if (File.Exists(p)) { var k = File.ReadAllText(p).Trim(); if (!string.IsNullOrEmpty(k)) return k; }
        }

        using var dlg = new OpenFileDialog
        {
            Title = "Locate groq_key.txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            var k = File.ReadAllText(dlg.FileName).Trim();
            if (!string.IsNullOrEmpty(k))
            {
                try { File.Copy(dlg.FileName, Path.Combine(AppContext.BaseDirectory, "groq_key.txt"), overwrite: true); } catch { }
                return k;
            }
        }

        MessageBox.Show("groq_key.txt not found.", "Voice Dictation", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Environment.Exit(1); return "";
    }

    private void PlaySound(string path, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (!_settings.SoundsEnabled) { Log.Write("SOUND-SKIP (disabled)", $"line {line}"); return; }
        Log.Write("SOUND", $"{System.IO.Path.GetFileName(path)}  (line {line})");
        SoundPlayback.Play(path, _settings.SoundVolume);
    }

    // Plays the done sound at most once per dictation cycle, whichever trigger fires
    // first — stopping manually with Win+\ used to chime, then chime again on paste.
    private void PlayDoneOnce([System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (_donePlayed) { Log.Write("DONE-SUPPRESSED", $"already played (line {line})"); return; }
        _donePlayed = true;
        Log.Write("DONE-SOUND", $"line {line}");
        PlaySound(_settings.SoundDonePath);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static void SendCtrlV()
    {
        keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x56, 0, 0, UIntPtr.Zero);
        keybd_event(0x56, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero);
    }
}

sealed class IgnoreDisposeStream : Stream
{
    private readonly Stream _s;
    public IgnoreDisposeStream(Stream s) => _s = s;
    public override bool CanRead => _s.CanRead;
    public override bool CanSeek => _s.CanSeek;
    public override bool CanWrite => _s.CanWrite;
    public override long Length => _s.Length;
    public override long Position { get => _s.Position; set => _s.Position = value; }
    public override void Flush() => _s.Flush();
    public override int Read(byte[] b, int o, int c) => _s.Read(b, o, c);
    public override long Seek(long o, SeekOrigin r) => _s.Seek(o, r);
    public override void SetLength(long v) => _s.SetLength(v);
    public override void Write(byte[] b, int o, int c) => _s.Write(b, o, c);
    protected override void Dispose(bool d) { }
}

sealed class HotkeyWindow : NativeWindow
{
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, int m, int vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    private readonly Action<int> _cb;
    public HotkeyWindow(Action<int> cb) { _cb = cb; CreateHandle(new CreateParams()); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312) _cb(m.WParam.ToInt32());
        base.WndProc(ref m);
    }
}
