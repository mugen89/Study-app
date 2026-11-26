using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace StudyApp.App.UI;

/// <summary>Drawing helpers: rounded shapes, colours and crisp GDI text.</summary>
public static class Gfx
{
    public const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
    public const TextFormatFlags Center = Flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
    public const TextFormatFlags Left = Flags | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
    public const TextFormatFlags Wrap = Flags | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    public static GraphicsPath Round(RectangleF r, float radius) => Round(r, radius, radius, radius, radius);

    public static GraphicsPath Round(RectangleF r, float tl, float tr, float br, float bl)
    {
        var path = new GraphicsPath();
        float max = Math.Min(r.Width, r.Height) / 2f;
        tl = Math.Min(tl, max); tr = Math.Min(tr, max); br = Math.Min(br, max); bl = Math.Min(bl, max);
        if (tl > 0) path.AddArc(r.X, r.Y, tl * 2, tl * 2, 180, 90); else path.AddLine(r.X, r.Y, r.X, r.Y);
        if (tr > 0) path.AddArc(r.Right - tr * 2, r.Y, tr * 2, tr * 2, 270, 90); else path.AddLine(r.Right, r.Y, r.Right, r.Y);
        if (br > 0) path.AddArc(r.Right - br * 2, r.Bottom - br * 2, br * 2, br * 2, 0, 90); else path.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
        if (bl > 0) path.AddArc(r.X, r.Bottom - bl * 2, bl * 2, bl * 2, 90, 90); else path.AddLine(r.X, r.Bottom, r.X, r.Bottom);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
    {
        if (r.Width <= 0 || r.Height <= 0 || color.A == 0) return;
        using var path = Round(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRound(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
    {
        if (r.Width <= 0 || r.Height <= 0 || color.A == 0) return;
        var inset = width / 2f;
        var rr = new RectangleF(r.X + inset, r.Y + inset, r.Width - width, r.Height - width);
        using var path = Round(rr, Math.Max(0, radius - inset));
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    /// <summary>Soft drop shadow below a rounded rectangle (draw before the shape).</summary>
    public static void Shadow(Graphics g, RectangleF r, float radius, float spread, Color color)
    {
        if (color.A == 0 || spread <= 0) return;
        int steps = 6;
        for (int i = steps; i >= 1; i--)
        {
            float grow = spread * i / steps;
            int alpha = (int)(color.A * (1f - (float)i / (steps + 1)) / steps * 2.2f);
            var rr = new RectangleF(r.X - grow * 0.6f, r.Y - grow * 0.3f + spread * 0.35f, r.Width + grow * 1.2f, r.Height + grow * 1.1f);
            FillRound(g, Color.FromArgb(Math.Clamp(alpha, 0, 255), color), rr, radius + grow);
        }
    }

    public static void Circle(Graphics g, Color color, RectangleF r)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, r);
    }

    // ───────────────────────────────────────────────────────────── text

    public static void Text(Graphics g, string? text, Font font, Color color, Rectangle rect, TextFormatFlags flags = Left)
    {
        if (string.IsNullOrEmpty(text) || rect.Width <= 0 || rect.Height <= 0) return;
        TextRenderer.DrawText(g, text, font, rect, color, flags | TextFormatFlags.NoPrefix);
    }

    public static Size Measure(string? text, Font font, int maxWidth = int.MaxValue, TextFormatFlags flags = Flags | TextFormatFlags.SingleLine)
    {
        if (string.IsNullOrEmpty(text)) return new Size(0, font.Height);
        return TextRenderer.MeasureText(text, font, new Size(maxWidth, int.MaxValue), flags | TextFormatFlags.NoPrefix);
    }

    public static int WrappedHeight(string? text, Font font, int width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0) return font.Height;
        return TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), Wrap).Height;
    }

    public static void Glyph(Graphics g, string glyph, Font font, Color color, Rectangle rect) =>
        TextRenderer.DrawText(g, glyph, font, rect, color, Center);

    // ───────────────────────────────────────────────────────────── colours

    public static Color Blend(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * t),
            (int)Math.Round(from.R + (to.R - from.R) * t),
            (int)Math.Round(from.G + (to.G - from.G) * t),
            (int)Math.Round(from.B + (to.B - from.B) * t));
    }

    public static Color Alpha(Color c, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), c);
    public static Color Lighten(Color c, float amount) => Blend(c, Color.White, amount);
    public static Color Darken(Color c, float amount) => Blend(c, Color.Black, amount);

    public static double Luminance(Color c)
    {
        static double Ch(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    public static Color FromHex(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return ColorTranslator.FromHtml(hex.StartsWith('#') ? hex : "#" + hex); }
        catch { return fallback; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Readable text colour on top of <paramref name="background"/>.</summary>
    public static Color OnColor(Color background) => Luminance(background) > 0.55 ? Color.FromArgb(20, 22, 28) : Color.White;
}

/// <summary>Win32 helpers: themed title bars, dark scrollbars, rounded popups.</summary>
internal static class Native
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(Point p);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    /// <summary>Colours the native title bar to match the theme (Windows 11; dark mode also on Windows 10).</summary>
    public static void StyleTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int dark = Theme.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, 4);
            int caption = ColorRef(Theme.P.Caption);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, 4);
            int text = ColorRef(Theme.P.Text);
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, 4);
            int border = ColorRef(Theme.P.Border);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, 4);
        }
        catch
        {
            // Older Windows: keep the default title bar.
        }
    }

    public static void RoundCorners(IntPtr hwnd, bool small = true)
    {
        try
        {
            int pref = small ? 3 : 2;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, 4);
        }
        catch { /* ignore */ }
    }

    /// <summary>Dark or light native scrollbars for text boxes and other native controls.</summary>
    public static void ThemeScrollbars(Control c)
    {
        try
        {
            if (c.IsHandleCreated) SetWindowTheme(c.Handle, Theme.IsDark ? "DarkMode_Explorer" : "Explorer", null);
        }
        catch { /* ignore */ }
    }
}
