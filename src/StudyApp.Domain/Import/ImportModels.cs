using StudyApp.Domain.Models;

namespace StudyApp.Domain.Import;

public enum IssueLevel
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public sealed class ImportIssue
{
    public IssueLevel Level { get; init; }
    public string Message { get; init; } = "";
    /// <summary>1-based item number in the file (null for file-level issues).</summary>
    public int? ItemNumber { get; init; }

    public static ImportIssue Info(string message, int? item = null) => new() { Level = IssueLevel.Info, Message = message, ItemNumber = item };
    public static ImportIssue Warning(string message, int? item = null) => new() { Level = IssueLevel.Warning, Message = message, ItemNumber = item };
    public static ImportIssue Error(string message, int? item = null) => new() { Level = IssueLevel.Error, Message = message, ItemNumber = item };
}

public sealed class ImportChapter
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Pages { get; set; } = "";

    public bool IsEmpty => string.IsNullOrWhiteSpace(Number) && string.IsNullOrWhiteSpace(Title);
    public string DisplayName => string.IsNullOrWhiteSpace(Number) ? Title : string.IsNullOrWhiteSpace(Title) ? Number : $"{Number}. {Title}";
}

public sealed class ImportDeck
{
    public string Title { get; set; } = "";
    public DeckKind? Kind { get; set; }
    public string Description { get; set; } = "";
    public int Difficulty { get; set; }
    public string Source { get; set; } = "";
    public string Language { get; set; } = "";
    public ImportChapter? Chapter { get; set; }
    public List<Item> Items { get; } = new();
    /// <summary>File the deck came from (multi-file imports).</summary>
    public string FileName { get; set; } = "";

    public DeckKind EffectiveKind
    {
        get
        {
            if (Kind.HasValue) return Kind.Value;
            if (Items.Count == 0) return DeckKind.Deck;
            int notes = Items.Count(i => i.Type == ItemTypes.Note);
            int questions = Items.Count(i => i.IsQuestion);
            int cards = Items.Count - notes - questions;
            if (notes >= questions && notes >= cards) return DeckKind.Notes;
            return questions > cards ? DeckKind.Test : DeckKind.Deck;
        }
    }
}

public sealed class ImportPackage
{
    public int SchemaVersion { get; set; } = 1;
    public string Subject { get; set; } = "";
    public string Source { get; set; } = "";
    public string Language { get; set; } = "";
    public List<ImportDeck> Decks { get; } = new();

    public IEnumerable<Item> AllItems => Decks.SelectMany(d => d.Items);
    public int ItemCount => Decks.Sum(d => d.Items.Count);

    public Dictionary<string, int> CountsByType() =>
        AllItems.GroupBy(i => i.Type).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Merges several parsed files into one package (multi-file import).</summary>
    public static ImportPackage Merge(IEnumerable<ImportPackage> packages)
    {
        var list = packages.ToList();
        if (list.Count == 1) return list[0];
        var merged = new ImportPackage
        {
            Subject = list.Select(p => p.Subject).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "",
            Source = list.Select(p => p.Source).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "",
            Language = list.Select(p => p.Language).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "",
            SchemaVersion = list.Max(p => p.SchemaVersion),
        };
        foreach (var p in list) merged.Decks.AddRange(p.Decks);
        return merged;
    }
}

public sealed class ParseResult
{
    public ImportPackage? Package { get; set; }
    public List<ImportIssue> Issues { get; } = new();

    /// <summary>Set when the text could not be read as JSON at all.</summary>
    public string? Error { get; set; }
    public int? ErrorLine { get; set; }
    public int? ErrorColumn { get; set; }
    public string? ErrorSnippet { get; set; }

    public bool Truncated { get; set; }
    public bool Repaired { get; set; }
    public string FileName { get; set; } = "";

    public bool Success => Package != null && Package.ItemCount > 0;
    public int ErrorCount => Issues.Count(i => i.Level == IssueLevel.Error) + (Error != null ? 1 : 0);
    public int WarningCount => Issues.Count(i => i.Level == IssueLevel.Warning);
}
