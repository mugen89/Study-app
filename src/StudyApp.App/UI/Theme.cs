using Microsoft.Win32;
using StudyApp.Domain.Settings;

namespace StudyApp.App.UI;

/// <summary>
/// Pixel scaling. The app is per-monitor DPI aware; all layout constants go through <see cref="S(int)"/>.
/// <see cref="Scale"/> = monitor DPI factor × the user's "UI size" preference.
/// </summary>
public static class Dpi
{
    /// <summary>Monitor DPI / 96.</summary>
    public static float Device { get; private set; } = 1f;
    /// <summary>User preference (Settings → UI size), 0.9–1.3.</summary>
    public static float Zoom { get; private set; } = 1f;
    public static float Scale => Device * Zoom;

    public static void Set(float device, float zoom)
    {
        Device = Math.Clamp(device, 0.5f, 4f);
        Zoom = Math.Clamp(zoom, 0.8f, 1.5f);
    }

    public static int S(int px) => (int)Math.Round(px * Scale);
    public static float Sf(float px) => px * Scale;
    public static Size S(int w, int h) => new(S(w), S(h));
    public static Padding Pad(int all) => new(S(all));
    public static Padding Pad(int horizontal, int vertical) => new(S(horizontal), S(vertical), S(horizontal), S(vertical));
    public static Padding Pad(int l, int t, int r, int b) => new(S(l), S(t), S(r), S(b));
}

/// <summary>All colours of the current theme.</summary>
public sealed class Palette
{
    public bool Dark;
    public bool Sepia;
    public Color Bg, Sidebar, Surface, Surface2, Surface3, Border, BorderStrong;
    public Color Text, Text2, Text3;
    public Color Accent, AccentHover, AccentPressed, OnAccent, AccentSoft, AccentSoftHover, AccentText;
    public Color Success, SuccessSoft, Warning, WarningSoft, Danger, DangerSoft, Info, InfoSoft;
    public Color Shadow, Selection, Caption;
}

public static class Theme
{
    public static readonly Color DefaultAccent = ColorTranslator.FromHtml("#4F6BED");

    /// <summary>Accent presets offered in Settings and for subject colours.</summary>
    public static readonly string[] AccentPresets =
    {
        "#4F6BED", "#7C5CE6", "#C2459B", "#E0525E", "#E8783A", "#D4A017", "#2F9E6E", "#159A9C", "#2D8FD5", "#5B6B82",
    };

    public static Palette P { get; private set; } = Build(ThemeMode.Light, DefaultAccent);
    public static ThemeMode Mode { get; private set; } = ThemeMode.System;
    public static bool IsDark => P.Dark;
    public static bool Animations { get; set; } = true;

