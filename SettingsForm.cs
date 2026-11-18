using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NAudio.Wave;

// The app's types (AppSettings, OverlayForm, SoundPlayback, AppVersion) live in this
// namespace, declared file-scoped in voice-dictation-program.cs. MessageFormatter.cs
// and SpeechIdleTracker.cs sit in the global namespace instead, which works because
// global types are visible from inside a namespace — but not the other way round.
namespace VoiceDictation;

// ============================================================================
// Settings window
//
// Borderless and rounded to match the overlay. Sections collapse, and the window
// sizes itself to whatever is open — which is what keeps a scrollbar off screen
// rather than trying to style the native one, which cannot be themed.
//
// The overlay preview is a REAL OverlayForm, not a mock: opacity makes it a
// layered window composited by DWM against whatever is behind it, and a Panel in
// a dialog has no such compositing. Judging an opacity slider against a fake is
// precisely the problem this replaces.
// ============================================================================

// Native TrackBar always paints as a light Windows control and cannot be themed.
sealed class FlatSlider : Control
{
    private int _min, _max = 100, _value = 50;
    private bool _dragging;

    public event Action ValueChanged;

    public int Minimum { get => _min; set { _min = value; Invalidate(); } }
    public int Maximum { get => _max; set { _max = value; Invalidate(); } }

    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke();
        }
    }

    public FlatSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Card;
        Height = 26;
        Cursor = Cursors.Hand;
    }

    private int ThumbX()
    {
        float t = _max > _min ? (_value - _min) / (float)(_max - _min) : 0f;
        return (int)Math.Round(9 + t * (Width - 18));
    }

    private void SetFromMouse(int x)
    {
        float t = Math.Clamp((x - 9f) / Math.Max(1, Width - 18), 0f, 1f);
        Value = (int)Math.Round(_min + t * (_max - _min));
    }

    protected override void OnMouseDown(MouseEventArgs e) { _dragging = true; SetFromMouse(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) SetFromMouse(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        int cy = Height / 2, tx = ThumbX();
        using (var track = new SolidBrush(Theme.Field))
            g.FillRectangle(track, 9, cy - 2, Math.Max(0, Width - 18), 4);
        using (var fill = new SolidBrush(Theme.Accent))
            g.FillRectangle(fill, 9, cy - 2, Math.Max(0, tx - 9), 4);
        using (var thumb = new SolidBrush(Theme.Accent))
            g.FillEllipse(thumb, tx - 6, cy - 6, 12, 12);
        using (var ring = new Pen(Color.FromArgb(225, 255, 255, 255), 1.4f))
            g.DrawEllipse(ring, tx - 6, cy - 6, 12, 12);
    }
}

// A collapsible titled section. Content is laid out absolutely at a known height;
// collapsing just hides it and shrinks the section, and the owning FlowLayoutPanel
// reflows everything below automatically.
sealed class Section : Panel
{
    public const int HEADER_H = 34;

    private readonly Panel _head;
    private readonly Label _chevron;
    private readonly int _contentH;
    private bool _open;

    public Panel Content { get; }
    public event Action Toggled;

