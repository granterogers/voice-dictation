using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace VoiceDictation;

// ============================================================================
// Icon drawing
//
// Shared so the tray icon, the overlay's close button and the Getting Started
// guide all render from one definition — the pictures in the guide are the
// actual controls, not lookalikes that can drift out of date.
// ============================================================================
static class AppIcons
{
    public static readonly Color MicIdle = Color.FromArgb(56, 132, 255);    // #3884FF
    public static readonly Color MicRecording = Color.FromArgb(235, 56, 56); // #EB3838
    public static readonly Color CloseMark = Color.FromArgb(236, 72, 62);

    public static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
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
    public static void DrawMicGlyph(Graphics g, Color c, float cx, float cy, float box, float weight)
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
    public static Bitmap MicBitmap(Color color, int size = 32)
    {
        var b = new Bitmap(size, size); // 32bpp ARGB
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float s = size;
        using (var brush = new SolidBrush(color))
        using (var badge = RoundRect(0.5f, 0.5f, s - 1f, s - 1f, s * 0.25f))
            g.FillPath(brush, badge);

        DrawMicGlyph(g, Color.White, s / 2f, s / 2f, s * 0.62f, 2.8f);
        return b;
    }

    public static Icon MicIcon(Color color)
    {
        using var b = MicBitmap(color);
        return Icon.FromHandle(b.GetHicon());
    }

    // Circle plus two rounded strokes. Drawn rather than a "✕" glyph: a text glyph
    // sits on the font baseline (never quite centred), hints unpredictably at small
    // sizes, and cannot be given rounded stroke ends.
    public static void DrawCloseMark(Graphics g, RectangleF bounds,
                                     Color circleColor, Color markColor, bool edge = true)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float d = Math.Min(bounds.Width, bounds.Height) - 2f;
        if (d <= 2f) return;
        var circle = new RectangleF(bounds.X + 1f, bounds.Y + 1f, d, d);

        using (var fill = new SolidBrush(circleColor))
            g.FillEllipse(fill, circle);
        if (edge)
            using (var pen = new Pen(Color.FromArgb(64, 255, 255, 255), 1f))
                g.DrawEllipse(pen, circle);

        float inset = d * 0.33f;
        using var stroke = new Pen(markColor, Math.Max(2.2f, d * 0.105f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLine(stroke, circle.Left + inset, circle.Top + inset,
                           circle.Right - inset, circle.Bottom - inset);
        g.DrawLine(stroke, circle.Right - inset, circle.Top + inset,
                           circle.Left + inset, circle.Bottom - inset);
    }

    public static Bitmap CloseBitmap(int size, Color circleColor, Color markColor)
    {
        var b = new Bitmap(size, size);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.Transparent);
        DrawCloseMark(g, new RectangleF(0, 0, size, size), circleColor, markColor);
        return b;
    }

    // Miniature of the overlay panel, for the guide. Not pixel-exact with the real
    // window, but built from the same gradient and accent so it is recognisable.
    public static Bitmap OverlayThumb(int w, int h, Color bg, Color accent, Color text)
    {
        var b = new Bitmap(w, h);
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        static Color Shift(Color c, int d) => Color.FromArgb(
            Math.Clamp(c.R + d, 0, 255), Math.Clamp(c.G + d, 0, 255), Math.Clamp(c.B + d, 0, 255));

        using (var path = RoundRect(0.5f, 0.5f, w - 1f, h - 1f, 5f))
        {
            using var fill = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                Shift(bg, 20), Shift(bg, -6), LinearGradientMode.Vertical);
            g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(60, 255, 255, 255));
            g.DrawPath(pen, path);
        }

        using (var lip = new SolidBrush(accent))
            g.FillRectangle(lip, 1, 1, w - 2, 1);

        // Title bar and two lines of "text".
        using (var acc = new SolidBrush(accent))
            g.FillRectangle(acc, 6, 6, (int)(w * 0.42f), 3);
        using (var t = new SolidBrush(Color.FromArgb(170, text)))
        {
            g.FillRectangle(t, 6, 15, w - 14, 2);
            g.FillRectangle(t, 6, 21, (int)(w * 0.72f), 2);
        }
        return b;
    }
}
