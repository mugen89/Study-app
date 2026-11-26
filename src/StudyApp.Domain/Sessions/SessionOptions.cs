using System.Text.Json;
using StudyApp.Domain.Models;

namespace StudyApp.Domain.Sessions;

public enum ItemOrder
{
    /// <summary>Flashcards: due first, then new. Tests: the order of the deck.</summary>
    Default = 0,
    Shuffle = 1,
    Difficulty = 2,
    PageOrder = 3,
}

public enum FeedbackTiming
{
    Immediate = 0,
    AtEnd = 1,
}

/// <summary>Everything that defines a study session: scope, filters, order and length.</summary>
public sealed class SessionOptions
{
    public SessionMode Mode { get; set; } = SessionMode.Flashcards;
    public string Title { get; set; } = "";

    // Scope (empty lists = everything in the subject; SubjectId null = all subjects)
    public long? SubjectId { get; set; }
    public List<long> ChapterIds { get; set; } = new();
    public List<long> DeckIds { get; set; } = new();
    /// <summary>Explicit items (e.g. "review missed items now"). Overrides scope and filters.</summary>
    public List<long> ItemIds { get; set; } = new();

    // Filters
    public List<int> Difficulties { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<string> Types { get; set; } = new();
    public bool OnlyMissed { get; set; }
    public bool OnlyFlagged { get; set; }
    public bool OnlyNew { get; set; }
    public bool OnlyDue { get; set; }

    // Order
    public ItemOrder Order { get; set; } = ItemOrder.Default;
    public bool ShuffleChoices { get; set; } = true;

    // Length: 0 = no limit
    public int MaxItems { get; set; } = 20;
    public int TimeLimitMinutes { get; set; }

    // Tests
    public FeedbackTiming Feedback { get; set; } = FeedbackTiming.Immediate;

    public bool HasFilters => Difficulties.Count > 0 || Tags.Count > 0 || OnlyMissed || OnlyFlagged || OnlyNew || OnlyDue;

    public static SessionOptions DefaultFlashcards() => new()
    {
        Mode = SessionMode.Flashcards,
        Types = new() { ItemTypes.Flashcard },
        MaxItems = 0,
        OnlyDue = false,
    };

    public static SessionOptions DefaultTest() => new()
    {
        Mode = SessionMode.Test,
        Types = ItemTypes.Questions.ToList(),
        MaxItems = 20,
        ShuffleChoices = true,
        Order = ItemOrder.Shuffle,
    };

    public SessionOptions Clone() => JsonSerializer.Deserialize<SessionOptions>(JsonSerializer.Serialize(this))!;

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Keeps only the reusable preferences (not the scope) when remembering the last setup.</summary>
    public SessionOptions PreferencesOnly()
    {
        var c = Clone();
        c.SubjectId = null;
        c.ChapterIds.Clear();
        c.DeckIds.Clear();
        c.ItemIds.Clear();
        c.Tags.Clear();
        c.Title = "";
        return c;
    }
}
