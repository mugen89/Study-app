using StudyApp.Data;
using StudyApp.Data.Services;
using StudyApp.Domain;
using StudyApp.Domain.Import;
using StudyApp.Domain.Models;
using StudyApp.Domain.Prompts;
using StudyApp.Domain.Samples;
using StudyApp.Domain.Sessions;
using StudyApp.Domain.Settings;

namespace StudyApp.Tests;

/// <summary>Each test gets its own database in a temp folder.</summary>
public sealed class TempStore : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "studyapp-tests-" + Guid.NewGuid().ToString("N")[..8]);
    public StudyStore Store { get; }

    public TempStore()
    {
        Directory.CreateDirectory(Dir);
        Store = new StudyStore(Path.Combine(Dir, "study.db"));
    }

    public ImportOutcome ImportSample(DuplicatePolicy policy = DuplicatePolicy.Skip, long? subjectId = null)
    {
        var pkg = ImportParser.Parse(SampleContent.Json("en")).Package!;
        return ImportService.Execute(Store, new ImportPlan { Package = pkg, SubjectId = subjectId, Duplicates = policy });
    }

    public void Dispose()
    {
        Store.Dispose();
        try { Directory.Delete(Dir, recursive: true); } catch { /* temp */ }
    }
}

public class DataTests
{
    [Fact]
    public void MigrationsCreateTheSchemaAndAreIdempotent()
    {
        using var t = new TempStore();
        Assert.Equal(1L, t.Store.Db.Scalar<long>("PRAGMA user_version"));
        t.Store.Dispose();
        using var again = new StudyStore(Path.Combine(t.Dir, "study.db"));
        Assert.Equal(1L, again.Db.Scalar<long>("PRAGMA user_version"));
    }

    [Fact]
    public void ImportCreatesSubjectChapterDecksAndItems()
    {
        using var t = new TempStore();
        var outcome = t.ImportSample();
        Assert.Equal(17, outcome.Added);
        var subject = t.Store.GetSubject(outcome.SubjectId)!;
        Assert.Equal("Human Biology (sample)", subject.Name);
        var chapters = t.Store.GetChapters(subject.Id);
        Assert.Single(chapters);
        Assert.Equal("10-19", chapters[0].Pages);
        Assert.Equal(3, t.Store.GetDecks(subject.Id).Count);
    }

    [Fact]
    public void ReimportDetectsDuplicates()
    {
        using var t = new TempStore();
        var first = t.ImportSample();
        var skipped = t.ImportSample(DuplicatePolicy.Skip, first.SubjectId);
        Assert.Equal(0, skipped.Added);
        Assert.Equal(17, skipped.Skipped);

        var replaced = t.ImportSample(DuplicatePolicy.Replace, first.SubjectId);
        Assert.Equal(17, replaced.Replaced);

        var both = t.ImportSample(DuplicatePolicy.KeepBoth, first.SubjectId);
        Assert.Equal(17, both.Added);
        Assert.Equal(34, t.Store.QueryItems(new ItemQuery { SubjectId = first.SubjectId, WrongInSource = null }).Count);
    }

    [Fact]
    public void ReplaceKeepsStudyProgress()
    {
        using var t = new TempStore();
        var first = t.ImportSample();
        var item = t.Store.QueryItems(new ItemQuery { SubjectId = first.SubjectId, Types = { ItemTypes.Flashcard } }).First();
        t.Store.RecordAnswer(item, Rating.Good, Result.GotIt, 1000, null, true, new AppSettings().ToSrs());
        t.ImportSample(DuplicatePolicy.Replace, first.SubjectId);
        var reloaded = t.Store.GetItem(item.Id)!;
        Assert.Equal(1, reloaded.SeenCount);
        Assert.Equal(SrsState.Learning, reloaded.SrsState);
    }

    [Fact]
    public void SoftDeleteAndRestoreThroughTheRecycleBin()
    {
        using var t = new TempStore();
        var outcome = t.ImportSample();
        var chapter = t.Store.GetChapters(outcome.SubjectId).Single();
        t.Store.DeleteChapter(chapter.Id);
        Assert.Empty(t.Store.GetDecks(outcome.SubjectId));
        Assert.Empty(t.Store.QueryItems(new ItemQuery { SubjectId = outcome.SubjectId }));
        var trash = t.Store.GetTrash();
        Assert.Contains(trash, e => e.Kind == TrashKind.Chapter && e.Id == chapter.Id);
        Assert.DoesNotContain(trash, e => e.Kind == TrashKind.Deck);

        t.Store.Restore(TrashKind.Chapter, chapter.Id);
        Assert.Equal(3, t.Store.GetDecks(outcome.SubjectId).Count);

        t.Store.DeleteSubject(outcome.SubjectId);
        Assert.Null(t.Store.GetSubject(outcome.SubjectId));
        Assert.Equal(1, t.Store.TrashCount());
        t.Store.EmptyTrash();
        Assert.Equal(0L, t.Store.Db.Scalar<long>("SELECT COUNT(*) FROM items"));
    }

