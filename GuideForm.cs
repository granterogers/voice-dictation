using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoiceDictation;

// ============================================================================
// Getting Started
//
// A short guide to the things that are not discoverable: that clicking the
// overlay finishes a dictation, that the X cancels it, and that it stops on its
// own once you stop speaking.
//
// Laid out in two columns so it fits on screen without scrolling, and the
// pictures are drawn by AppIcons — the same code the tray icon and the
// overlay's close button use, so they cannot drift out of date.
// ============================================================================
sealed class GuideForm : Form
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2;

    private const int CARD_W = 340;
    private const int GUTTER = 10;
    private const int PAD = 18;
    private const int BADGE_W = 72;

    // Art is either a drawn bitmap or a short key caption, never both.
    private sealed record Entry(Bitmap Art, string Key, string Title, string Body);

    private readonly List<Bitmap> _art = new();

    public GuideForm(AppSettings s)
    {
        int stopSec = Math.Max(0, s.SilenceTimeoutSec);
        var entries = BuildEntries(s, stopSec);

        Text = "Voice Dictation — Getting Started";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.UI();
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        // Each card occupies CARD_W plus its right margin, so two columns need
        // (CARD_W + GUTTER) * 2 of usable width — counting the gutter once leaves the
        // second card 10px short and FlowLayoutPanel silently drops to one column.
        int bodyW = PAD * 2 + (CARD_W + GUTTER) * 2;
        ClientSize = new Size(bodyW, 600);

        BuildHeader();
        BuildFooter();

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, AutoScroll = false, BackColor = Theme.Bg,
            Padding = new Padding(PAD, 8, PAD, 8)
        };
        Controls.Add(body);
        body.BringToFront();

        // One uniform card height keeps the two columns aligned into a tidy grid
        // instead of a ragged staircase.
        int cardH = 0;
        foreach (var e in entries) cardH = Math.Max(cardH, MeasureCard(e));

        foreach (var e in entries) body.Controls.Add(BuildCard(e, cardH));

        int rows = (entries.Count + 1) / 2;
        ClientSize = new Size(bodyW, 56 + rows * (cardH + 8) + 16 + 54);

        Resize += (_, _) => ApplyRoundedRegion();
        Shown += (_, _) => ActiveControl = null;
        FormClosed += (_, _) => { foreach (var b in _art) b.Dispose(); };
        ApplyRoundedRegion();
    }

    private List<Entry> BuildEntries(AppSettings s, int stopSec)
    {
        Bitmap Keep(Bitmap b) { _art.Add(b); return b; }

        var micIdle = Keep(AppIcons.MicBitmap(AppIcons.MicIdle, 30));
        var micRec = Keep(AppIcons.MicBitmap(AppIcons.MicRecording, 30));
        var closeArt = Keep(AppIcons.CloseBitmap(30, Theme.Field, AppIcons.CloseMark));
        var overlayArt = Keep(AppIcons.OverlayThumb(66, 30, s.OverlayBgColor, s.StatusColor, s.TextColor));

        // Idle and recording side by side, so the colour change is self-explanatory.
        var micPair = Keep(SideBySide(micIdle, micRec));

        return new List<Entry>
        {
            new(null, "Win + \\", "Start dictating",
                "Press it once to begin, and again to stop and paste. The overlay slides up "
                + "from the tray as you speak."),

            new(micPair, null, "Or click the tray icon",
                "Click the microphone to start, and again to finish. Blue when idle, red "
                + "while it is listening."),

            new(overlayArt, null, "Click the overlay when done",
                "Clicking anywhere on it means \"I have finished speaking\". It stops, cleans "
                + "the text up and pastes it at your cursor."),

            new(closeArt, null, "Click the X to discard",
                "Cancels the dictation and pastes nothing. Escape does the same. Use it when "
                + "you would rather start over."),

            new(null, stopSec > 0 ? stopSec + "s" : "Off", "It also stops by itself",
                stopSec > 0
                    ? $"After {stopSec}s with no new words it finishes on its own. It listens "
                    + "for words, not silence, so background noise will not hold it open."
                    : "Automatic stopping is off, so dictation runs until you click or press "
                    + "the hotkey. Change this under Settings."),

            new(null, "Live", "Tidied as you speak",
                "Punctuation, capitalisation and paragraph breaks appear in the preview while "
                + "you talk, and the window grows as you say more."),

            new(null, "AI", "Cleaned before pasting",
                "Your speech is transcribed, then an AI pass fixes grammar and formats it. "
                + "Change its instructions under Settings → AI Cleanup Prompt."),

            new(null, "Log", "If something looks wrong",
                "Each dictation is logged to voice-dictation.log next to the app. It records "
                + "the text at each stage, and never your API key."),
        };
    }

    private static Bitmap SideBySide(Bitmap a, Bitmap b)
    {
        var o = new Bitmap(a.Width + 8 + b.Width, Math.Max(a.Height, b.Height));
        using var g = Graphics.FromImage(o);
        g.Clear(Color.Transparent);
        g.DrawImage(a, 0, 0);
        g.DrawImage(b, a.Width + 8, 0);
        return o;
    }

    private static int TextWidth => CARD_W - BADGE_W - 26;

    private static int MeasureCard(Entry e)
    {
        int h = TextRenderer.MeasureText(e.Body, Theme.UI(8.75f),
            new Size(TextWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
        return Math.Max(74, 32 + h + 14);
    }

    private Panel BuildCard(Entry e, int height)
    {
        var card = new Panel
        {
            Size = new Size(CARD_W, height), BackColor = Theme.Card,
            Margin = new Padding(0, 0, GUTTER, 8)
        };

        if (e.Art != null)
        {
            var pic = new PictureBox
            {
                Image = e.Art, SizeMode = PictureBoxSizeMode.AutoSize,
                BackColor = Theme.Card,
                Location = new Point(14 + (BADGE_W - e.Art.Width) / 2, 14)
            };
            // The picture is decoration; clicks should not land on a dead control.
            pic.Enabled = false;
            card.Controls.Add(pic);
        }
        else
        {
            var badge = new Label
            {
                Text = e.Key, AutoSize = false, Size = new Size(BADGE_W, 26),
                Location = new Point(14, 15), TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Accent, BackColor = Theme.Card,
                Font = Theme.UI(8.5f, FontStyle.Bold)
            };
            badge.Paint += (_, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = Theme.RoundRect(new Rectangle(0, 0, badge.Width - 1, badge.Height - 1), 6);
                using var pen = new Pen(Theme.Accent);
                pe.Graphics.DrawPath(pen, path);
            };
            card.Controls.Add(badge);
        }

        card.Controls.Add(new Label
        {
            Text = e.Title, AutoSize = true, Location = new Point(BADGE_W + 22, 13),
            ForeColor = Theme.Text, BackColor = Theme.Card, Font = Theme.UI(9.5f, FontStyle.Bold)
        });

        card.Controls.Add(new Label
        {
            Text = e.Body, Location = new Point(BADGE_W + 22, 32),
            Size = new Size(TextWidth, height - 44), AutoSize = false,
            ForeColor = Theme.Muted, BackColor = Theme.Card, Font = Theme.UI(8.75f)
        });

        card.Paint += (_, pe) =>
        {
            using var pen = new Pen(Theme.CardEdge);
            pe.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        return card;
    }

    private void BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Theme.Bg };
        header.Paint += (_, e) =>
        {
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, PAD, header.Height - 2, header.Width - PAD * 2, 2);
        };

        var title = new Label
        {
            Text = "Getting Started", AutoSize = true, Location = new Point(PAD, 12),
            Font = Theme.UI(13f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Bg
        };
        var sub = new Label
        {
            Text = "How to dictate, stop and cancel", AutoSize = true, Location = new Point(PAD + 2, 34),
            Font = Theme.UI(8.5f), ForeColor = Theme.Muted, BackColor = Theme.Bg
        };

        foreach (var c in new Control[] { header, title, sub })
            c.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            };

        var close = new CloseCircle
        {
            Size = new Size(30, 30), Location = new Point(ClientSize.Width - 44, 13),
            CircleColor = Theme.Field
        };
        close.Click += (_, _) => Close();

        header.Controls.Add(close);
        header.Controls.Add(title);
        header.Controls.Add(sub);
        Controls.Add(header);
    }

    private void BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Bg };
        var ok = new Button
        {
            Text = "Got it", Size = new Size(96, 30),
            Location = new Point(ClientSize.Width - 96 - PAD, 11),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = Theme.Accent, ForeColor = Color.White,
            Font = Theme.UI(9f, FontStyle.Bold)
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.Click += (_, _) => Close();
        footer.Controls.Add(ok);
        Controls.Add(footer);
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
        using var pen = new Pen(Theme.CardEdge);
        e.Graphics.DrawPath(pen, path);
    }
}
