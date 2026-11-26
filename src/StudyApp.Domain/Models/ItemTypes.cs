using StudyApp.Domain.Localization;

namespace StudyApp.Domain.Models;

/// <summary>
/// Registry of item types. To add a new type: add a key here, teach <see cref="Import.ImportParser"/>
/// to read it, <see cref="Grading.Grader"/> to grade it, and add a view in the UI (ItemViews).
/// No database migration is needed — type-specific fields live in <see cref="ItemData"/>.
/// </summary>
public static class ItemTypes
{
    public const string Flashcard = "flashcard";
    public const string Single = "single";
    public const string Multiple = "multiple";
    public const string TrueFalse = "truefalse";
    public const string Short = "short";
    public const string Note = "note";

    public static readonly IReadOnlyList<string> All = new[] { Flashcard, Single, Multiple, TrueFalse, Short, Note };
    public static readonly IReadOnlyList<string> Studyable = new[] { Flashcard, Single, Multiple, TrueFalse, Short };
    public static readonly IReadOnlyList<string> Questions = new[] { Single, Multiple, TrueFalse, Short };

    public static bool IsQuestion(string type) => type is Single or Multiple or TrueFalse or Short;
    public static bool IsStudyable(string type) => type != Note;
    public static bool IsChoice(string type) => type is Single or Multiple;

    public static string DisplayName(string type) => L.T(type switch
    {
        Flashcard => "Flashcard",
        Single => "Single choice",
        Multiple => "Multiple choice",
        TrueFalse => "True / False",
        Short => "Short answer",
        Note => "Note",
        _ => type,
    });

    public static string PluralName(string type) => L.T(type switch
    {
        Flashcard => "Flashcards",
        Single => "Single choice",
        Multiple => "Multiple choice",
        TrueFalse => "True / False",
        Short => "Short answer",
        Note => "Notes",
        _ => type,
    });

    /// <summary>Groups used for duplicate detection: a flashcard never duplicates a question.</summary>
    public static string FingerprintGroup(string type) => type switch
    {
        Flashcard => "card",
        Note => "note",
        _ => "q",
    };

    /// <summary>Maps the many spellings LLMs use to our keys. Returns null when unknown.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var k = new string(raw.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return k switch
        {
            "flashcard" or "flashcards" or "card" or "cards" or "flash" or "qa" or "term" or "definition" or "glossary" => Flashcard,
            "single" or "singlechoice" or "single_choice" or "mcq" or "choice" or "multiplechoice" or "multiplechoicesingle" or "radio" or "onecorrect" or "singleanswer" => Single,
            "multiple" or "multi" or "multiselect" or "multipleselect" or "multipleanswer" or "multipleanswers" or "multiplecorrect" or "checkbox" or "checkboxes" or "selectall" or "multiplechoicemultiple" or "manycorrect" => Multiple,
            "truefalse" or "tf" or "boolean" or "bool" or "trueorfalse" or "yesno" => TrueFalse,
            "short" or "shortanswer" or "open" or "openended" or "essay" or "freetext" or "free" or "written" or "fill" or "fillin" or "fillintheblank" or "fillblank" or "cloze" or "text" or "answer" => Short,
            "note" or "notes" or "summary" or "section" or "reading" => Note,
            _ => null,
        };
    }
}

/// <summary>Difficulty is stored as 1–5 (0 = not set).</summary>
public static class Difficulty
{
    public const int None = 0;
    public const int VeryEasy = 1;
    public const int Easy = 2;
    public const int Medium = 3;
    public const int Hard = 4;
    public const int Expert = 5;

    public static string Label(int d) => d switch
    {
        1 => L.T("Very easy"),
        2 => L.T("Easy"),
        3 => L.T("Medium"),
        4 => L.T("Hard"),
        5 => L.T("Expert"),
        _ => "",
    };

    /// <summary>Accepts 1–5, "easy/medium/hard/expert", "3/5", Bulgarian words, etc.</summary>
    public static int Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return None;
        var s = raw.Trim().ToLowerInvariant();
        var slash = s.IndexOf('/');
        if (slash > 0) s = s[..slash].Trim();
        if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
            return Clamp((int)Math.Round(n));

        s = s.Replace("-", " ").Replace("_", " ");
        if (s.Contains("very easy") || s.Contains("basic") || s.Contains("много лес") || s.Contains("начал")) return VeryEasy;
        if (s.Contains("very hard") || s.Contains("expert") || s.Contains("exam") || s.Contains("advanced") || s.Contains("challeng") || s.Contains("експерт") || s.Contains("много труд") || s.Contains("изпит")) return Expert;
        if (s.Contains("easy") || s.Contains("simple") || s.Contains("лес")) return Easy;
        if (s.Contains("medium") || s.Contains("moderate") || s.Contains("intermediate") || s.Contains("normal") || s.Contains("средн")) return Medium;
        if (s.Contains("hard") || s.Contains("difficult") || s.Contains("труд")) return Hard;
        return None;
    }

    public static int Clamp(int d) => d <= 0 ? None : Math.Min(5, d);
}
