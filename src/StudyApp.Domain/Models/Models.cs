namespace StudyApp.Domain.Models;

/// <summary>What a deck mainly contains. Decks may still mix item types.</summary>
public enum DeckKind
{
    Deck = 0,   // flashcards
    Test = 1,   // questions
    Notes = 2,  // reading notes / summaries
}

public enum SrsState
{
    New = 0,
    Learning = 1,
    Review = 2,
    Relearning = 3,
}

/// <summary>Self-rating after an answer (SM-2 input). Keys 1–4.</summary>
public enum Rating
{
    Again = 1,
    Hard = 2,
    Good = 3,
    Easy = 4,
}

/// <summary>Outcome of an answer: Missed / Partial / Got it.</summary>
public enum Result
{
    None = -1,
    Missed = 0,
    Partial = 1,
    GotIt = 2,
}

/// <summary>A subject ("project"), e.g. "Anatomy".</summary>
public sealed class Subject
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Default source/textbook name for items in this subject.</summary>
    public string Source { get; set; } = "";
    public string Color { get; set; } = "#4F6BED";
    /// <summary>Icon key (see the icon catalogue in the UI).</summary>
    public string Icon { get; set; } = "book";
    public bool Archived { get; set; }
    public int SortOrder { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? LastOpenedAt { get; set; }
    public long? DeletedAt { get; set; }
}

public sealed class Chapter
{
    public long Id { get; set; }
    public long SubjectId { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Pages { get; set; } = "";
    public int SortOrder { get; set; }
    public long CreatedAt { get; set; }
    public long? DeletedAt { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Number) ? Title : $"{Number}. {Title}";
}

public sealed class Deck
{
    public long Id { get; set; }
    public long SubjectId { get; set; }
    public long? ChapterId { get; set; }
    public DeckKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";
    /// <summary>0 = not set, 1–5.</summary>
    public int Difficulty { get; set; }
    public string Language { get; set; } = "";
    public int SortOrder { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? LastStudiedAt { get; set; }
    public long? DeletedAt { get; set; }
}

/// <summary>An answer option for choice questions. <see cref="Why"/> explains why it is right or wrong.</summary>
public sealed class ChoiceOption
{
    public string Text { get; set; } = "";
    public string? Why { get; set; }

    public ChoiceOption() { }
    public ChoiceOption(string text, string? why = null)
    {
        Text = text;
        Why = why;
    }
}

/// <summary>Type-specific fields, stored as JSON in items.data so new item types need no migration.</summary>
public sealed class ItemData
{
    public List<ChoiceOption>? Options { get; set; }
    /// <summary>0-based indices of the correct options (single / multiple).</summary>
    public List<int>? Correct { get; set; }
    /// <summary>Answer of a true/false statement.</summary>
    public bool? Truth { get; set; }
    /// <summary>Accepted alternative answers (short answer).</summary>
    public List<string>? Accept { get; set; }
    /// <summary>Points a good answer should cover (short answer).</summary>
    public List<string>? KeyPoints { get; set; }
    public string? Hint { get; set; }