    public static void Apply(AppSettings settings)
    {
        Mode = settings.Theme;
        Animations = settings.Animations;
        var accent = Gfx.FromHex(settings.Accent, DefaultAccent);
        var effective = Mode == ThemeMode.System ? (SystemPrefersDark() ? ThemeMode.Dark : ThemeMode.Light) : Mode;
        P = Build(effective, accent);
    }

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    public static Palette Build(ThemeMode mode, Color accent)
    {
        var p = new Palette();
        switch (mode)
        {
            case ThemeMode.Dark:
                p.Dark = true;
                p.Bg = Hex("#121418"); p.Sidebar = Hex("#0D0F12"); p.Surface = Hex("#1A1D23"); p.Surface2 = Hex("#22262E");
                p.Surface3 = Hex("#2B3039"); p.Border = Hex("#2B2F37"); p.BorderStrong = Hex("#3C424D");
                p.Text = Hex("#E9EBEF"); p.Text2 = Hex("#A9B0BC"); p.Text3 = Hex("#6F7685");
                p.Success = Hex("#3CCB7F"); p.Warning = Hex("#F0A92E"); p.Danger = Hex("#F2706F"); p.Info = Hex("#5EA2F2");
                break;
            case ThemeMode.Sepia:
                p.Sepia = true;
                p.Bg = Hex("#F2E9D6"); p.Sidebar = Hex("#E9DDC3"); p.Surface = Hex("#FBF6EA"); p.Surface2 = Hex("#F1E7D1");
                p.Surface3 = Hex("#E7DABE"); p.Border = Hex("#DFD0AF"); p.BorderStrong = Hex("#CAB68F");
                p.Text = Hex("#3A2E22"); p.Text2 = Hex("#665542"); p.Text3 = Hex("#978369");
                p.Success = Hex("#2E7D46"); p.Warning = Hex("#A8620C"); p.Danger = Hex("#B83A32"); p.Info = Hex("#2F6BB0");
                break;
            default:
                p.Bg = Hex("#F5F6FA"); p.Sidebar = Hex("#ECEEF4"); p.Surface = Hex("#FFFFFF"); p.Surface2 = Hex("#F3F4F8");
                p.Surface3 = Hex("#E8EAF0"); p.Border = Hex("#E1E4EB"); p.BorderStrong = Hex("#C9CED8");
                p.Text = Hex("#171A21"); p.Text2 = Hex("#4B5261"); p.Text3 = Hex("#8A909C");
                p.Success = Hex("#16884A"); p.Warning = Hex("#B9650B"); p.Danger = Hex("#D63B3B"); p.Info = Hex("#2563EB");
                break;
        }

        p.Accent = accent;
        if (p.Dark)
        {
            p.AccentHover = Gfx.Lighten(accent, 0.10f);
            p.AccentPressed = Gfx.Lighten(accent, 0.20f);
            p.AccentSoft = Gfx.Blend(p.Surface, accent, 0.22f);
            p.AccentSoftHover = Gfx.Blend(p.Surface, accent, 0.30f);
            p.AccentText = Gfx.Luminance(accent) < 0.45 ? Gfx.Lighten(accent, 0.35f) : accent;
        }
        else
        {
            p.AccentHover = Gfx.Darken(accent, 0.08f);
            p.AccentPressed = Gfx.Darken(accent, 0.16f);
            p.AccentSoft = Gfx.Blend(p.Surface, accent, 0.11f);
            p.AccentSoftHover = Gfx.Blend(p.Surface, accent, 0.18f);
            p.AccentText = Gfx.Luminance(accent) > 0.42 ? Gfx.Darken(accent, 0.30f) : accent;
        }
        p.OnAccent = Gfx.Luminance(accent) > 0.62 ? Hex("#111318") : Color.White;

        float soft = p.Dark ? 0.20f : 0.12f;
        p.SuccessSoft = Gfx.Blend(p.Surface, p.Success, soft);
        p.WarningSoft = Gfx.Blend(p.Surface, p.Warning, soft);
        p.DangerSoft = Gfx.Blend(p.Surface, p.Danger, soft);
        p.InfoSoft = Gfx.Blend(p.Surface, p.Info, soft);
        p.Shadow = p.Dark ? Color.FromArgb(90, 0, 0, 0) : Color.FromArgb(28, 30, 40, 70);
        p.Selection = Gfx.Blend(p.Surface, accent, p.Dark ? 0.35f : 0.20f);
        p.Caption = p.Sidebar;
        return p;
    }

    private static Color Hex(string hex) => ColorTranslator.FromHtml(hex);
}

/// <summary>Font roles. Sizes are in points and converted to pixels for the current DPI.</summary>
public static class Fonts
{
    private static readonly Dictionary<(string, float, FontStyle), Font> Cache = new();
    private static HashSet<string>? _families;

    public static string Text { get; private set; } = "Segoe UI";
    public static string TextSemibold { get; private set; } = "Segoe UI Semibold";
    public static string Display { get; private set; } = "Segoe UI";
    public static string DisplaySemibold { get; private set; } = "Segoe UI Semibold";
    public static string Icons { get; private set; } = "Segoe MDL2 Assets";
    public static string Mono { get; private set; } = "Consolas";
    /// <summary>The reading font chosen in Settings → Reading.</summary>
    public static string Study { get; private set; } = "Segoe UI";

