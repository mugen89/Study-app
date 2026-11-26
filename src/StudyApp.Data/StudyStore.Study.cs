using Microsoft.Data.Sqlite;
using StudyApp.Domain;
using StudyApp.Domain.Models;
using StudyApp.Domain.Srs;

namespace StudyApp.Data;

/// <summary>Everything needed to undo one answer (Ctrl+Z in a session).</summary>
public sealed class AnswerUndo
{
    public required Item Before { get; init; }
    public long ReviewId { get; init; }
}

public sealed record DayActivity(DateOnly Day, int Reviews, int Correct);

public sealed record WeekPoint(DateOnly WeekStart, int Answers, double Accuracy);

public sealed record WeakSpot(string Label, int Answers, double Accuracy, long? ChapterId = null);

public sealed class SubjectSummary
{
    public long SubjectId { get; init; }
    public int Chapters { get; set; }
    public int Decks { get; set; }
    public int Items { get; set; }
    public int Notes { get; set; }
    public int DueToday { get; set; }
    public int New { get; set; }
    public int Learning { get; set; }
    public int Mastered { get; set; }
    public int Flagged { get; set; }
    public double Accuracy { get; set; }
    public int Answers { get; set; }
    public double MasteredShare => Items == 0 ? 0 : (double)Mastered / Items;
    public double SeenShare => Items == 0 ? 0 : (double)(Items - New) / Items;
}

public sealed class DeckSummary
{
    public long DeckId { get; init; }
    public int Items { get; set; }
    public int Studyable { get; set; }
    public int Questions { get; set; }
    public int Cards { get; set; }
    public int Notes { get; set; }
    public int NotesRead { get; set; }
    public int DueToday { get; set; }
    public int New { get; set; }
    public int Mastered { get; set; }
    public int Flagged { get; set; }
    public int Seen { get; set; }
    public int Correct { get; set; }
    public double Accuracy => Seen == 0 ? 0 : (double)Correct / Seen;
}

public sealed partial class StudyStore
{
    // ───────────────────────────────────────────────────────────── answers

    /// <summary>
    /// Records one answer: updates the schedule (optional), statistics and the review log in one transaction.
    /// </summary>
    public AnswerUndo RecordAnswer(Item item, Rating rating, Result result, long durationMs, long? sessionId,
        bool updateSchedule, SrsSettings srs)
    {
        var now = Now();
        var before = CloneProgress(item);
        bool wasNew = item.SrsState == SrsState.New && item.SeenCount == 0;

        if (updateSchedule && item.IsStudyable)
            Sm2.Next(item, rating, now, srs).ApplyTo(item);

        item.SeenCount++;
        if (result == Result.GotIt) item.CorrectCount++;
        item.LastResult = result;
        item.LastSeenAt = now;

        long reviewId = 0;
        Db.Tx(() =>
        {
            SaveProgress(item);
            reviewId = Db.Insert("""
                INSERT INTO reviews (item_id, session_id, subject_id, reviewed_at, study_day, rating, result, duration_ms, was_new)
                VALUES (@itemId, @sessionId, @subjectId, @now, @day, @rating, @result, @durationMs, @wasNew)
                """, new
            {
                itemId = item.Id, sessionId, subjectId = item.SubjectId, now, day = StudyDay.Key(StudyDay.OfMs(now)),
                rating = (int)rating, result = (int)result, durationMs = Math.Clamp(durationMs, 0, 3_600_000), wasNew,
            });
            Db.Exec("UPDATE decks SET last_studied_at = @now WHERE id = @deckId", new { now, deckId = item.DeckId });
        });
        return new AnswerUndo { Before = before, ReviewId = reviewId };
    }

    public void UndoAnswer(AnswerUndo undo, Item live)
    {
        CopyProgress(undo.Before, live);
        Db.Tx(() =>
        {
            SaveProgress(live);
            Db.Exec("DELETE FROM reviews WHERE id = @id", new { id = undo.ReviewId });
        });
    }

    private static Item CloneProgress(Item i)
    {
        var c = new Item { Id = i.Id };
        CopyProgress(i, c);
        return c;
    }

    private static void CopyProgress(Item from, Item to)
    {
        to.SrsState = from.SrsState; to.SrsStep = from.SrsStep; to.SrsEase = from.SrsEase; to.SrsInterval = from.SrsInterval;
        to.SrsDue = from.SrsDue; to.SrsReps = from.SrsReps; to.SrsLapses = from.SrsLapses; to.SeenCount = from.SeenCount;
        to.CorrectCount = from.CorrectCount; to.LastResult = from.LastResult; to.LastSeenAt = from.LastSeenAt;
    }

