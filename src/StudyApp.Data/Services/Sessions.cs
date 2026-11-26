using System.Diagnostics;
using StudyApp.Domain;
using StudyApp.Domain.Grading;
using StudyApp.Domain.Models;
using StudyApp.Domain.Sessions;
using StudyApp.Domain.Settings;
using StudyApp.Domain.Srs;

namespace StudyApp.Data.Services;

/// <summary>Picks the items for a session from the options (scope, filters, order, length).</summary>
public static class SessionBuilder
{
    private static readonly long LearnAheadMs = (long)TimeSpan.FromMinutes(20).TotalMilliseconds;

    public static List<Item> Build(StudyStore store, SessionOptions o, AppSettings settings, Random? rng = null)
    {
        rng ??= Random.Shared;
        var now = StudyStore.Now();

        List<string> types = o.Types.Where(ItemTypes.IsStudyable).ToList();
        if (types.Count == 0)
            types = o.Mode == SessionMode.Test ? ItemTypes.Questions.ToList() : ItemTypes.Studyable.ToList();

        ItemQuery query = o.ItemIds.Count > 0
            ? new ItemQuery { ItemIds = o.ItemIds.ToList(), Types = ItemTypes.Studyable.ToList(), WrongInSource = null, IncludeSuspended = true }
            : new ItemQuery
            {
                SubjectId = o.SubjectId,
                ChapterIds = o.ChapterIds.ToList(),
                DeckIds = o.DeckIds.ToList(),
                Types = types,
                Difficulties = o.Difficulties.ToList(),
                Tags = o.Tags.ToList(),
                OnlyMissed = o.OnlyMissed,
                OnlyFlagged = o.OnlyFlagged,
                OnlyNew = o.OnlyNew,
                DueBefore = o.OnlyDue ? now + LearnAheadMs : null,
            };

        var items = store.QueryItems(query);
        if (o.ItemIds.Count > 0)
        {
            var order = o.ItemIds.Select((id, i) => (id, i)).GroupBy(t => t.id).ToDictionary(g => g.Key, g => g.First().i);
            items = items.OrderBy(i => order.GetValueOrDefault(i.Id)).ToList();
        }

        List<Item> result;
        bool smart = o.Mode == SessionMode.Flashcards && o.Order == ItemOrder.Default && !o.HasFilters && o.ItemIds.Count == 0;
        if (smart)
        {
            var due = items.Where(i => i.SrsState != SrsState.New && i.SrsDue.HasValue && i.SrsDue.Value <= now + LearnAheadMs)
                .OrderBy(i => i.SrsDue).ToList();
            int reviewsLeft = Math.Max(0, settings.MaxReviewsPerDay - store.CountAnswersToday(o.SubjectId));
            if (due.Count > reviewsLeft) due = due.Take(Math.Max(reviewsLeft, 10)).ToList();

            int newLeft = Math.Max(0, settings.NewPerDay - store.CountNewToday(o.SubjectId));
            var fresh = items.Where(i => i.SrsState == SrsState.New).Take(newLeft).ToList();
            result = Interleave(due, fresh);
        }
        else
        {
            result = o.Order switch
            {
                ItemOrder.Shuffle => items.OrderBy(_ => rng.Next()).ToList(),
                ItemOrder.Difficulty => items.Select((i, idx) => (i, idx)).OrderBy(t => t.i.Difficulty == 0 ? 3 : t.i.Difficulty).ThenBy(t => t.idx).Select(t => t.i).ToList(),
                ItemOrder.PageOrder => items.Select((i, idx) => (i, idx)).OrderBy(t => TextUtil.FirstPageNumber(t.i.Page) ?? int.MaxValue).ThenBy(t => t.idx).Select(t => t.i).ToList(),
                _ => items,
            };
        }

        if (o.MaxItems > 0 && result.Count > o.MaxItems) result = result.Take(o.MaxItems).ToList();
        return result;
    }

    /// <summary>Spreads new cards evenly between due reviews.</summary>
    public static List<Item> Interleave(List<Item> due, List<Item> fresh)
    {
        if (due.Count == 0) return fresh.ToList();
        if (fresh.Count == 0) return due.ToList();
        var result = new List<Item>(due.Count + fresh.Count);
        double step = (double)due.Count / (fresh.Count + 1);
        int f = 0;
        double next = step;
        for (int d = 0; d < due.Count; d++)
        {
            result.Add(due[d]);
            while (f < fresh.Count && d + 1 >= next)
            {
                result.Add(fresh[f++]);
                next += step;
            }
        }
        while (f < fresh.Count) result.Add(fresh[f++]);
        return result;
    }
}