    public static IReadOnlyCollection<string> Installed
    {
        get
        {
            _families ??= new HashSet<string>(FontFamily.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            return _families;
        }
    }

    public static bool Has(string family) => Installed.Contains(family);

    public static void Init()
    {
        Text = Has("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        TextSemibold = Has("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold" : Has("Segoe UI Semibold") ? "Segoe UI Semibold" : "Segoe UI";
        Display = Has("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
        DisplaySemibold = Has("Segoe UI Variable Display Semib") ? "Segoe UI Variable Display Semib" : TextSemibold;
        Icons = Has("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        Mono = Has("Cascadia Mono") ? "Cascadia Mono" : "Consolas";
    }

    public static void SetStudyFont(string? family) =>
        Study = !string.IsNullOrWhiteSpace(family) && Has(family) ? family : Text;

    /// <summary>Semibold variant of the reading font (falls back to bold).</summary>
    public static Font StudyStrong(float pt)
    {
        if (Study == "Segoe UI Variable Text" && Has("Segoe UI Variable Text Semibold")) return GetDevice("Segoe UI Variable Text Semibold", pt, FontStyle.Regular);
        if (Study == "Segoe UI" && Has("Segoe UI Semibold")) return GetDevice("Segoe UI Semibold", pt, FontStyle.Regular);
        return GetDevice(Study, pt, FontStyle.Bold);
    }

    public static void Clear()
    {
        foreach (var f in Cache.Values) f.Dispose();
        Cache.Clear();
    }

    /// <summary>UI font scaled by DPI and the UI size preference.</summary>
    public static Font Get(string family, float pt, FontStyle style = FontStyle.Regular) => Create(family, pt * Dpi.Scale, style);

    /// <summary>Font scaled by DPI only (study text has its own size settings).</summary>
    public static Font GetDevice(string family, float pt, FontStyle style = FontStyle.Regular) => Create(family, pt * Dpi.Device, style);

    private static Font Create(string family, float scaledPt, FontStyle style)
    {
        var px = (float)Math.Round(scaledPt * 96f / 72f, 1);
        var key = (family, px, style);
        if (Cache.TryGetValue(key, out var font)) return font;
        try { font = new Font(family, px, style, GraphicsUnit.Pixel); }
        catch { font = new Font("Segoe UI", px, style, GraphicsUnit.Pixel); }
        Cache[key] = font;
        return font;
    }

    public static Font Ui(float pt = 10f) => Get(Text, pt);
    public static Font UiSemibold(float pt = 10f) => Get(TextSemibold, pt);
    public static Font Heading(float pt) => Get(DisplaySemibold, pt);
    public static Font Icon(float pt = 11f) => Get(Icons, pt);
    public static Font Code(float pt = 9.5f) => Get(Mono, pt);
    public static Font StudyText(float pt, FontStyle style = FontStyle.Regular) => GetDevice(Study, pt, style);
}

/// <summary>Segoe Fluent Icons / Segoe MDL2 Assets glyphs used in the app.</summary>
public static class Icons
{
    public const string Home = "";
    public const string Library = "";
    public const string Book = "";
    public const string Reading = "";
    public const string Cap = "";
    public const string Review = "";
    public const string Search = "";
    public const string Import = "";
    public const string Export = "";
    public const string Chat = "";
    public const string Delete = "";
    public const string Settings = "";
    public const string Add = "";
    public const string Close = "";
    public const string More = "";
    public const string Edit = "";
    public const string Save = "";
    public const string Copy = "";
    public const string Paste = "";
    public const string OpenFile = "";
    public const string Folder = "";
    public const string Document = "";
    public const string Page = "";
    public const string Play = "";
    public const string Pause = "";
    public const string Back = "";
    public const string Forward = "";
    public const string ChevronLeft = "";
    public const string ChevronRight = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string Check = "";
    public const string CheckCircle = "";
    public const string ErrorCircle = "";
    public const string Flag = "";
    public const string Pin = "";
    public const string Star = "";
    public const string StarFill = "";
    public const string Refresh = "";
    public const string Sync = "";
    public const string Undo = "";
    public const string Shuffle = "";
    public const string Sort = "";
    public const string Filter = "";
    public const string Info = "";
    public const string Help = "";
    public const string Warning = "";
    public const string Error = "";
    public const string Lightbulb = "";
    public const string Clock = "";
    public const string Timer = "";
    public const string Calendar = "";
    public const string Font = "";
    public const string FontSize = "";
    public const string Palette = "";
    public const string Sun = "";
    public const string Moon = "";
    public const string Eye = "";
    public const string Hide = "";
    public const string Tag = "";
    public const string List = "";
    public const string Grid = "";
    public const string Bolt = "";
    public const string Chart = "";
    public const string Trending = "";
    public const string Keyboard = "";
    public const string Feedback = "";
    public const string Update = "";
    public const string Language = "";
    public const string ZoomIn = "";
    public const string ZoomOut = "";
    public const string FullScreen = "";
    public const string ExitFullScreen = "";
    public const string Note = "";
    public const string Bug = "";
    public const string Archive = "";
    public const string Link = "";
    public const string Download = "";
    public const string Database = "";
    public const string Clipboard = "";
    public const string Sliders = "";
    public const string Question = "";
    public const string Layers = "";

    /// <summary>Icons a subject can use (key → glyph).</summary>
    public static readonly (string Key, string Glyph)[] SubjectIcons =
    {
        ("book", ""), ("reading", ""), ("cap", ""), ("heart", ""), ("health", ""),
        ("globe", ""), ("calculator", ""), ("code", ""), ("music", ""), ("palette", ""),
        ("people", ""), ("bank", ""), ("chart", ""), ("idea", ""), ("star", ""),
        ("flag", ""), ("map", ""), ("mic", ""), ("camera", ""), ("car", ""),
        ("plane", ""), ("key", ""), ("puzzle", ""), ("books", ""), ("clipboard", ""),
        ("gears", ""), ("bug", ""), ("bolt", ""),
    };

    public static string ForSubject(string? key) =>
        SubjectIcons.FirstOrDefault(i => i.Key == key).Glyph ?? Book;
}