    // ───────────────────────────────────────────────────────────── sessions

    private static SessionRecord ReadSession(SqliteDataReader r) => new()
    {
        Id = r.Long("id"),
        SubjectId = r.LongN("subject_id"),
        Mode = (SessionMode)r.Int("mode"),
        Title = r.Str("title"),
        SettingsJson = r.Str("settings"),
        StartedAt = r.Long("started_at"),
        EndedAt = r.LongN("ended_at"),
        ItemCount = r.Int("item_count"),
        Correct = r.Int("correct"),
        Partial = r.Int("partial"),
        Wrong = r.Int("wrong"),
        Score = r.Dbl("score"),
        DurationMs = r.Long("duration_ms"),
    };

    public long StartSession(SessionRecord s)
    {
        s.StartedAt = Now();
        s.Id = Db.Insert("""
            INSERT INTO sessions (subject_id, mode, title, settings, started_at, item_count)
            VALUES (@SubjectId, @Mode, @Title, @SettingsJson, @StartedAt, @ItemCount)
            """, new { s.SubjectId, s.Mode, s.Title, s.SettingsJson, s.StartedAt, s.ItemCount });
        return s.Id;
    }

    public void FinishSession(SessionRecord s)
    {
        s.EndedAt = Now();
        Db.Exec("""
            UPDATE sessions SET ended_at = @EndedAt, item_count = @ItemCount, correct = @Correct, partial = @Partial,
                   wrong = @Wrong, score = @Score, duration_ms = @DurationMs
            WHERE id = @Id
            """, new { s.Id, s.EndedAt, s.ItemCount, s.Correct, s.Partial, s.Wrong, s.Score, s.DurationMs });
    }

    public List<SessionRecord> RecentSessions(long? subjectId, int limit) =>
        Db.Query($"SELECT * FROM sessions WHERE ended_at IS NOT NULL AND item_count > 0 {(subjectId.HasValue ? "AND subject_id = @subjectId" : "")} ORDER BY started_at DESC LIMIT {limit}",
            ReadSession, new { subjectId = subjectId ?? 0 });

    // ───────────────────────────────────────────────────────────── daily counters

    public int CountAnswersToday(long? subjectId = null) =>
        (int)Db.Scalar<long>($"SELECT COUNT(*) FROM reviews WHERE study_day = @day {(subjectId.HasValue ? "AND subject_id = @subjectId" : "")}",
            new { day = StudyDay.TodayKey, subjectId = subjectId ?? 0 });

    public int CountNewToday(long? subjectId = null) =>
        (int)Db.Scalar<long>($"SELECT COUNT(*) FROM reviews WHERE study_day = @day AND was_new = 1 {(subjectId.HasValue ? "AND subject_id = @subjectId" : "")}",
            new { day = StudyDay.TodayKey, subjectId = subjectId ?? 0 });

    /// <summary>Studyable items due before the end of today's study day (flashcards and scheduled questions).</summary>
    public int CountDueToday(long? subjectId = null)
    {
        var sql = ItemSelect.Replace("SELECT i.*,", "SELECT COUNT(*) AS n, i.id AS dummy_id,")
                  + " AND i.type <> 'note' AND i.suspended = 0 AND i.wrong_in_source = 0 AND i.srs_state <> 0 AND i.srs_due < @end"
                  + (subjectId.HasValue ? " AND d.subject_id = @subjectId" : "");
        return Db.Query(sql, r => r.Int("n"), new { end = StudyDay.EndOfTodayMs(), subjectId = subjectId ?? 0 }).FirstOrDefault();
    }

    // ───────────────────────────────────────────────────────────── statistics

    public List<DayActivity> Activity(int days)
    {
        var from = StudyDay.Key(StudyDay.Today.AddDays(-days + 1));
        return Db.Query("""
            SELECT study_day, COUNT(*) AS n, SUM(CASE WHEN result = 2 THEN 1 ELSE 0 END) AS ok
            FROM reviews WHERE study_day >= @from GROUP BY study_day ORDER BY study_day
            """, r => new DayActivity(StudyDay.Parse(r.Str("study_day")), r.Int("n"), r.Int("ok")), new { from });
    }

