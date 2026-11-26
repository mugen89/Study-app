using StudyApp.Domain;
using StudyApp.Domain.Import;
using StudyApp.Domain.Localization;
using StudyApp.Domain.Models;

namespace StudyApp.Data.Services;

public enum DuplicatePolicy
{
    Skip = 0,
    Replace = 1,
    KeepBoth = 2,
}

public enum ChapterTarget
{
    /// <summary>Use (or create) the chapter named in the file.</summary>
    FromFile = 0,
    /// <summary>Put everything into one existing chapter.</summary>
    Existing = 1,
    /// <summary>No chapter.</summary>
    None = 2,
}

/// <summary>Where and how to import a parsed package.</summary>
public sealed class ImportPlan
{
    public required ImportPackage Package { get; init; }
    public long? SubjectId { get; set; }
    public string NewSubjectName { get; set; } = "";
    public string NewSubjectSource { get; set; } = "";
    public string NewSubjectColor { get; set; } = "#4F6BED";
    public ChapterTarget ChapterMode { get; set; } = ChapterTarget.FromFile;
    public long? ChapterId { get; set; }
    public DuplicatePolicy Duplicates { get; set; } = DuplicatePolicy.Skip;
}

public sealed record DeckPlan(ImportDeck Deck, string Title, string ChapterLabel, bool MergesIntoExisting, int Duplicates);

public sealed class ImportAnalysis
{
    public int Total { get; set; }
    public int DuplicatesExisting { get; set; }
    public int DuplicatesInFile { get; set; }
    public List<DeckPlan> Decks { get; } = new();
}

public sealed class ImportOutcome
{
    public long SubjectId { get; set; }
    public List<long> DeckIds { get; } = new();
    public int Added { get; set; }
    public int Replaced { get; set; }
    public int Skipped { get; set; }
}

public static class ImportService
{
    public static string DefaultDeckTitle(ImportDeck deck)
    {
        var kind = deck.EffectiveKind switch
        {
            DeckKind.Test => L.T("Test"),
            DeckKind.Notes => L.T("Notes"),
            _ => L.T("Flashcards"),
        };
        if (deck.Chapter != null && !string.IsNullOrWhiteSpace(deck.Chapter.Title)) return $"{deck.Chapter.Title} – {kind}";
        if (deck.Chapter != null && !string.IsNullOrWhiteSpace(deck.Chapter.Number)) return $"{L.F("Chapter {0}", deck.Chapter.Number)} – {kind}";
        return kind;
    }