    public Section(string title, int width, int contentHeight, bool open = false)
    {
        _contentH = contentHeight;
        _open = open;

        Width = width;
        BackColor = Theme.Card;
        Margin = new Padding(0, 0, 0, 8);

        _head = new Panel
        {
            Location = new Point(0, 0), Size = new Size(width, HEADER_H),
            BackColor = Theme.Card, Cursor = Cursors.Hand
        };

        _chevron = new Label
        {
            Text = open ? "▾" : "▸",
            AutoSize = true, Location = new Point(12, 9),
            ForeColor = Theme.Accent, BackColor = Theme.Card, Font = Theme.UI(9f)
        };

        var caption = new Label
        {
            Text = title.ToUpperInvariant(), AutoSize = true, Location = new Point(30, 10),
            ForeColor = Theme.Accent, BackColor = Theme.Card, Font = Theme.UI(8f, FontStyle.Bold)
        };

        foreach (var c in new Control[] { _head, _chevron, caption })
        {
            c.Click += (_, _) => Toggle();
            c.MouseEnter += (_, _) => SetHover(true);
            c.MouseLeave += (_, _) => SetHover(false);
        }

        _head.Controls.Add(_chevron);
        _head.Controls.Add(caption);
        Controls.Add(_head);

        Content = new Panel
        {
            Location = new Point(0, HEADER_H),
            Size = new Size(width, contentHeight),
            BackColor = Theme.Card,
            Visible = open
        };
        Controls.Add(Content);

        Height = HEADER_H + (open ? contentHeight : 0);

        Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.CardEdge);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
    }

    private void SetHover(bool on)
    {
        var c = on ? Theme.CardHover : Theme.Card;
        _head.BackColor = c;
        foreach (Control ch in _head.Controls) ch.BackColor = c;
    }

    public void Toggle() => SetOpen(!_open);

    public void SetOpen(bool open)
    {
        _open = open;
        _chevron.Text = open ? "▾" : "▸";
        Content.Visible = open;
        Height = HEADER_H + (open ? _contentH : 0);
        Toggled?.Invoke();
    }
}

