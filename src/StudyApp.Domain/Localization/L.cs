using System.Globalization;

namespace StudyApp.Domain.Localization;

/// <summary>
/// Tiny localization layer. English text is the key; Bulgarian translations live in
/// <see cref="Bulgarian"/>. Missing translations fall back to English, so the UI never breaks.
/// </summary>
public static class L
{
    public const string English = "en";
    public const string BulgarianCode = "bg";

    private static readonly CultureInfo EnCulture = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo BgCulture = CultureInfo.GetCultureInfo("bg-BG");

    public static string Lang { get; private set; } = English;
    public static bool IsBg => Lang == BulgarianCode;
    public static CultureInfo Culture => IsBg ? BgCulture : EnCulture;

    /// <summary>Raised after the language changed. The UI rebuilds itself.</summary>
    public static event Action? Changed;

    public static string SystemDefault =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("bg", StringComparison.OrdinalIgnoreCase)
            ? BulgarianCode
            : English;

    public static void SetLanguage(string? lang, bool raise = true)
    {
        var normalized = string.Equals(lang, BulgarianCode, StringComparison.OrdinalIgnoreCase) ? BulgarianCode : English;
        if (normalized == Lang) return;
        Lang = normalized;
        if (raise) Changed?.Invoke();
    }

    /// <summary>Translate a UI string.</summary>
    public static string T(string en)
    {
        if (!IsBg || string.IsNullOrEmpty(en)) return en;
        return Bulgarian.Strings.TryGetValue(en, out var bg) ? bg : en;
    }

    /// <summary>Translate and format (placeholders {0}, {1}...).</summary>
    public static string F(string en, params object?[] args) => string.Format(Culture, T(en), args);

    /// <summary>"5 cards" / "5 карти". Pass the English singular and plural.</summary>
    public static string N(long count, string singular, string plural)
        => count.ToString("N0", Culture) + " " + Word(count, singular, plural);

    /// <summary>Only the noun, correctly pluralised for the count.</summary>
    public static string Word(long count, string singular, string plural)
    {
        if (!IsBg) return count == 1 ? singular : plural;
        if (Bulgarian.Plurals.TryGetValue(singular, out var forms))
            return count == 1 ? forms.One : forms.Many;
        return T(count == 1 ? singular : plural);
    }

    public static string Date(DateTime local) => local.ToString(IsBg ? "d MMMM yyyy" : "d MMMM yyyy", Culture);
    public static string ShortDate(DateTime local) => local.ToString("d MMM yyyy", Culture);
    public static string DateTimeShort(DateTime local) => local.ToString("d MMM yyyy, HH:mm", Culture);
    public static string Weekday(DateTime local) => local.ToString("dddd", Culture);

    /// <summary>Human friendly duration: 45s, 3m, 1h 05m.</summary>
    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return F("{0}s", (int)t.TotalSeconds);
        if (t.TotalMinutes < 60) return F("{0}m {1:00}s", (int)t.TotalMinutes, t.Seconds);
        return F("{0}h {1:00}m", (int)t.TotalHours, t.Minutes);
    }

    /// <summary>"just now", "3 hours ago", "yesterday", "5 days ago".</summary>
    public static string Ago(DateTime local)
    {
        var d = DateTime.Now - local;
        if (d.TotalMinutes < 1) return T("just now");
        if (d.TotalMinutes < 60) return F("{0} ago", N((int)d.TotalMinutes, "minute", "minutes"));
        if (d.TotalHours < 24) return F("{0} ago", N((int)d.TotalHours, "hour", "hours"));
        if (d.TotalDays < 2) return T("yesterday");
        if (d.TotalDays < 30) return F("{0} ago", N((int)d.TotalDays, "day", "days"));
        return ShortDate(local);
    }

    /// <summary>Page reference shown on cards: "p. 52" / "стр. 52".</summary>
    public static string Page(string? page)
    {
        if (string.IsNullOrWhiteSpace(page)) return "";
        var p = page.Trim();
        var lower = p.ToLowerInvariant();
        if (lower.StartsWith("p.") || lower.StartsWith("pp.") || lower.StartsWith("page") || lower.StartsWith("стр") || lower.StartsWith("§") || lower.StartsWith("ch"))
            return p;
        bool range = p.Contains('-') || p.Contains('–') || p.Contains(',');
        return IsBg ? "стр. " + p : (range ? "pp. " : "p. ") + p;
    }
}
