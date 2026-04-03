// ============================================================================
// VoiceDictation — Win+\ to record, auto-stops on silence, transcribes & polishes
// C# / .NET 8 / Windows Forms / NAudio
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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

// ============================================================================
// Settings
// ============================================================================
class AppSettings
{
    public string SoundStart { get; set; } = "Speech On.wav";
    public string SoundDone { get; set; } = "Speech Off.wav";
    public string SoundError { get; set; } = "Windows Foreground.wav";
    public bool SoundsEnabled { get; set; } = true;
    public float OverlayFontSize { get; set; } = 10.5f;
    public int OverlayBgR { get; set; } = 24;
    public int OverlayBgG { get; set; } = 24;
    public int OverlayBgB { get; set; } = 28;
    public int OverlayOpacity { get; set; } = 95;
    public int TextColorR { get; set; } = 230;
    public int TextColorG { get; set; } = 230;
    public int TextColorB { get; set; } = 230;
    public int StatusColorR { get; set; } = 120;
    public int StatusColorG { get; set; } = 180;
    public int StatusColorB { get; set; } = 255;
    public int InputDeviceIndex { get; set; } = -1;
    public string PolishPrompt { get; set; } =
        "You are a dictation cleanup assistant. Fix punctuation, capitalization, and sentence structure. Do light rephrasing for clarity but keep the user's tone and meaning exactly. Output ONLY the cleaned text, nothing else.";

    public static readonly string MediaDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    public Color OverlayBgColor => Color.FromArgb(OverlayBgR, OverlayBgG, OverlayBgB);
    public Color TextColor => Color.FromArgb(TextColorR, TextColorG, TextColorB);
    public Color StatusColor => Color.FromArgb(StatusColorR, StatusColorG, StatusColorB);
    public string SoundStartPath => Path.Combine(MediaDir, SoundStart);
    public string SoundDonePath => Path.Combine(MediaDir, SoundDone);
    public string SoundErrorPath => Path.Combine(MediaDir, SoundError);

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
    private readonly ComboBox _cmbStart, _cmbDone, _cmbError, _cmbDevice;
    private readonly CheckBox _chkSounds;
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

        Text = "Voice Dictation — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(520, 648);

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
        Controls.Add(_chkSounds); y += 26;

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
        var btnSave = new Button { Text = "Save", Size = new Size(80, 30), Location = new Point(340, y) };
        btnSave.Click += (_, _) => { DoSave(); DialogResult = DialogResult.OK; Close(); };
        Controls.Add(btnSave);

