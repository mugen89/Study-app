using System.Text;
using System.Text.RegularExpressions;

namespace StudyApp.Domain.Import;

/// <summary>
/// Gets the JSON out of a chat reply and repairs the mistakes LLMs typically make:
/// ```json fences, chatter before/after, raw line breaks inside strings, missing commas
/// between objects, “smart quotes”, and replies that were cut off mid-way.
/// </summary>
public static partial class JsonExtractor
{
    /// <summary>Returns the most likely JSON text. Adds human-readable notes about what was removed.</summary>
    public static string Extract(string? raw, List<string> notes)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var text = raw.Replace("﻿", "").Trim();

        // 1) Fenced code blocks: take the largest one that looks like JSON.
        var fences = FenceRegex().Matches(text);
        if (fences.Count > 0)
        {
            var best = fences
                .Select(m => m.Groups["body"].Value.Trim())
                .Where(b => b.StartsWith('{') || b.StartsWith('['))
                .OrderByDescending(b => b.Length)
                .FirstOrDefault();
            if (best != null)
            {
                notes.Add("Removed the ``` code fences around the JSON.");
                if (fences.Count > 1)
                    notes.Add("The reply contained several code blocks; the largest JSON block was used.");
                return best;
            }
        }

        // 2) An opening fence without a closing one (reply cut off).
        var open = OpenFenceRegex().Match(text);
        if (open.Success)
        {
            text = text[open.Length..].Trim();
            notes.Add("Removed the ``` code fence around the JSON.");
        }

        // 3) Chatter before / after the JSON.
        int start = IndexOfJsonStart(text);
        if (start < 0) return "";
        if (start > 0)
        {
            notes.Add("Ignored the text before the JSON.");
            text = text[start..];
        }

        char opener = text[0];
        char closer = opener == '{' ? '}' : ']';
        int end = text.LastIndexOf(closer);
        if (end >= 0 && end < text.Length - 1)
        {
            var tail = text[(end + 1)..].Trim();
            if (tail.Length > 0 && !tail.StartsWith('`'))
                notes.Add("Ignored the text after the JSON.");
            text = text[..(end + 1)];
        }
        return text.Trim();
    }

    private static int IndexOfJsonStart(string text)
    {
        // Prefer an object start; fall back to an array start that is followed by an object or string.
        int obj = text.IndexOf('{');
        int arr = text.IndexOf('[');
        if (obj < 0) return arr;
        if (arr < 0) return obj;
        if (arr < obj)
        {
            var after = text[(arr + 1)..].TrimStart();
            if (after.StartsWith('{') || after.StartsWith('"')) return arr;
        }
        return obj;
    }

    /// <summary>Escapes raw line breaks and tabs that appear inside string literals (invalid JSON).</summary>
    public static string EscapeControlCharsInStrings(string json)
    {
        var sb = new StringBuilder(json.Length + 16);
        bool inString = false, escape = false;
        foreach (var ch in json)
        {
            if (inString)
            {
                if (escape) { sb.Append(ch); escape = false; continue; }
                switch (ch)
                {
                    case '\\': sb.Append(ch); escape = true; continue;
                    case '"': sb.Append(ch); inString = false; continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': continue;
                    case '\t': sb.Append("\\t"); continue;
                    default:
                        if (ch < ' ') continue;
                        sb.Append(ch);
                        continue;
                }
            }
            if (ch == '"') inString = true;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Inserts the comma that LLMs sometimes forget between two objects/arrays/strings in a list.</summary>
    public static string InsertMissingCommas(string json)
    {
        var t = MissingCommaObjects().Replace(json, "},$1{");
        t = MissingCommaArrays().Replace(t, "],$1[");
        t = MissingCommaStrings().Replace(t, "\",$1\"");
        return t;
    }

    /// <summary>Replaces typographic quotes with straight ones (only used as a last resort).</summary>
    public static string NormalizeQuotes(string json) =>
        json.Replace('“', '"').Replace('”', '"').Replace('„', '"').Replace('‟', '"')
            .Replace('«', '"').Replace('»', '"');

    /// <summary>
    /// If the JSON ends abruptly, cuts it after the last complete element of an array and closes
    /// every open bracket, so all complete items can still be imported.
    /// </summary>
    public static bool TryCloseTruncated(string json, out string repaired)
    {
        repaired = json;
        var stack = new List<char>();
        bool inString = false, escape = false;
        int lastCut = -1;
        List<char>? lastStack = null;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': stack.Add('{'); break;
                case '[': stack.Add('['); break;
                case '}':
                case ']':
                    if (stack.Count == 0) return false;
                    stack.RemoveAt(stack.Count - 1);
                    if (c == '}' && stack.Count > 0 && stack[^1] == '[')
                    {
                        // A complete object inside an array: a safe place to cut.
                        lastCut = i + 1;
                        lastStack = new List<char>(stack);
                    }
                    break;
            }
        }

        bool truncated = inString || stack.Count > 0;
        if (!truncated || lastCut < 0 || lastStack == null) return false;

        var sb = new StringBuilder(json, 0, lastCut, lastCut + lastStack.Count + 2);
        for (int i = lastStack.Count - 1; i >= 0; i--) sb.Append(lastStack[i] == '{' ? '}' : ']');
        repaired = sb.ToString();
        return true;
    }

    [GeneratedRegex(@"```[ \t]*[A-Za-z0-9_+-]*[ \t]*\r?\n(?<body>[\s\S]*?)```", RegexOptions.Compiled)]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"^```[ \t]*[A-Za-z0-9_+-]*[ \t]*\r?\n")]
    private static partial Regex OpenFenceRegex();

    [GeneratedRegex(@"\}(\s*)\{")]
    private static partial Regex MissingCommaObjects();

    [GeneratedRegex(@"\](\s*)\[")]
    private static partial Regex MissingCommaArrays();

    [GeneratedRegex("\"(\\s*\\n\\s*)\"")]
    private static partial Regex MissingCommaStrings();
}