    public ItemData Clone() => new()
    {
        Options = Options?.Select(o => new ChoiceOption(o.Text, o.Why)).ToList(),
        Correct = Correct?.ToList(),
        Truth = Truth,
        Accept = Accept?.ToList(),
        KeyPoints = KeyPoints?.ToList(),
        Hint = Hint,
    };
}

/// <summary>A study item of any type (flashcard, question, note section).</summary>
public sealed class Item
{
    public long Id { get; set; }
    public long DeckId { get; set; }
    public string Type { get; set; } = ItemTypes.Flashcard;
    /// <summary>Front of a flashcard, question text, statement, or note heading.</summary>
    public string Prompt { get; set; } = "";
    /// <summary>Back of a flashcard, model answer, or note body (markdown).</summary>
    public string Answer { get; set; } = "";
    public ItemData Data { get; set; } = new();
    public string Explanation { get; set; } = "";
    public string Page { get; set; } = "";
    public string Source { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    /// <summary>0 = not set, 1 (very easy) … 5 (expert).</summary>
    public int Difficulty { get; set; }
    public string Fingerprint { get; set; } = "";

    public bool Flagged { get; set; }
    public bool WrongInSource { get; set; }
    public string PersonalNote { get; set; } = "";
    public bool Suspended { get; set; }

    // Spaced repetition (SM-2)
    public SrsState SrsState { get; set; } = SrsState.New;
    public int SrsStep { get; set; }
    public double SrsEase { get; set; } = 2.5;
    public double SrsInterval { get; set; }
    public long? SrsDue { get; set; }
    public int SrsReps { get; set; }
    public int SrsLapses { get; set; }

    // Statistics
    public int SeenCount { get; set; }
    public int CorrectCount { get; set; }
    public Result LastResult { get; set; } = Result.None;
    public long? LastSeenAt { get; set; }

    public int SortOrder { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? DeletedAt { get; set; }

    // Context filled by queries that join decks/subjects/chapters (not stored on the item row).
    public long SubjectId { get; set; }
    public long? ChapterId { get; set; }
    public string DeckTitle { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string SubjectColor { get; set; } = "";
    public string ChapterTitle { get; set; } = "";

    public bool IsQuestion => ItemTypes.IsQuestion(Type);
    public bool IsStudyable => ItemTypes.IsStudyable(Type);
    public double Accuracy => SeenCount == 0 ? 0 : (double)CorrectCount / SeenCount;
    public bool IsMastered => SrsState == SrsState.Review && SrsInterval >= 21;
    public string Hint => Data.Hint ?? "";

    /// <summary>Text representation of the correct answer (used in summaries and exports).</summary>
    public string CorrectAnswerText()
    {
        switch (Type)
        {
            case ItemTypes.Single:
            case ItemTypes.Multiple:
                var opts = Data.Options ?? new();
                var idx = Data.Correct ?? new();
                return string.Join("; ", idx.Where(i => i >= 0 && i < opts.Count).Select(i => $"{(char)('A' + i)}) {opts[i].Text}"));
            case ItemTypes.TrueFalse:
                return Data.Truth == true ? Localization.L.T("True") : Localization.L.T("False");
            default:
                return Answer;
        }
    }

    public Item CloneContent() => new()
    {
        Type = Type,
        Prompt = Prompt,
        Answer = Answer,
        Data = Data.Clone(),
        Explanation = Explanation,
        Page = Page,
        Source = Source,
        Tags = Tags.ToList(),
        Difficulty = Difficulty,
        Fingerprint = Fingerprint,
    };
}

/// <summary>One answered item (feeds stats, streaks and accuracy over time).</summary>
public sealed class ReviewRecord
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public long? SessionId { get; set; }
    public long SubjectId { get; set; }
    public long ReviewedAt { get; set; }
    public string StudyDay { get; set; } = "";
    public Rating Rating { get; set; }
    public Result Result { get; set; }
    public long DurationMs { get; set; }
    public bool WasNew { get; set; }
}

public enum SessionMode
{
    Flashcards = 0,
    Test = 1,
}

/// <summary>A finished (or abandoned) study session.</summary>
public sealed class SessionRecord
{
    public long Id { get; set; }
    public long? SubjectId { get; set; }
    public SessionMode Mode { get; set; }
    public string Title { get; set; } = "";
    public string SettingsJson { get; set; } = "{}";
    public long StartedAt { get; set; }
    public long? EndedAt { get; set; }
    public int ItemCount { get; set; }
    public int Correct { get; set; }
    public int Partial { get; set; }
    public int Wrong { get; set; }
    public double Score { get; set; }
    public long DurationMs { get; set; }
}

/// <summary>A prompt template the user can copy into ChatGPT/Claude.</summary>
public sealed class PromptTemplate
{
    public long Id { get; set; }
    /// <summary>Stable key for built-ins ("flashcards", "mcq", …) or "custom-…".</summary>
    public string Key { get; set; } = "";
    public string Lang { get; set; } = "en";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsBuiltIn { get; set; }
    public bool IsModified { get; set; }
    public int SortOrder { get; set; }
    public long UpdatedAt { get; set; }
}