        var btnCancel = new Button { Text = "Cancel", Size = new Size(80, 30), Location = new Point(428, y) };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnCancel);
    }

    private void RefreshPreview()
    {
        float fs = (float)_numFont.Value;
        int alpha = (int)(_trkOpacity.Value / 100.0 * 255);
        _prevPanel.BackColor = Color.FromArgb(alpha, _bgColor.R, _bgColor.G, _bgColor.B);
        _prevStatus.ForeColor = _statusColor;
        _prevStatus.Font = new Font("Segoe UI", Math.Max(fs - 1.5f, 8f), FontStyle.Bold);
        _prevStatus.Text = "\U0001F3A4  Listening...";
        _prevText.ForeColor = _textColor;
        _prevText.Font = new Font("Segoe UI", fs);
        _prevText.Text = "This is how your transcript will look.";
    }

    private void DoSave()
    {
        _s.SoundsEnabled = _chkSounds.Checked;
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
            // Match stored filename (e.g. "Windows Notify.wav") OR display name without extension
            if (string.Equals(_wavNames[i], currentFile, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(_wavNames[i]), currentFile, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_wavNames[i], Path.GetFileName(currentFile), StringComparison.OrdinalIgnoreCase))
                selIdx = i;
        }
        // If no match found (e.g. old full-path setting), try matching just the filename portion
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
        Controls.Add(cmb);

        // Play button
        var btn = new Button
        {
            Text = "\u25B6", Size = new Size(26, 24), Location = new Point(x + 384, y),
            Font = new Font("Segoe UI", 7f)
        };
        btn.Click += (_, _) =>
        {
            var path = Path.Combine(AppSettings.MediaDir, SelectedWav(cmb));
            try { if (File.Exists(path)) new SoundPlayer(path).Play(); } catch { }
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

    public OverlayForm(AppSettings s)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(440, 180);

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

        _panel.Controls.Add(_textLabel);
        _panel.Controls.Add(_statusLabel);
        Controls.Add(_panel);
        ApplyColors(s);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public void ApplyColors(AppSettings s)
    {
        if (InvokeRequired) { Invoke(() => ApplyColors(s)); return; }
        BackColor = s.OverlayBgColor;
        _panel.BackColor = s.OverlayBgColor;
        Opacity = s.OverlayOpacity / 100.0;
        _statusLabel.ForeColor = s.StatusColor;
        _statusLabel.Font = new Font("Segoe UI", Math.Max(s.OverlayFontSize - 1.5f, 8f), FontStyle.Bold);
        _textLabel.ForeColor = s.TextColor;
        _textLabel.Font = new Font("Segoe UI", s.OverlayFontSize);
    }

    public void ShowOverlay(string status)
    {
        if (InvokeRequired) { Invoke(() => ShowOverlay(status)); return; }
        _statusLabel.Text = status;
        _textLabel.Text = "";
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;
        Show();
    }

    public void SetText(string text)
    {
        if (InvokeRequired) { Invoke(() => SetText(text)); return; }
        _textLabel.Text = text;
    }

    public void SetStatus(string status)
    {
        if (InvokeRequired) { Invoke(() => SetStatus(status)); return; }
        _statusLabel.Text = status;
    }

    public void HideOverlay()
    {
        if (InvokeRequired) { Invoke(() => HideOverlay()); return; }
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
    private bool _speechStarted;
    private double _silenceAccum;
    private DateTime _startTime;
    private int _totalBytesWritten;
    private readonly object _lock = new();

    public bool IsFinished => _done.IsSet;
    public int TotalBytesWritten { get { lock (_lock) return _totalBytesWritten; } }

    public AudioRecorder(int deviceIndex = -1)
    {
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
        else if (_speechStarted && elapsed > 2.0)
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

    private const string WHISPER_URL   = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string WHISPER_MODEL = "whisper-large-v3-turbo";
    private const string CHAT_URL      = "https://api.groq.com/openai/v1/chat/completions";
    private const string CHAT_MODEL    = "llama-3.3-70b-versatile";
    private const double CHUNK_SEC     = 2.0;

    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly HttpClient _http;
    private readonly OverlayForm _overlay;
    private AppSettings _settings;
    private volatile bool _isRecording;

    private const int HOTKEY_ID = 1, MOD_WIN = 0x0008, VK_OEM_5 = 0xDC;

    public TrayApp()
    {
        _settings = AppSettings.Load();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GROQ_API_KEY);
        _overlay = new OverlayForm(_settings);

        _trayIcon = new NotifyIcon
        {
            Icon = LoadMicIcon(), Text = "Voice Dictation (Win+\\)",
            Visible = true, ContextMenuStrip = BuildMenu()
        };

        _hotkeyWindow = new HotkeyWindow(OnHotkeyPressed);
        HotkeyWindow.RegisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID, MOD_WIN, VK_OEM_5);
        PlaySound(_settings.SoundStartPath);
    }

    private ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip();
        m.Items.Add("Voice Dictation (Win+\\)").Enabled = false;
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Settings...", null, (_, _) => OpenSettings());
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Quit", null, (_, _) =>
        {
            HotkeyWindow.UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID);
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

    private static Icon LoadMicIcon()
    {
        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        foreach (var (file, idx) in new[] { ("shell32.dll", 243), ("imageres.dll", 108) })
        {
            try
            {
                var h = ExtractIcon(IntPtr.Zero, Path.Combine(sys, file), idx);
                if (h != IntPtr.Zero) return Icon.FromHandle(h);
            }
            catch { }
        }
        return DrawMicIcon();
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string file, int idx);

    private static Icon DrawMicIcon()
    {
        var b = new Bitmap(32, 32);
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
        using var pen = new Pen(Color.White, 2f);
        using var brush = new SolidBrush(Color.White);
        g.FillEllipse(brush, 11, 3, 10, 10); g.FillRectangle(brush, 11, 8, 10, 8);
        g.FillEllipse(brush, 11, 12, 10, 6); g.DrawArc(pen, 7, 10, 18, 14, 0, 180);
        g.DrawLine(pen, 16, 24, 16, 28); g.DrawLine(pen, 10, 28, 22, 28);
        return Icon.FromHandle(b.GetHicon());
    }

    private void OnHotkeyPressed()
    {
        if (_isRecording) return;
        _isRecording = true;
        Task.Run(async () =>
        {
            try { await RunPipeline(); }
            catch { PlaySound(_settings.SoundErrorPath); _overlay.HideOverlay(); }
            finally { _isRecording = false; }
        });
    }

    private async Task RunPipeline()
    {
        PlaySound(_settings.SoundStartPath);
        _overlay.ShowOverlay("\U0001F3A4  Listening...");

        using var rec = new AudioRecorder(_settings.InputDeviceIndex);
        rec.Start();

        string liveText = "";
        var lastChunk = DateTime.UtcNow;

        while (!rec.IsFinished)
        {
            await Task.Delay(200);
            if ((DateTime.UtcNow - lastChunk).TotalSeconds >= CHUNK_SEC && rec.TotalBytesWritten > 16000)
            {
                lastChunk = DateTime.UtcNow;
                try
                {
                    var wav = rec.GetCurrentWav();
                    if (wav.Length > 10000)
                    {
                        var t = await Transcribe(wav);
                        if (!string.IsNullOrWhiteSpace(t)) { liveText = t; _overlay.SetText(liveText); }
                    }
                }
                catch { }
            }
        }

        rec.WaitUntilDone();
        var finalWav = rec.GetFinalWav();

        if (finalWav.Length < 10000)
        {
            PlaySound(_settings.SoundErrorPath);
            _overlay.SetStatus("\u274C  No speech detected");
            await Task.Delay(1500); _overlay.HideOverlay(); return;
        }

        _overlay.SetStatus("\U0001F50D  Transcribing...");
        string raw;
        try { raw = await Transcribe(finalWav); } catch { raw = liveText; }

        if (string.IsNullOrWhiteSpace(raw))
        {
            PlaySound(_settings.SoundErrorPath);
            _overlay.SetStatus("\u274C  No text returned");
            await Task.Delay(1500); _overlay.HideOverlay(); return;
        }

        _overlay.SetText(raw);
        _overlay.SetStatus("\u2728  Cleaning up before pasting...");

        string polished = "";
        try { polished = await Polish(raw); } catch { PlaySound(_settings.SoundErrorPath); }

        string output = !string.IsNullOrWhiteSpace(polished) ? polished : raw;
        _overlay.SetText(output);
        _overlay.SetStatus("\u2705  Done!");

        var tcs = new TaskCompletionSource();
        var sta = new Thread(() => { Clipboard.SetText(output); tcs.SetResult(); });
        sta.SetApartmentState(ApartmentState.STA); sta.Start();
        await tcs.Task;

        await Task.Delay(150);
        SendCtrlV();
        PlaySound(_settings.SoundDonePath);
        await Task.Delay(2000);
        _overlay.HideOverlay();
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
        var body = new
        {
            model = CHAT_MODEL,
            messages = new[]
            {
                new { role = "system", content = _settings.PolishPrompt },
                new { role = "user", content = text }
            },
            temperature = 0.3, max_tokens = 2048
        };
        var r = await _http.PostAsync(CHAT_URL,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        r.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    private static string LoadApiKey()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var p = Path.Combine(dir, "groq_key.txt");
            if (File.Exists(p)) { var k = File.ReadAllText(p).Trim(); if (!string.IsNullOrEmpty(k)) return k; }
        }
        MessageBox.Show("groq_key.txt not found.", "Voice Dictation", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Environment.Exit(1); return "";
    }

    private void PlaySound(string path)
    {
        if (!_settings.SoundsEnabled) return;
        try { if (File.Exists(path)) new SoundPlayer(path).Play(); } catch { }
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
    private readonly Action _cb;
    public HotkeyWindow(Action cb) { _cb = cb; CreateHandle(new CreateParams()); }
    protected override void WndProc(ref Message m) { if (m.Msg == 0x0312) _cb(); base.WndProc(ref m); }
}
