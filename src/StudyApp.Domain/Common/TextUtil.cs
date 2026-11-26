using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StudyApp.Domain;

public static partial class TextUtil
{
    /// <summary>Trim, normalise line endings, collapse 3+ blank lines.</summary>
    public static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = s.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        while (t.Contains("\n\n\n")) t = t.Replace("\n\n\n", "\n\n");
        return t;
    }

    /// <summary>Lowercase, no punctuation, single spaces. Optionally removes accents.</summary>
    public static string NormalizeForMatch(string? s, bool stripDiacritics = false)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var text = s.ToLowerInvariant();
        if (stripDiacritics)
        {
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var sbd = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sbd.Append(ch);
            text = sbd.ToString().Normalize(NormalizationForm.FormC);
        }

        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                space = false;
            }
            else if (!space && sb.Length > 0)
            {
                sb.Append(' ');
                space = true;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Duplicate-detection key: type group + normalised prompt.</summary>
    public static string Fingerprint(string type, string prompt)
    {
        var norm = NormalizeForMatch(prompt);
        if (norm.Length > 400) norm = norm[..400];
        return Models.ItemTypes.FingerprintGroup(type) + ":" + norm;
    }

    public static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>First number in a page reference ("pp. 45–48" → 45). Used for "page order" sorting.</summary>
    public static int? FirstPageNumber(string? page)
    {
        if (string.IsNullOrWhiteSpace(page)) return null;
        var m = DigitsRegex().Match(page);
        return m.Success && int.TryParse(m.Value, out var n) ? n : null;
    }

    /// <summary>Removes "A) ", "b. ", "(C) ", "1) " prefixes LLMs like to put on options.</summary>
    public static string StripOptionPrefix(string text)
    {
        var t = text.Trim();
        var m = OptionPrefixRegex().Match(t);
        return m.Success ? t[m.Length..].Trim() : t;
    }

    public static List<string> SplitTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        return raw.Split(new[] { ',', ';', '#', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Stored as ",tag one,tag two," so a LIKE '%,tag,%' query matches whole tags.</summary>
    public static string JoinTagsForDb(IEnumerable<string> tags)
    {
        var list = tags.Select(t => t.Trim().Replace(",", " ")).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list.Count == 0 ? "" : "," + string.Join(",", list) + ",";
    }

    public static List<string> SplitTagsFromDb(string? raw) =>
        string.IsNullOrEmpty(raw) ? new() : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var oneLine = s.Replace('\n', ' ').Replace('\r', ' ');
        return oneLine.Length <= max ? oneLine : oneLine[..Math.Max(0, max - 1)].TrimEnd() + "…";
    }

    /// <summary>Removes the most common markdown markers for plain-text previews.</summary>
    public static string StripMarkdown(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = s.Replace("**", "").Replace("__", "").Replace("`", "");
        t = HeadingRegex().Replace(t, "");
        return t;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsRegex();

    [GeneratedRegex(@"^(\(?[A-Ha-h1-9]\)|[A-Ha-h]\.|[A-Ha-h]:|[1-9]\.)\s+")]
    private static partial Regex OptionPrefixRegex();

    [GeneratedRegex(@"(?m)^#{1,6}\s*")]
    private static partial Regex HeadingRegex();
}