    /// <summary>Shows what will happen without changing anything.</summary>
    public static ImportAnalysis Analyze(StudyStore store, ImportPlan plan)
    {
        var analysis = new ImportAnalysis { Total = plan.Package.ItemCount };
        var existing = plan.SubjectId.HasValue ? store.FingerprintsInSubject(plan.SubjectId.Value) : new Dictionary<string, long>();
        var chapters = plan.SubjectId.HasValue ? store.GetChapters(plan.SubjectId.Value) : new List<Chapter>();
        var decks = plan.SubjectId.HasValue ? store.GetDecks(plan.SubjectId.Value) : new List<Deck>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var deck in plan.Package.Decks)
        {
            int dup = 0;
            foreach (var item in deck.Items)
            {
                if (existing.ContainsKey(item.Fingerprint)) { analysis.DuplicatesExisting++; dup++; }
                else if (!seen.Add(item.Fingerprint)) { analysis.DuplicatesInFile++; dup++; }
            }

            var (chapterId, chapterLabel) = DescribeChapter(plan, deck, chapters);
            var title = string.IsNullOrWhiteSpace(deck.Title) ? DefaultDeckTitle(deck) : deck.Title;
            long? targetChapter = chapterId == 0 ? null : chapterId;
            bool merges = chapterId != -1 && decks.Any(d => d.ChapterId == targetChapter
                                                           && d.Kind == deck.EffectiveKind
                                                           && string.Equals(d.Title, title, StringComparison.CurrentCultureIgnoreCase));
            analysis.Decks.Add(new DeckPlan(deck, title, chapterLabel, merges, dup));
        }
        return analysis;
    }

    /// <returns>(chapter id: 0 = none, -1 = will be created), label</returns>
    private static (long Id, string Label) DescribeChapter(ImportPlan plan, ImportDeck deck, List<Chapter> chapters)
    {
        switch (plan.ChapterMode)
        {
            case ChapterTarget.None:
                return (0, L.T("No chapter"));
            case ChapterTarget.Existing:
                var c = chapters.FirstOrDefault(x => x.Id == plan.ChapterId);
                return c == null ? (0, L.T("No chapter")) : (c.Id, c.DisplayName);
            default:
                if (deck.Chapter == null) return (0, L.T("No chapter"));
                var match = FindChapter(chapters, deck.Chapter);
                return match != null ? (match.Id, match.DisplayName) : (-1, deck.Chapter.DisplayName + " " + L.T("(new)"));
        }
    }

    private static Chapter? FindChapter(IEnumerable<Chapter> chapters, ImportChapter info) =>
        chapters.FirstOrDefault(c =>
            (!string.IsNullOrWhiteSpace(info.Number) && string.Equals(c.Number.Trim(), info.Number.Trim(), StringComparison.OrdinalIgnoreCase)
             && (string.IsNullOrWhiteSpace(info.Title) || string.IsNullOrWhiteSpace(c.Title) || string.Equals(c.Title.Trim(), info.Title.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            || (string.IsNullOrWhiteSpace(info.Number) && !string.IsNullOrWhiteSpace(info.Title)
                && string.Equals(c.Title.Trim(), info.Title.Trim(), StringComparison.CurrentCultureIgnoreCase)))
        ?? chapters.FirstOrDefault(c => !string.IsNullOrWhiteSpace(info.Title) && string.Equals(c.Title.Trim(), info.Title.Trim(), StringComparison.CurrentCultureIgnoreCase));

    public static ImportOutcome Execute(StudyStore store, ImportPlan plan)
    {
        var outcome = new ImportOutcome();
        store.Db.Tx(() =>
        {
            long subjectId;
            if (plan.SubjectId.HasValue)
            {
                subjectId = plan.SubjectId.Value;
            }
            else
            {
                var name = string.IsNullOrWhiteSpace(plan.NewSubjectName)
                    ? (string.IsNullOrWhiteSpace(plan.Package.Subject) ? L.T("Imported") : plan.Package.Subject)
                    : plan.NewSubjectName;
                subjectId = store.CreateSubject(new Subject
                {
                    Name = name.Trim(),
                    Source = string.IsNullOrWhiteSpace(plan.NewSubjectSource) ? plan.Package.Source : plan.NewSubjectSource,
                    Color = plan.NewSubjectColor,
                });
            }
            outcome.SubjectId = subjectId;

            var existing = store.FingerprintsInSubject(subjectId);
            var chapters = store.GetChapters(subjectId);
            var decks = store.GetDecks(subjectId);
            var seenInFile = new HashSet<string>(StringComparer.Ordinal);

            foreach (var importDeck in plan.Package.Decks)
            {
                long? chapterId = ResolveChapter(store, plan, importDeck, subjectId, chapters);
                var title = string.IsNullOrWhiteSpace(importDeck.Title) ? DefaultDeckTitle(importDeck) : importDeck.Title.Trim();
                var kind = importDeck.EffectiveKind;

                var deck = decks.FirstOrDefault(d => d.ChapterId == chapterId && d.Kind == kind
                                                     && string.Equals(d.Title, title, StringComparison.CurrentCultureIgnoreCase));
                if (deck == null)
                {
                    deck = new Deck
                    {
                        SubjectId = subjectId,
                        ChapterId = chapterId,
                        Kind = kind,
                        Title = title,
                        Description = importDeck.Description,
                        Source = importDeck.Source,
                        Difficulty = importDeck.Difficulty,
                        Language = importDeck.Language,
                    };
                    store.CreateDeck(deck);
                    decks.Add(deck);
                }
                if (!outcome.DeckIds.Contains(deck.Id)) outcome.DeckIds.Add(deck.Id);

                int sort = store.NextItemSortOrder(deck.Id);
                foreach (var item in importDeck.Items)
                {
                    var fp = string.IsNullOrEmpty(item.Fingerprint) ? TextUtil.Fingerprint(item.Type, item.Prompt) : item.Fingerprint;
                    bool dupInFile = !seenInFile.Add(fp);

                    if (existing.TryGetValue(fp, out var existingId) && plan.Duplicates != DuplicatePolicy.KeepBoth)
                    {
                        if (plan.Duplicates == DuplicatePolicy.Skip || dupInFile)
                        {
                            outcome.Skipped++;
                            continue;
                        }
                        var current = store.GetItem(existingId);
                        if (current == null) { outcome.Skipped++; continue; }
                        current.Type = item.Type;
                        current.Prompt = item.Prompt;
                        current.Answer = item.Answer;
                        current.Data = item.Data;
                        current.Explanation = item.Explanation;
                        current.Page = item.Page;
                        current.Tags = item.Tags;
                        current.Difficulty = item.Difficulty;
                        current.Source = item.Source;
                        current.WrongInSource = false;
                        store.UpdateItemContent(current);
                        outcome.Replaced++;
                        continue;
                    }
                    if (dupInFile && plan.Duplicates != DuplicatePolicy.KeepBoth)
                    {
                        outcome.Skipped++;
                        continue;
                    }

                    var copy = item.CloneContent();
                    copy.DeckId = deck.Id;
                    copy.SortOrder = sort++;
                    copy.Fingerprint = fp;
                    if (string.IsNullOrWhiteSpace(copy.Source)) copy.Source = deck.Source;
                    store.InsertItem(copy);
                    existing.TryAdd(fp, copy.Id);
                    outcome.Added++;
                }
            }
            store.TouchSubject(subjectId);
        });
        Log.Info($"Imported into subject {outcome.SubjectId}: {outcome.Added} added, {outcome.Replaced} replaced, {outcome.Skipped} skipped.");
        return outcome;
    }

    private static long? ResolveChapter(StudyStore store, ImportPlan plan, ImportDeck deck, long subjectId, List<Chapter> chapters)
    {
        switch (plan.ChapterMode)
        {
            case ChapterTarget.None:
                return null;
            case ChapterTarget.Existing:
                return chapters.Any(c => c.Id == plan.ChapterId) ? plan.ChapterId : null;
            default:
                if (deck.Chapter == null || deck.Chapter.IsEmpty) return null;
                var match = FindChapter(chapters, deck.Chapter);
                if (match != null)
                {
                    if (string.IsNullOrWhiteSpace(match.Pages) && !string.IsNullOrWhiteSpace(deck.Chapter.Pages))
                    {
                        match.Pages = deck.Chapter.Pages;
                        store.UpdateChapter(match);
                    }
                    return match.Id;
                }
                var chapter = new Chapter
                {
                    SubjectId = subjectId,
                    Number = deck.Chapter.Number.Trim(),
                    Title = string.IsNullOrWhiteSpace(deck.Chapter.Title) ? L.F("Chapter {0}", deck.Chapter.Number) : deck.Chapter.Title.Trim(),
                    Pages = deck.Chapter.Pages.Trim(),
                };
                store.CreateChapter(chapter);
                chapters.Add(chapter);
                return chapter.Id;
        }
    }
}