/// <summary>One answered item in a session.</summary>
public sealed class SessionEntry
{
    public required Item Item { get; init; }
    public Result Result { get; set; }
    public Rating Rating { get; set; }
    public string GivenText { get; set; } = "";
    public long DurationMs { get; set; }
}

public sealed class SessionSummary
{
    public SessionMode Mode { get; init; }
    public string Title { get; init; } = "";
    public long? SubjectId { get; init; }
    public SessionOptions Options { get; init; } = new();
    public List<SessionEntry> Entries { get; init; } = new();
    public TimeSpan Duration { get; init; }
    public bool TimedOut { get; init; }

    public int Total => Entries.Count;
    public int GotIt => Entries.Count(e => e.Result == Result.GotIt);
    public int Partial => Entries.Count(e => e.Result == Result.Partial);
    public int Missed => Entries.Count(e => e.Result is Result.Missed or Result.None);
    public double Score => Total == 0 ? 0 : (GotIt + Partial * 0.5) / Total;

    public List<SessionEntry> MissedEntries => Entries.Where(e => e.Result != Result.GotIt).ToList();
    public List<Item> MissedItems => MissedEntries.Select(e => e.Item).DistinctBy(i => i.Id).ToList();

    /// <summary>Distinct page references of everything missed, in page order.</summary>
    public List<string> PagesToReread =>
        MissedItems.Select(i => i.Page.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => TextUtil.FirstPageNumber(p) ?? int.MaxValue).ToList();
}

/// <summary>
/// Flashcard / review session: one item at a time, rated Again–Easy, with spaced repetition.
/// Items that come due again within 20 minutes (Again, learning steps) are shown again later in the session.
/// </summary>
public sealed class ReviewRunner
{
    private readonly StudyStore _store;
    private readonly AppSettings _settings;
    private readonly SrsSettings _srs;
    private readonly List<Item> _queue;
    private readonly Stack<(SessionEntry Entry, AnswerUndo Undo, bool Requeued, Item Item)> _undo = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<long, SessionEntry> _first = new();
    private readonly SessionRecord _record;
    private long _shownAt;

    public SessionOptions Options { get; }
    public Item? Current { get; private set; }
    public int InitialCount { get; }
    public int Answered => _undo.Count;
    public int Remaining => _queue.Count + (Current != null ? 1 : 0);
    public List<SessionEntry> Entries { get; } = new();
    public TimeSpan Elapsed => _clock.Elapsed;
    public bool CanUndo => _undo.Count > 0;
    public bool TimeUp => Options.TimeLimitMinutes > 0 && _clock.Elapsed.TotalMinutes >= Options.TimeLimitMinutes;
    public SrsSettings Srs => _srs;

    public ReviewRunner(StudyStore store, AppSettings settings, SessionOptions options, List<Item> items)
    {
        _store = store;
        _settings = settings;
        _srs = settings.ToSrs();
        Options = options;
        _queue = items.ToList();
        InitialCount = items.Count;
        _record = new SessionRecord
        {
            SubjectId = options.SubjectId ?? items.Select(i => (long?)i.SubjectId).FirstOrDefault(),
            Mode = SessionMode.Flashcards,
            Title = options.Title,
            SettingsJson = options.ToJson(),
            ItemCount = items.Count,
        };
        store.StartSession(_record);
    }

    /// <summary>Moves to the next item; null when the session is complete.</summary>
    public Item? Next()
    {
        if (TimeUp || _queue.Count == 0)
        {
            Current = null;
            return null;
        }
        Current = _queue[0];
        _queue.RemoveAt(0);
        _shownAt = _clock.ElapsedMilliseconds;
        return Current;
    }