sealed class SettingsForm : Form
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2;

    private const string CUSTOM_SCHEME = "Custom";
    private const int CARD_W = 520;

    private readonly AppSettings _s;
    private readonly string[] _wavNames;
    private readonly Dictionary<ComboBox, string> _missingSounds = new();

    private readonly FlowLayoutPanel _body;
    private readonly List<Section> _sections = new();

    private readonly ComboBox _cmbDevice, _cmbScheme, _cmbStart, _cmbDone, _cmbError;
    private readonly CheckBox _chkSounds;
    private readonly NumericUpDown _numSilence, _numTitleFont, _numTextFont;
    private readonly FlatSlider _sldVolume, _sldOpacity;
    private readonly Label _lblVolume, _lblOpacity;
    private readonly TextBox _txtPrompt;
    private readonly Button _btnDark, _btnLight;
    private bool _darkTheme;

    private Color _bgColor, _textColor, _statusColor;
    private bool _applyingScheme;

    private WaveOutEvent _volPreviewOut;
    private AudioFileReader _volPreviewReader;
    private OverlayForm _preview;

    private const string SAMPLE =
        "Hi Mark, thanks for jumping on the call earlier. So I went back through the "
        + "numbers and the panel yield is lower than we quoted.";

    public SettingsForm(AppSettings settings)
    {
        _s = settings;
        _bgColor = _s.OverlayBgColor;
        _textColor = _s.TextColor;
        _statusColor = _s.StatusColor;
        _wavNames = GatherSounds();
        _darkTheme = _s.DarkTheme;

        Text = "Voice Dictation — Settings";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.UI();
        ClientSize = new Size(CARD_W + 40, 420);
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };

        BuildHeader();
        BuildFooter();

        _body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Bg,
            Padding = new Padding(20, 10, 20, 10)
        };
        Controls.Add(_body);
        _body.BringToFront();

        // Order: the things tuned often first, the set-once ones last.
        var secLook = AddSection("Appearance", 62);
        var secStop = AddSection("Finish Dictation", 86);
        var secSnd = AddSection("Sounds", 212);
        var secOv = AddSection("Overlay", 178);
        var secMic = AddSection("Microphone", 54);
        var secAi = AddSection("AI Cleanup Prompt", 150);

        // ---- Finish Dictation ----
        _numSilence = Num(secStop.Content, 16, 10, 62, 0, 30, Math.Clamp(_s.SilenceTimeoutSec, 0, 30));
        Hint(secStop.Content, "seconds with no words spoken", 86, 14);
        Hint(secStop.Content, "0 = never stop on its own; end it with a click or the hotkey.",
             16, 42, Theme.Muted);

        // ---- Appearance ----
        Hint(secLook.Content, "Theme", 16, 18);
        _btnDark = FlatButton("Dark", 84, Theme.IsDark);
        _btnDark.Location = new Point(76, 14);
        _btnDark.Click += (_, _) => SetTheme(true);
        secLook.Content.Controls.Add(_btnDark);

        _btnLight = FlatButton("Light", 84, !Theme.IsDark);
        _btnLight.Location = new Point(166, 14);
        _btnLight.Click += (_, _) => SetTheme(false);
        secLook.Content.Controls.Add(_btnLight);

        Hint(secLook.Content, "Applies to every window, including the overlay.", 262, 18, Theme.Muted);

        // ---- Sounds ----
        var cs = secSnd.Content;
        _chkSounds = new CheckBox
        {
            Text = "Enable sounds", Checked = _s.SoundsEnabled,
            Location = new Point(16, 10), AutoSize = true,
            ForeColor = Theme.Text, BackColor = Theme.Card,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        cs.Controls.Add(_chkSounds);

        Hint(cs, "Scheme", 16, 44);
        _cmbScheme = Combo(cs, 76, 40, 180);
        _cmbScheme.Items.Add(CUSTOM_SCHEME);
        foreach (var sc in AppSettings.SoundSchemes) _cmbScheme.Items.Add(sc.Name);
        _cmbScheme.SelectedItem = _cmbScheme.Items.Contains(_s.SoundScheme) ? _s.SoundScheme : CUSTOM_SCHEME;
        _cmbScheme.SelectedIndexChanged += (_, _) => ApplyScheme();

        var bPrev = FlatButton("Preview", 76, false);
        bPrev.Location = new Point(266, 39);
        bPrev.Click += (_, _) => PreviewScheme();
        cs.Controls.Add(bPrev);

        Hint(cs, "Volume", 16, 78);
        _sldVolume = new FlatSlider
        {
            Location = new Point(76, 72), Size = new Size(320, 26),
            Minimum = 0, Maximum = 100, Value = Math.Clamp(_s.SoundVolume, 0, 100)
        };
        _lblVolume = Hint(cs, _s.SoundVolume + "%", 406, 78);
        _sldVolume.ValueChanged += () =>
        {
            _lblVolume.Text = _sldVolume.Value + "%";
            PreviewVolume(_sldVolume.Value);
        };
        cs.Controls.Add(_sldVolume);

        Hint(cs, "Start", 16, 114);
        _cmbStart = SoundCombo(cs, _s.SoundStart, 76, 110);
        Hint(cs, "Done", 16, 146);
        _cmbDone = SoundCombo(cs, _s.SoundDone, 76, 142);
        Hint(cs, "Error", 16, 178);
        _cmbError = SoundCombo(cs, _s.SoundError, 76, 174);

        // ---- Overlay ----
        var co = secOv.Content;
        Hint(co, "Title size", 16, 14);
        _numTitleFont = Num(co, 86, 10, 64, 7, 24, (decimal)_s.OverlayTitleFontSize);
        _numTitleFont.DecimalPlaces = 1; _numTitleFont.Increment = 0.5m;
        _numTitleFont.ValueChanged += (_, _) => RefreshPreview();

        Hint(co, "Text size", 170, 14);
        _numTextFont = Num(co, 236, 10, 64, 7, 24, (decimal)_s.OverlayFontSize);
        _numTextFont.DecimalPlaces = 1; _numTextFont.Increment = 0.5m;
        _numTextFont.ValueChanged += (_, _) => RefreshPreview();

        Hint(co, "Opacity", 16, 50);
        _sldOpacity = new FlatSlider
        {
            Location = new Point(86, 44), Size = new Size(286, 26),
            Minimum = 20, Maximum = 100, Value = Math.Clamp(_s.OverlayOpacity, 20, 100)
        };
        _lblOpacity = Hint(co, _s.OverlayOpacity + "%", 382, 50);
        _sldOpacity.ValueChanged += () =>
        {
            _lblOpacity.Text = _sldOpacity.Value + "%";
            RefreshPreview();
        };
        co.Controls.Add(_sldOpacity);

        Hint(co, "Background", 16, 86);
        Swatch(co, _bgColor, 100, 82, c => { _bgColor = c; RefreshPreview(); });
        Hint(co, "Text", 176, 86);
        Swatch(co, _textColor, 222, 82, c => { _textColor = c; RefreshPreview(); });
        Hint(co, "Status", 300, 86);
        Swatch(co, _statusColor, 352, 82, c => { _statusColor = c; RefreshPreview(); });

        Hint(co, "Changes show on the real overlay as you make them.", 16, 118, Theme.Muted);

        var bShow = FlatButton("Show overlay preview", 164, false);
        bShow.Location = new Point(16, 140);
        bShow.Click += (_, _) => TogglePreview();
        co.Controls.Add(bShow);

        // ---- Microphone ----
        _cmbDevice = Combo(secMic.Content, 16, 12, CARD_W - 32);
        _cmbDevice.Items.Add("(System default)");
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            _cmbDevice.Items.Add(WaveInEvent.GetCapabilities(i).ProductName);
        _cmbDevice.SelectedIndex = Math.Clamp(_s.InputDeviceIndex + 1, 0, _cmbDevice.Items.Count - 1);

        // ---- AI prompt ----
        Hint(secAi.Content, "How the AI cleans up dictated speech before pasting.", 16, 10, Theme.Muted);
        _txtPrompt = new TextBox
        {
            Location = new Point(16, 34), Size = new Size(CARD_W - 32, 100),
            Multiline = true, ScrollBars = ScrollBars.Vertical, Text = _s.PolishPrompt,
            BackColor = Theme.Field, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle, Font = Theme.UI(9.5f)
        };
        secAi.Content.Controls.Add(_txtPrompt);

        Resize += (_, _) => ApplyRoundedRegion();
        Shown += (_, _) => { ActiveControl = null; FitToContent(); };
        FormClosed += (_, _) => { StopVolumePreview(); ClosePreview(); };

        FitToContent();
    }

    // ------------------------------------------------------------------ chrome

    private void BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Bg };
        header.Paint += (_, e) =>
        {
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, 20, header.Height - 2, header.Width - 40, 2);
        };

        var title = new Label
        {
            Text = "Voice Dictation", AutoSize = true, Location = new Point(20, 10),
            Font = Theme.UI(12.5f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Bg
        };
        var ver = new Label
        {
            Text = AppVersion.Current, AutoSize = true, Location = new Point(22, 31),
            Font = Theme.UI(8f), ForeColor = Theme.Muted, BackColor = Theme.Bg
        };

        // Borderless windows have no caption to drag, so forward the drag to the OS.
        foreach (var c in new Control[] { header, title, ver })
            c.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            };

        var close = new CloseCircle
        {
            Size = new Size(30, 30),
            Location = new Point(ClientSize.Width - 42, 11),
            CircleColor = Theme.Field
        };
        close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        header.Controls.Add(close);
        header.Controls.Add(title);
        header.Controls.Add(ver);
        Controls.Add(header);
    }

    private void BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg };

        var bPrev = FlatButton("Show overlay preview", 164, false);
        bPrev.Location = new Point(20, 12);
        bPrev.Click += (_, _) => TogglePreview();
        footer.Controls.Add(bPrev);

        var bSave = FlatButton("Save", 88, true);
        bSave.Location = new Point(ClientSize.Width - 108, 12);
        bSave.Click += (_, _) => { DoSave(); DialogResult = DialogResult.OK; Close(); };
        footer.Controls.Add(bSave);

        var bCancel = FlatButton("Cancel", 88, false);
        bCancel.Location = new Point(ClientSize.Width - 204, 12);
        bCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        footer.Controls.Add(bCancel);

        Controls.Add(footer);
    }

    private Section AddSection(string title, int contentHeight)
    {
        var s = new Section(title, CARD_W, contentHeight);
        s.Toggled += FitToContent;
        _sections.Add(s);
        _body.Controls.Add(s);
        return s;
    }

    // Grow or shrink the window to whatever is expanded, so no scrollbar is needed
    // in the common case. Clamped to the screen; AutoScroll covers the rest.
    private void FitToContent()
    {
        int content = _sections.Sum(s => s.Height + s.Margin.Bottom)
                      + _body.Padding.Top + _body.Padding.Bottom;
        int chrome = 52 + 56;
        var wa = Screen.FromControl(this).WorkingArea;
        int target = Math.Clamp(content + chrome, 260, (int)(wa.Height * 0.9));

        if (ClientSize.Height != target) ClientSize = new Size(ClientSize.Width, target);
        ApplyRoundedRegion();
    }

    private void ApplyRoundedRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = Theme.RoundRect(new Rectangle(0, 0, Width, Height), 12);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using var pen = new Pen(Theme.CardEdge, 1f);
        e.Graphics.DrawPath(pen, path);
    }

    // ------------------------------------------------------------------ widgets

    private static string[] GatherSounds()
    {
        var names = Array.Empty<string>();
        if (Directory.Exists(AppSettings.MediaDir))
            names = Directory.GetFiles(AppSettings.MediaDir, "*.wav")
                .Select(Path.GetFileName).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();

        if (Directory.Exists(AppSettings.LocalSoundsDir))
        {
            var local = Directory.GetFiles(AppSettings.LocalSoundsDir)
                .Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
            if (local.Length > 0) names = local.Concat(names).ToArray();
        }
        return names;
    }

    private static Label Hint(Control parent, string text, int x, int y, Color? color = null)
    {
        var l = new Label
        {
            Text = text, AutoSize = true, Location = new Point(x, y),
            ForeColor = color ?? Theme.Text, BackColor = Theme.Card, Font = Theme.UI()
        };
        parent.Controls.Add(l);
        return l;
    }

    private static ComboBox Combo(Control parent, int x, int y, int w)
    {
        var c = new ComboBox
        {
            Location = new Point(x, y), Size = new Size(w, 24),
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field, ForeColor = Theme.Text,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 18
        };
        // Owner-draw, else the dropdown list paints with system colours.
        c.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Accent : Theme.Field))
                e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, c.Items[e.Index]?.ToString() ?? "", Theme.UI(),
                e.Bounds, sel ? Color.White : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        parent.Controls.Add(c);
        return c;
    }

    private static NumericUpDown Num(Control parent, int x, int y, int w,
                                     decimal min, decimal max, decimal val)
    {
        var n = new NumericUpDown
        {
            Location = new Point(x, y), Size = new Size(w, 24),
            Minimum = min, Maximum = max, Value = Math.Clamp(val, min, max),
            BackColor = Theme.Field, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle, Font = Theme.UI()
        };
        parent.Controls.Add(n);
        return n;
    }

    private static Button FlatButton(string text, int width, bool primary)
    {
        var b = new Button
        {
            Text = text, Size = new Size(width, 30),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = primary ? Theme.Accent : Theme.Field,
            ForeColor = primary ? Color.White : Theme.Text,
            Font = Theme.UI(9f, primary ? FontStyle.Bold : FontStyle.Regular)
        };
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.BorderColor = Theme.CardEdge;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(112, 176, 255) : Theme.CardEdge;
        return b;
    }

    private Panel Swatch(Control parent, Color c, int x, int y, Action<Color> onChange)
    {
        var p = new Panel
        {
            Location = new Point(x, y), Size = new Size(52, 22),
            BackColor = c, Cursor = Cursors.Hand
        };
        p.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.CardEdge);
            e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
        };
        p.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = p.BackColor, FullOpen = true };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            p.BackColor = dlg.Color;
            onChange(dlg.Color);
        };
        parent.Controls.Add(p);
        return p;
    }

    // ------------------------------------------------------------------ sounds

    private ComboBox SoundCombo(Control parent, string current, int x, int y)
    {
        var c = Combo(parent, x, y, 320);
        int sel = -1;
        for (int i = 0; i < _wavNames.Length; i++)
        {
            c.Items.Add(Path.GetFileNameWithoutExtension(_wavNames[i]));
            if (string.Equals(_wavNames[i], Path.GetFileName(current), StringComparison.OrdinalIgnoreCase))
                sel = i;
        }

        // If the stored sound is not on this machine, show it anyway rather than
        // silently selecting the first entry — otherwise opening Settings and pressing
        // Save would quietly replace the user's choice with whatever sorts first.
        if (sel < 0 && !string.IsNullOrEmpty(current))
        {
            c.Items.Add(Path.GetFileNameWithoutExtension(current) + "  (missing)");
            _missingSounds[c] = current;
            sel = c.Items.Count - 1;
        }
        if (c.Items.Count > 0) c.SelectedIndex = Math.Max(sel, 0);

        c.SelectedIndexChanged += (_, _) =>
        {
            if (_applyingScheme || _cmbScheme == null) return;
            _cmbScheme.SelectedItem = CUSTOM_SCHEME;
        };

        var play = FlatButton("▶", 28, false);
        play.Location = new Point(x + 328, y - 1);
        play.Font = Theme.UI(7f);
        play.Click += (_, _) => SoundPlayback.Play(ResolveSoundPath(SelectedWav(c)), _sldVolume.Value);
        parent.Controls.Add(play);
        return c;
    }

    private static string ResolveSoundPath(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var local = Path.Combine(AppSettings.LocalSoundsDir, name);
        return File.Exists(local) ? local : Path.Combine(AppSettings.MediaDir, name);
    }

    private string SelectedWav(ComboBox cmb)
    {
        if (cmb.SelectedIndex >= 0 && cmb.SelectedIndex < _wavNames.Length)
            return _wavNames[cmb.SelectedIndex];
        return _missingSounds.TryGetValue(cmb, out var stored) ? stored : "";
    }

    private static void SelectWav(ComboBox cmb, string wavName)
    {
        var target = Path.GetFileNameWithoutExtension(wavName);
        for (int i = 0; i < cmb.Items.Count; i++)
            if (string.Equals(cmb.Items[i] as string, target, StringComparison.OrdinalIgnoreCase))
            { cmb.SelectedIndex = i; return; }
    }

    private void ApplyScheme()
    {
        var name = _cmbScheme.SelectedItem as string;
        if (string.IsNullOrEmpty(name) || name == CUSTOM_SCHEME) return;

        foreach (var sc in AppSettings.SoundSchemes)
        {
            if (sc.Name != name) continue;
            _applyingScheme = true;
            try
            {
                SelectWav(_cmbStart, sc.Start);
                SelectWav(_cmbDone, sc.Done);
                SelectWav(_cmbError, sc.Error);
            }
            finally { _applyingScheme = false; }
            PreviewScheme();
            return;
        }
    }

    private void PreviewScheme()
    {
        StopVolumePreview();
        int vol = _sldVolume.Value;
        SoundPlayback.Play(ResolveSoundPath(SelectedWav(_cmbStart)), vol);
        var t = new System.Windows.Forms.Timer { Interval = 900 };
        t.Tick += (_, _) =>
        {
            t.Stop(); t.Dispose();
            SoundPlayback.Play(ResolveSoundPath(SelectedWav(_cmbDone)), vol);
        };
        t.Start();
    }

    private void PreviewVolume(int volumePercent)
    {
        StopVolumePreview();
        try
        {
            var path = ResolveSoundPath(SelectedWav(_cmbStart));
            if (!File.Exists(path)) return;
            _volPreviewReader = new AudioFileReader(path)
            { Volume = Math.Clamp(volumePercent, 0, 100) / 100f };
            _volPreviewOut = new WaveOutEvent();
            _volPreviewOut.Init(_volPreviewReader);
            _volPreviewOut.Play();
        }
        catch { }
    }

    private void StopVolumePreview()
    {
        try { _volPreviewOut?.Stop(); _volPreviewOut?.Dispose(); } catch { }
        try { _volPreviewReader?.Dispose(); } catch { }
        _volPreviewOut = null; _volPreviewReader = null;
    }

    // ------------------------------------------------------------------ preview

    private AppSettings PendingSettings() => new AppSettings
    {
        OverlayFontSize = (float)_numTextFont.Value,
        OverlayTitleFontSize = (float)_numTitleFont.Value,
        OverlayOpacity = _sldOpacity.Value,
        OverlayBgR = _bgColor.R, OverlayBgG = _bgColor.G, OverlayBgB = _bgColor.B,
        TextColorR = _textColor.R, TextColorG = _textColor.G, TextColorB = _textColor.B,
        StatusColorR = _statusColor.R, StatusColorG = _statusColor.G, StatusColorB = _statusColor.B,
    };

    // Switching theme re-tints dozens of stock controls whose colours were assigned
    // at construction. Rather than walking the tree and guessing each one's role,
    // save what is on screen and rebuild the window — DialogResult.Retry tells
    // TrayApp to reopen it. The overlay adopts the matching palette at the same time.
    private void SetTheme(bool dark)
    {
        if (dark == _darkTheme) return;
        _darkTheme = dark;

        var ov = Theme.OverlayDefaults(dark);
        _bgColor = ov.Bg; _textColor = ov.Text; _statusColor = ov.Status;

        DoSave();
        Theme.Apply(dark);

        ClosePreview();
        DialogResult = DialogResult.Retry;
        Close();
    }

    private void TogglePreview()
    {
        if (_preview != null && _preview.Visible) { ClosePreview(); return; }
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_preview == null || _preview.IsDisposed)
        {
            _preview = new OverlayForm(PendingSettings());
            _ = _preview.Handle;
            _preview.CloseClicked += ClosePreview;
        }

        _preview.ApplyColors(PendingSettings());
        if (!_preview.Visible) _preview.ShowOverlay("\U0001F3A4 Listening...");
        _preview.SetText(SAMPLE);
    }

    private void ClosePreview()
    {
        if (_preview == null) return;
        try { _preview.HideOverlay(); } catch { }
    }

    // ------------------------------------------------------------------ save

    private void DoSave()
    {
        _s.InputDeviceIndex = _cmbDevice.SelectedIndex - 1;
        _s.SilenceTimeoutSec = (int)_numSilence.Value;

        _s.SoundsEnabled = _chkSounds.Checked;
        _s.SoundVolume = _sldVolume.Value;
        _s.SoundScheme = _cmbScheme.SelectedItem as string ?? CUSTOM_SCHEME;
        _s.SoundStart = SelectedWav(_cmbStart);
        _s.SoundDone = SelectedWav(_cmbDone);
        _s.SoundError = SelectedWav(_cmbError);

        _s.OverlayFontSize = (float)_numTextFont.Value;
        _s.OverlayTitleFontSize = (float)_numTitleFont.Value;
        _s.OverlayOpacity = _sldOpacity.Value;
        _s.OverlayBgR = _bgColor.R; _s.OverlayBgG = _bgColor.G; _s.OverlayBgB = _bgColor.B;
        _s.TextColorR = _textColor.R; _s.TextColorG = _textColor.G; _s.TextColorB = _textColor.B;
        _s.StatusColorR = _statusColor.R; _s.StatusColorG = _statusColor.G; _s.StatusColorB = _statusColor.B;

        _s.DarkTheme = _darkTheme;
        _s.PolishPrompt = _txtPrompt.Text;
        _s.Save();
    }
}
