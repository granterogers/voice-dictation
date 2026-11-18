using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VoiceDictation;

// ============================================================================
// Interface palette
//
// Mutable rather than const so Light/Dark can be switched at runtime. Custom
// controls (FlatSlider, Section, GradientPanel, CloseCircle, the menu renderer)
// read these at paint time, so they follow a change on the next Invalidate;
// stock controls have their colours assigned once and are re-applied by
// Restyle().
// ============================================================================
static class Theme
{
    public static bool IsDark { get; private set; } = true;

    public static Color Bg { get; private set; }
    public static Color Card { get; private set; }
    public static Color CardHover { get; private set; }
    public static Color CardEdge { get; private set; }
    public static Color Field { get; private set; }
    public static Color Text { get; private set; }
    public static Color Muted { get; private set; }
    public static Color Accent { get; private set; }

    static Theme() => Apply(true);

    public static void Apply(bool dark)
    {
        IsDark = dark;
        if (dark)
        {
            Bg        = Color.FromArgb(26, 26, 32);
            Card      = Color.FromArgb(34, 34, 42);
            CardHover = Color.FromArgb(42, 42, 52);
            CardEdge  = Color.FromArgb(54, 54, 64);
            Field     = Color.FromArgb(46, 46, 56);
            Text      = Color.FromArgb(232, 232, 238);
            Muted     = Color.FromArgb(150, 150, 164);
            Accent    = Color.FromArgb(86, 156, 255);
        }
        else
        {
            Bg        = Color.FromArgb(244, 245, 248);
            Card      = Color.FromArgb(255, 255, 255);
            CardHover = Color.FromArgb(236, 239, 245);
            CardEdge  = Color.FromArgb(214, 218, 226);
            Field     = Color.FromArgb(240, 241, 245);
            Text      = Color.FromArgb(27, 29, 34);
            Muted     = Color.FromArgb(105, 112, 128);
            Accent    = Color.FromArgb(38, 108, 208);
        }
    }

    // Overlay colours that go with each theme. Switching the theme adopts these,
    // since the overlay is the most visible surface and leaving it dark under a
    // light interface looks like a bug rather than a preference.
    public static (Color Bg, Color Text, Color Status) OverlayDefaults(bool dark) => dark
        ? (Color.FromArgb(24, 24, 28), Color.FromArgb(230, 230, 230), Color.FromArgb(120, 180, 255))
        : (Color.FromArgb(248, 249, 252), Color.FromArgb(28, 30, 36), Color.FromArgb(36, 106, 205));

    public static Font UI(float size = 9f, FontStyle style = FontStyle.Regular)
        => new Font("Segoe UI", size, style);

    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        if (r.Width <= d || r.Height <= d) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// Dark/light tray context menu. The stock renderer always paints system colours,
// so both the colour table and the item rendering are replaced.
sealed class ThemedMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Card;
    public override Color MenuItemSelected => Theme.Accent;
    public override Color MenuItemSelectedGradientBegin => Theme.Accent;
    public override Color MenuItemSelectedGradientEnd => Theme.Accent;
    public override Color MenuItemBorder => Theme.Accent;
    public override Color MenuBorder => Theme.CardEdge;
    public override Color ImageMarginGradientBegin => Theme.Card;
    public override Color ImageMarginGradientMiddle => Theme.Card;
    public override Color ImageMarginGradientEnd => Theme.Card;
    public override Color SeparatorDark => Theme.CardEdge;
    public override Color SeparatorLight => Theme.CardEdge;
}

sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedMenuRenderer() : base(new ThemedMenuColors()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.Muted
                    : e.Item.Selected ? Color.White
                    : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.Card);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var p = new Pen(Theme.CardEdge);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
    }
}