    public void Answer(Rating rating, Result result, string givenText = "")
    {
        var item = Current ?? throw new InvalidOperationException("No current item.");
        var duration = _clock.ElapsedMilliseconds - _shownAt;
        var undo = _store.RecordAnswer(item, rating, result, duration, _record.Id, updateSchedule: true, _srs);
        var entry = new SessionEntry { Item = item, Result = result, Rating = rating, GivenText = givenText, DurationMs = duration };
        Entries.Add(entry);
        _first.TryAdd(item.Id, entry);

        bool requeue = item.SrsDue.HasValue && item.SrsDue.Value <= StudyStore.Now() + (long)TimeSpan.FromMinutes(20).TotalMilliseconds;
        if (requeue)
        {
            int pos = rating == Rating.Again ? Math.Min(_queue.Count, 3) : _queue.Count;
            _queue.Insert(pos, item);
        }
        _undo.Push((entry, undo, requeue, item));
        Current = null;
    }

    /// <summary>Takes back the last answer and shows that item again.</summary>
    public Item? Undo()
    {
        if (_undo.Count == 0) return null;
        var (entry, undo, requeued, item) = _undo.Pop();
        _store.UndoAnswer(undo, item);
        Entries.Remove(entry);
        if (_first.TryGetValue(item.Id, out var first) && first == entry) _first.Remove(item.Id);
        if (requeued)
        {
            int idx = _queue.FindIndex(i => ReferenceEquals(i, item));
            if (idx >= 0) _queue.RemoveAt(idx);
        }
        if (Current != null) _queue.Insert(0, Current);
        Current = item;
        _shownAt = _clock.ElapsedMilliseconds;
        return item;
    }

    public SessionSummary Finish()
    {
        _clock.Stop();
        var firsts = _first.Values.ToList();
        _record.ItemCount = firsts.Count;
        _record.Correct = firsts.Count(e => e.Result == Result.GotIt);
        _record.Partial = firsts.Count(e => e.Result == Result.Partial);
        _record.Wrong = firsts.Count(e => e.Result == Result.Missed);
        _record.Score = firsts.Count == 0 ? 0 : (_record.Correct + _record.Partial * 0.5) / firsts.Count;
        _record.DurationMs = (long)_clock.Elapsed.TotalMilliseconds;
        _store.FinishSession(_record);
        return new SessionSummary
        {
            Mode = SessionMode.Flashcards,
            Title = Options.Title,
            SubjectId = _record.SubjectId,
            Options = Options,
            Entries = firsts,
            Duration = _clock.Elapsed,
            TimedOut = TimeUp,
        };
    }
}

/// <summary>Per-question state in a test.</summary>
public sealed class TestQuestion
{
    public required Item Item { get; init; }
    /// <summary>Display order of options (shuffled), mapping display index → original index.</summary>
    public int[] OptionOrder { get; init; } = Array.Empty<int>();
    public Answer Answer { get; } = new();
    public Result Result { get; set; } = Result.None;
    public bool Submitted { get; set; }
    public bool Flagged { get; set; }
    public long DurationMs { get; set; }
}

/// <summary>
/// Test session: fixed list of questions with navigation, optional time limit, and feedback either
/// immediately after each answer or at the end.
/// </summary>
public sealed class TestRunner
{
    private readonly StudyStore _store;
    private readonly AppSettings _settings;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SessionRecord _record;
    private long _questionShownAt;
    private bool _finished;

    public SessionOptions Options { get; }
    public List<TestQuestion> Questions { get; }
    public int Index { get; private set; }
    public TestQuestion Current => Questions[Index];
    public TimeSpan Elapsed => _clock.Elapsed;
    public bool Immediate => Options.Feedback == FeedbackTiming.Immediate;
    public TimeSpan? Remaining => Options.TimeLimitMinutes > 0 ? TimeSpan.FromMinutes(Options.TimeLimitMinutes) - _clock.Elapsed : null;
    public bool TimeUp => Remaining is { } r && r <= TimeSpan.Zero;
    public int AnsweredCount => Questions.Count(q => q.Submitted || !q.Answer.IsEmpty);

    public TestRunner(StudyStore store, AppSettings settings, SessionOptions options, List<Item> items, Random? rng = null)
    {
        rng ??= Random.Shared;
        _store = store;
        _settings = settings;
        Options = options;
        Questions = items.Select(i =>
        {
            int n = i.Data.Options?.Count ?? 0;
            var order = Enumerable.Range(0, n).ToArray();
            if (options.ShuffleChoices && ItemTypes.IsChoice(i.Type)) order = order.OrderBy(_ => rng.Next()).ToArray();
            return new TestQuestion { Item = i, OptionOrder = order };
        }).ToList();
        _record = new SessionRecord
        {
            SubjectId = options.SubjectId ?? items.Select(i => (long?)i.SubjectId).FirstOrDefault(),
            Mode = SessionMode.Test,
            Title = options.Title,
            SettingsJson = options.ToJson(),
            ItemCount = items.Count,
        };
        store.StartSession(_record);
    }