    [Fact]
    public void RecordAnswerUpdatesScheduleAndUndoRestoresIt()
    {
        using var t = new TempStore();
        var outcome = t.ImportSample();
        var item = t.Store.QueryItems(new ItemQuery { SubjectId = outcome.SubjectId, Types = { ItemTypes.Flashcard } }).First();
        var undo = t.Store.RecordAnswer(item, Rating.Easy, Result.GotIt, 2000, null, true, new AppSettings().ToSrs());
        Assert.Equal(SrsState.Review, t.Store.GetItem(item.Id)!.SrsState);
        Assert.Equal(1, t.Store.CountAnswersToday());
        Assert.Equal(1, t.Store.CountNewToday());

        t.Store.UndoAnswer(undo, item);
        var back = t.Store.GetItem(item.Id)!;
        Assert.Equal(SrsState.New, back.SrsState);
        Assert.Equal(0, back.SeenCount);
        Assert.Equal(0, t.Store.CountAnswersToday());
    }

    [Fact]
    public void SmartSessionRespectsTheDailyNewLimit()
    {
        using var t = new TempStore();
        var outcome = t.ImportSample();
        var settings = new AppSettings { NewPerDay = 4 };
        var o = SessionOptions.DefaultFlashcards();
        o.SubjectId = outcome.SubjectId;
        o.Types = ItemTypes.Studyable.ToList();
        var items = SessionBuilder.Build(t.Store, o, settings);
        Assert.Equal(4, items.Count);

        var practice = o.Clone();
        practice.Order = ItemOrder.Shuffle;
        Assert.Equal(14, SessionBuilder.Build(t.Store, practice, settings).Count);
    }

    [Fact]
    public void ReviewRunnerRequeuesAgainAndFinishes()
    {
        using var t = new TempStore();
        var outcome = t.ImportSample();
        var settings = new AppSettings();
        var o = SessionOptions.DefaultFlashcards();
        o.SubjectId = outcome.SubjectId;
        o.ItemIds = t.Store.QueryItems(new ItemQuery { SubjectId = outcome.SubjectId, Types = { ItemTypes.Flashcard } }).Take(2).Select(i => i.Id).ToList();
        var runner = new ReviewRunner(t.Store, settings, o, SessionBuilder.Build(t.Store, o, settings));
        var first = runner.Next()!;
        runner.Answer(Rating.Again, Result.Missed);
        Assert.Equal(2, runner.Remaining);
        int guard = 0;
        while (runner.Next() is { } item && guard++ < 20) runner.Answer(Rating.Easy, Result.GotIt);
        var summary = runner.Finish();
        Assert.Equal(2, summary.Total);
        Assert.Equal(1, summary.Missed);
        Assert.Contains(first.Id, summary.MissedItems.Select(i => i.Id));
    }

    [Fact]
    public void TestRunnerGradesAtTheEnd()
    {
        using var t = new TempStore();
        var outcome = t.ImportSample();
        var settings = new AppSettings();
        var o = SessionOptions.DefaultTest();
        o.SubjectId = outcome.SubjectId;
        o.Feedback = FeedbackTiming.AtEnd;
        o.Types = new() { ItemTypes.TrueFalse };
        var items = SessionBuilder.Build(t.Store, o, settings);
        var runner = new TestRunner(t.Store, settings, o, items);
        runner.Current.Answer.Truth = runner.Current.Item.Data.Truth;
        var summary = runner.Finish();
        Assert.Equal(2, summary.Total);
        Assert.Equal(1, summary.GotIt);
        Assert.Equal(1, summary.Missed);
        Assert.Single(t.Store.RecentSessions(outcome.SubjectId, 5));
    }

    [Fact]
    public void BuiltInTemplatesAreSeededAndResettable()
    {
        using var t = new TempStore();
        t.Store.EnsureBuiltInTemplates();
        var en = t.Store.GetTemplates("en");
        var bg = t.Store.GetTemplates("bg");
        Assert.Equal(PromptDefaults.All("en").Count, en.Count);
        Assert.Equal(PromptDefaults.All("bg").Count, bg.Count);

        var flash = en.First(x => x.Key == PromptDefaults.Flashcards);
        flash.Body += "\nMy extra line";
        t.Store.SaveTemplate(flash);
        Assert.True(t.Store.GetTemplate(flash.Id)!.IsModified);
        t.Store.EnsureBuiltInTemplates();
        Assert.Contains("My extra line", t.Store.GetTemplate(flash.Id)!.Body);
        t.Store.ResetTemplate(flash.Id);
        Assert.False(t.Store.GetTemplate(flash.Id)!.IsModified);
        Assert.DoesNotContain("My extra line", t.Store.GetTemplate(flash.Id)!.Body);
    }

    [Fact]
    public void SearchFindsCyrillicCaseInsensitively()
    {
        using var t = new TempStore();
        var pkg = ImportParser.Parse(SampleContent.Json("bg")).Package!;
        ImportService.Execute(t.Store, new ImportPlan { Package = pkg });
        Assert.NotEmpty(t.Store.Search("СИСТОЛА"));
        Assert.NotEmpty(t.Store.Search("митрална"));
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        using var t = new TempStore();
        var s = t.Store.LoadSettings();
        s.Language = "bg";
        s.Theme = ThemeMode.Sepia;
        s.LineHeight = 1.8f;
        t.Store.SaveSettings(s);
        var again = t.Store.LoadSettings();
        Assert.Equal("bg", again.Language);
        Assert.Equal(ThemeMode.Sepia, again.Theme);
        Assert.Equal(1.8f, again.LineHeight);
    }

    [Fact]
    public void BackupCopiesTheDatabase()
    {
        using var t = new TempStore();
        t.ImportSample();
        var path = Path.Combine(t.Dir, "copy.db");
        t.Store.Db.BackupTo(path);
        using var copy = new StudyStore(path);
        Assert.Single(copy.GetSubjects());
    }
}