    /// <summary>Consecutive study days ending today (or yesterday if nothing was studied yet today).</summary>
    public int Streak()
    {
        var days = Db.Query("SELECT DISTINCT study_day FROM reviews ORDER BY study_day DESC LIMIT 400", r => StudyDay.Parse(r.Str("study_day")));
        if (days.Count == 0) return 0;
        var set = days.ToHashSet();
        var day = StudyDay.Today;
        if (!set.Contains(day)) day = day.AddDays(-1);
        int streak = 0;
        while (set.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }

    /// <summary>Weighted accuracy (partial = ½) over the last <paramref name="days"/> days.</summary>
    public (int Answers, double Accuracy) RecentAccuracy(int days, long? subjectId = null)
    {
        var from = StudyDay.Key(StudyDay.Today.AddDays(-days + 1));
        var row = Db.Query($"""
            SELECT COUNT(*) AS n, SUM(CASE result WHEN 2 THEN 1.0 WHEN 1 THEN 0.5 ELSE 0 END) AS score
            FROM reviews WHERE study_day >= @from {(subjectId.HasValue ? "AND subject_id = @subjectId" : "")}
            """, r => (r.Int("n"), r.Dbl("score")), new { from, subjectId = subjectId ?? 0 }).FirstOrDefault();
        return row.Item1 == 0 ? (0, 0) : (row.Item1, row.Item2 / row.Item1);
    }

    public Dictionary<long, SubjectSummary> SubjectSummaries()
    {
        var map = new Dictionary<long, SubjectSummary>();
        SubjectSummary Get(long id) => map.TryGetValue(id, out var s) ? s : map[id] = new SubjectSummary { SubjectId = id };

        foreach (var (id, n) in Db.Query("SELECT subject_id, COUNT(*) AS n FROM chapters WHERE deleted_at IS NULL GROUP BY subject_id",
                     r => (r.Long("subject_id"), r.Int("n"))))
            Get(id).Chapters = n;

        var end = StudyDay.EndOfTodayMs();
        var rows = Db.Query(ItemSelect.Replace("SELECT i.*,", "SELECT i.type AS t, i.srs_state AS st, i.srs_interval AS iv, i.srs_due AS due, i.flagged AS fl, i.wrong_in_source AS ws, i.suspended AS su, i.seen_count AS seen, i.correct_count AS ok, i.deck_id AS dk,"),
            r => (Subject: r.Long("ctx_subject_id"), Type: r.Str("t"), State: r.Int("st"), Interval: r.Dbl("iv"), Due: r.LongN("due"),
                  Flagged: r.Bool("fl"), Wrong: r.Bool("ws"), Suspended: r.Bool("su"), Seen: r.Int("seen"), Ok: r.Int("ok"), Deck: r.Long("dk")));

        foreach (var g in rows.GroupBy(x => x.Subject))
        {
            var s = Get(g.Key);
            s.Decks = g.Select(x => x.Deck).Distinct().Count();
            foreach (var x in g)
            {
                if (x.Type == ItemTypes.Note) { s.Notes++; continue; }
                s.Items++;
                if (x.Flagged) s.Flagged++;
                if (x.State == 0) s.New++;
                else if (x.State == (int)SrsState.Review && x.Interval >= 21) s.Mastered++;
                else s.Learning++;
                if (x.State != 0 && !x.Suspended && !x.Wrong && x.Due.HasValue && x.Due.Value < end) s.DueToday++;
            }
        }

        foreach (var (id, n, score) in Db.Query("""
                     SELECT subject_id, COUNT(*) AS n, SUM(CASE result WHEN 2 THEN 1.0 WHEN 1 THEN 0.5 ELSE 0 END) AS score
                     FROM reviews GROUP BY subject_id
                     """, r => (r.Long("subject_id"), r.Int("n"), r.Dbl("score"))))
        {
            var s = Get(id);
            s.Answers = n;
            s.Accuracy = n == 0 ? 0 : score / n;
        }
        return map;
    }

    public Dictionary<long, DeckSummary> DeckSummaries(long subjectId)
    {
        var end = StudyDay.EndOfTodayMs();
        var map = new Dictionary<long, DeckSummary>();
        foreach (var item in QueryItems(new ItemQuery { SubjectId = subjectId, WrongInSource = null, IncludeSuspended = true }))
        {
            if (!map.TryGetValue(item.DeckId, out var d)) map[item.DeckId] = d = new DeckSummary { DeckId = item.DeckId };
            d.Items++;
            if (item.Type == ItemTypes.Note)
            {
                d.Notes++;
                if (item.LastResult == Result.GotIt) d.NotesRead++;
                continue;
            }
            d.Studyable++;
            if (item.IsQuestion) d.Questions++; else d.Cards++;
            if (item.Flagged) d.Flagged++;
            if (item.SrsState == SrsState.New) d.New++;
            if (item.IsMastered) d.Mastered++;
            if (item.SrsState != SrsState.New && !item.Suspended && !item.WrongInSource && item.SrsDue < end) d.DueToday++;
            d.Seen += item.SeenCount;
            d.Correct += item.CorrectCount;
        }
        return map;
    }

    /// <summary>Accuracy per week for the last <paramref name="weeks"/> weeks.</summary>
    public List<WeekPoint> AccuracyByWeek(long? subjectId, int weeks)
    {
        var today = StudyDay.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var first = monday.AddDays(-7 * (weeks - 1));
        var rows = Db.Query($"""
            SELECT study_day, COUNT(*) AS n, SUM(CASE result WHEN 2 THEN 1.0 WHEN 1 THEN 0.5 ELSE 0 END) AS score
            FROM reviews WHERE study_day >= @from {(subjectId.HasValue ? "AND subject_id = @subjectId" : "")}
            GROUP BY study_day
            """, r => (Day: StudyDay.Parse(r.Str("study_day")), N: r.Int("n"), Score: r.Dbl("score")),
            new { from = StudyDay.Key(first), subjectId = subjectId ?? 0 });

        var result = new List<WeekPoint>();
        for (int w = 0; w < weeks; w++)
        {
            var start = first.AddDays(7 * w);
            var end = start.AddDays(7);
            var inWeek = rows.Where(r => r.Day >= start && r.Day < end).ToList();
            int n = inWeek.Sum(r => r.N);
            result.Add(new WeekPoint(start, n, n == 0 ? 0 : inWeek.Sum(r => r.Score) / n));
        }
        return result;
    }

    public List<WeakSpot> WeakestChapters(long subjectId, int minAnswers = 3)
    {
        var chapters = GetChapters(subjectId).ToDictionary(c => c.Id);
        return QueryItems(new ItemQuery { SubjectId = subjectId })
            .Where(i => i.IsStudyable && i.SeenCount > 0)
            .GroupBy(i => i.ChapterId ?? 0)
            .Select(g => new WeakSpot(
                g.Key != 0 && chapters.TryGetValue(g.Key, out var c) ? c.DisplayName : Domain.Localization.L.T("No chapter"),
                g.Sum(i => i.SeenCount), (double)g.Sum(i => i.CorrectCount) / Math.Max(1, g.Sum(i => i.SeenCount)), g.Key == 0 ? null : g.Key))
            .Where(w => w.Answers >= minAnswers)
            .OrderBy(w => w.Accuracy)
            .ToList();
    }

    public List<WeakSpot> WeakestTags(long subjectId, int minAnswers = 3) =>
        QueryItems(new ItemQuery { SubjectId = subjectId })
            .Where(i => i.IsStudyable && i.SeenCount > 0)
            .SelectMany(i => i.Tags.Select(t => (Tag: t, i.SeenCount, i.CorrectCount)))
            .GroupBy(x => x.Tag, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new WeakSpot(g.First().Tag, g.Sum(x => x.SeenCount), (double)g.Sum(x => x.CorrectCount) / Math.Max(1, g.Sum(x => x.SeenCount))))
            .Where(w => w.Answers >= minAnswers)
            .OrderBy(w => w.Accuracy)
            .ToList();

    /// <summary>The items missed most (for the "weak spots" prompt and the Review page).</summary>
    public List<Item> WeakItems(long? subjectId, int limit) =>
        QueryItems(new ItemQuery { SubjectId = subjectId })
            .Where(i => i.IsStudyable && i.SeenCount > 0 && (i.LastResult is Result.Missed or Result.Partial || i.CorrectCount < i.SeenCount))
            .OrderByDescending(i => (i.SeenCount - i.CorrectCount) + i.SrsLapses + (i.LastResult == Result.Missed ? 2 : i.LastResult == Result.Partial ? 1 : 0))
            .ThenBy(i => i.Accuracy)
            .Take(limit)
            .ToList();
}