    public void GoTo(int index)
    {
        if (index < 0 || index >= Questions.Count) return;
        AccumulateTime();
        Index = index;
    }

    private void AccumulateTime()
    {
        var now = _clock.ElapsedMilliseconds;
        Current.DurationMs += now - _questionShownAt;
        _questionShownAt = now;
    }

    /// <summary>Grades the current question (immediate feedback). Self-graded items pass their verdict.</summary>
    public Result Submit(Result? selfGrade = null)
    {
        var q = Current;
        if (q.Submitted) return q.Result;
        AccumulateTime();
        if (selfGrade.HasValue) q.Answer.SelfGrade = selfGrade;
        q.Result = Grader.Grade(q.Item, q.Answer, _settings.ShortAnswerStrictness);
        q.Submitted = true;
        Record(q);
        return q.Result;
    }

    /// <summary>Suggested verdict for a typed answer (shown before the learner self-grades).</summary>
    public ShortCheck CheckTyped(TestQuestion q)
    {
        var accepted = q.Item.Type == ItemTypes.Flashcard ? new[] { q.Item.Answer } : Grader.AcceptedAnswers(q.Item);
        return Grader.CheckShort(q.Answer.Text, accepted, _settings.ShortAnswerStrictness);
    }

    private void Record(TestQuestion q)
    {
        var rating = Sm2.RatingFor(q.Result);
        _store.RecordAnswer(q.Item, rating, q.Result, q.DurationMs, _record.Id, _settings.TestsUpdateSchedule, _settings.ToSrs());
    }

    /// <summary>Questions that still need a self-grade before results can be shown (feedback at the end).</summary>
    public List<TestQuestion> NeedSelfGrade() =>
        Questions.Where(q => !q.Submitted && (q.Item.Type is ItemTypes.Short or ItemTypes.Flashcard)).ToList();

    public SessionSummary Finish(bool timedOut = false)
    {
        if (!_finished)
        {
            _finished = true;
            AccumulateTime();
            foreach (var q in Questions.Where(q => !q.Submitted))
            {
                q.Result = q.Answer.IsEmpty && q.Answer.SelfGrade == null ? Result.Missed : Grader.Grade(q.Item, q.Answer, _settings.ShortAnswerStrictness);
                q.Submitted = true;
                Record(q);
            }
            _clock.Stop();
            _record.ItemCount = Questions.Count;
            _record.Correct = Questions.Count(q => q.Result == Result.GotIt);
            _record.Partial = Questions.Count(q => q.Result == Result.Partial);
            _record.Wrong = Questions.Count(q => q.Result is Result.Missed or Result.None);
            _record.Score = Questions.Count == 0 ? 0 : (_record.Correct + _record.Partial * 0.5) / Questions.Count;
            _record.DurationMs = (long)_clock.Elapsed.TotalMilliseconds;
            _store.FinishSession(_record);
        }

        return new SessionSummary
        {
            Mode = SessionMode.Test,
            Title = Options.Title,
            SubjectId = _record.SubjectId,
            Options = Options,
            Duration = _clock.Elapsed,
            TimedOut = timedOut,
            Entries = Questions.Select(q => new SessionEntry
            {
                Item = q.Item,
                Result = q.Result,
                Rating = Sm2.RatingFor(q.Result),
                GivenText = DescribeAnswer(q),
                DurationMs = q.DurationMs,
            }).ToList(),
        };
    }

    public static string DescribeAnswer(TestQuestion q)
    {
        var item = q.Item;
        switch (item.Type)
        {
            case ItemTypes.Single:
            case ItemTypes.Multiple:
                var opts = item.Data.Options ?? new();
                // Option letters are shuffled per test, so the summary shows the option text only.
                return string.Join("; ", q.Answer.Selected.OrderBy(i => i).Where(i => i >= 0 && i < opts.Count).Select(i => opts[i].Text));
            case ItemTypes.TrueFalse:
                return q.Answer.Truth == null ? "" : q.Answer.Truth.Value ? Domain.Localization.L.T("True") : Domain.Localization.L.T("False");
            default:
                return q.Answer.Text;
        }
    }
}
