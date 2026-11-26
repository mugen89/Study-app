using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StudyApp.Domain;
using StudyApp.Domain.Models;

namespace StudyApp.Data;

/// <summary>Filter for <see cref="StudyStore.QueryItems"/>. Empty lists mean "no filter".</summary>
public sealed class ItemQuery
{
    public long? SubjectId { get; set; }
    public List<long> ChapterIds { get; set; } = new();
    public List<long> DeckIds { get; set; } = new();
    public List<long> ItemIds { get; set; } = new();
    public List<string> Types { get; set; } = new();
    public List<int> Difficulties { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public bool OnlyMissed { get; set; }
    public bool OnlyFlagged { get; set; }
    public bool OnlyNew { get; set; }
    /// <summary>Only items scheduled before this time (UTC ms).</summary>
    public long? DueBefore { get; set; }
    public bool? WrongInSource { get; set; } = false;
    public bool IncludeSuspended { get; set; }
    public string? Search { get; set; }
    public int Limit { get; set; }
    public string OrderBy { get; set; } = "d.sort_order, d.id, i.sort_order, i.id";
}

public sealed partial class StudyStore
{
    /// <summary>Items joined with their deck/subject/chapter; only rows that are not in the recycle bin.</summary>
    private const string ItemSelect = """
        SELECT i.*, d.subject_id AS ctx_subject_id, d.chapter_id AS ctx_chapter_id, d.title AS ctx_deck_title,
               s.name AS ctx_subject_name, s.color AS ctx_subject_color, COALESCE(c.title, '') AS ctx_chapter_title
        FROM items i
        JOIN decks d ON d.id = i.deck_id
        JOIN subjects s ON s.id = d.subject_id
        LEFT JOIN chapters c ON c.id = d.chapter_id
        WHERE i.deleted_at IS NULL AND d.deleted_at IS NULL AND s.deleted_at IS NULL AND (c.id IS NULL OR c.deleted_at IS NULL)
        """;

    private static Item ReadItem(SqliteDataReader r)
    {
        var item = new Item
        {
            Id = r.Long("id"),
            DeckId = r.Long("deck_id"),
            Type = r.Str("type"),
            Prompt = r.Str("prompt"),
            Answer = r.Str("answer"),
            Data = ParseData(r.Str("data")),
            Explanation = r.Str("explanation"),
            Page = r.Str("page"),
            Source = r.Str("source"),
            Tags = TextUtil.SplitTagsFromDb(r.Str("tags")),
            Difficulty = r.Int("difficulty"),
            Fingerprint = r.Str("fingerprint"),
            Flagged = r.Bool("flagged"),
            WrongInSource = r.Bool("wrong_in_source"),
            PersonalNote = r.Str("personal_note"),
            Suspended = r.Bool("suspended"),
            SrsState = (SrsState)r.Int("srs_state"),
            SrsStep = r.Int("srs_step"),
            SrsEase = r.Dbl("srs_ease"),
            SrsInterval = r.Dbl("srs_interval"),
            SrsDue = r.LongN("srs_due"),
            SrsReps = r.Int("srs_reps"),
            SrsLapses = r.Int("srs_lapses"),
            SeenCount = r.Int("seen_count"),
            CorrectCount = r.Int("correct_count"),
            LastResult = (Result)r.Int("last_result"),
            LastSeenAt = r.LongN("last_seen_at"),
            SortOrder = r.Int("sort_order"),
            CreatedAt = r.Long("created_at"),
            UpdatedAt = r.Long("updated_at"),
            DeletedAt = r.LongN("deleted_at"),
        };
        return item;
    }

    private static Item ReadItemWithContext(SqliteDataReader r)
    {
        var item = ReadItem(r);
        item.SubjectId = r.Long("ctx_subject_id");
        item.ChapterId = r.LongN("ctx_chapter_id");
        item.DeckTitle = r.Str("ctx_deck_title");
        item.SubjectName = r.Str("ctx_subject_name");
        item.SubjectColor = r.Str("ctx_subject_color");
        item.ChapterTitle = r.Str("ctx_chapter_title");
        return item;
    }

    private static ItemData ParseData(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ItemData();
        try { return JsonSerializer.Deserialize<ItemData>(json, DataJson) ?? new ItemData(); }
        catch { return new ItemData(); }
    }

    private static string DataToJson(ItemData data) => JsonSerializer.Serialize(data, DataJson);

    // ───────────────────────────────────────────────────────────── reads

    public List<Item> GetItems(long deckId) =>
        Db.Query(ItemSelect + " AND i.deck_id = @deckId ORDER BY i.sort_order, i.id", ReadItemWithContext, new { deckId });

    public Item? GetItem(long id) =>
        Db.First(ItemSelect + " AND i.id = @id", ReadItemWithContext, new { id });

    public List<Item> GetItemsByIds(IEnumerable<long> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        var args = new Dictionary<string, object?>();
        var inList = InList("id", list.Cast<object>(), args);
        var items = Db.Query(ItemSelect + $" AND i.id IN ({inList})", ReadItemWithContext, args);
        var order = list.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i);
        return items.OrderBy(i => order.GetValueOrDefault(i.Id)).ToList();
    }

    public List<Item> QueryItems(ItemQuery q)
    {
        var sql = new StringBuilder(ItemSelect);
        var args = new Dictionary<string, object?>();

        if (q.ItemIds.Count > 0)
            sql.Append($" AND i.id IN ({InList("iid", q.ItemIds.Cast<object>(), args)})");
        if (q.SubjectId.HasValue)
        {
            sql.Append(" AND d.subject_id = @subjectId");
            args["subjectId"] = q.SubjectId.Value;
        }
        if (q.ChapterIds.Count > 0)
            sql.Append($" AND d.chapter_id IN ({InList("ch", q.ChapterIds.Cast<object>(), args)})");
        if (q.DeckIds.Count > 0)
            sql.Append($" AND i.deck_id IN ({InList("dk", q.DeckIds.Cast<object>(), args)})");
        if (q.Types.Count > 0)
            sql.Append($" AND i.type IN ({InList("ty", q.Types.Cast<object>(), args)})");
        if (q.Difficulties.Count > 0)
            sql.Append($" AND i.difficulty IN ({InList("df", q.Difficulties.Cast<object>(), args)})");
        if (q.Tags.Count > 0)
        {
            var parts = new List<string>();
            for (int t = 0; t < q.Tags.Count; t++)
            {
                parts.Add($"ulower(i.tags) LIKE @tag{t}");
                args[$"tag{t}"] = "%," + q.Tags[t].Trim().ToLowerInvariant() + ",%";
            }
            sql.Append(" AND (").Append(string.Join(" OR ", parts)).Append(')');
        }
        if (q.OnlyMissed) sql.Append(" AND i.last_result IN (0, 1)");
        if (q.OnlyFlagged) sql.Append(" AND i.flagged = 1");
        if (q.OnlyNew) sql.Append(" AND i.seen_count = 0");
        if (q.DueBefore.HasValue)
        {
            sql.Append(" AND i.srs_state <> 0 AND i.srs_due IS NOT NULL AND i.srs_due <= @dueBefore");
            args["dueBefore"] = q.DueBefore.Value;
        }
        if (q.WrongInSource.HasValue) sql.Append(q.WrongInSource.Value ? " AND i.wrong_in_source = 1" : " AND i.wrong_in_source = 0");
        if (!q.IncludeSuspended) sql.Append(" AND i.suspended = 0");
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            sql.Append(" AND (ulower(i.prompt) LIKE @q OR ulower(i.answer) LIKE @q OR ulower(i.explanation) LIKE @q OR ulower(i.data) LIKE @q OR ulower(i.tags) LIKE @q OR ulower(i.page) LIKE @q OR ulower(i.personal_note) LIKE @q)");
            args["q"] = "%" + q.Search.Trim().ToLowerInvariant() + "%";
        }

        sql.Append(" ORDER BY ").Append(q.OrderBy);
        if (q.Limit > 0) sql.Append(" LIMIT ").Append(q.Limit);
        return Db.Query(sql.ToString(), ReadItemWithContext, args);
    }

    private static string InList(string prefix, IEnumerable<object> values, Dictionary<string, object?> args)
    {
        var names = new List<string>();
        int i = 0;
        foreach (var v in values)
        {
            var name = $"{prefix}{i++}";
            args[name] = v;
            names.Add("@" + name);
        }
        return names.Count == 0 ? "NULL" : string.Join(", ", names);
    }

    /// <summary>All distinct tags used in a subject (for the session filter).</summary>
    public List<string> GetTags(long? subjectId)
    {
        var rows = Db.Query(ItemSelect.Replace("SELECT i.*,", "SELECT i.tags AS tags_only, i.*,") +
                            (subjectId.HasValue ? " AND d.subject_id = @subjectId" : "") + " AND i.tags <> ''",
            r => r.Str("tags_only"), new { subjectId = subjectId ?? 0 });
        return rows.SelectMany(TextUtil.SplitTagsFromDb)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>fingerprint → item id, for duplicate detection within a subject.</summary>
    public Dictionary<string, long> FingerprintsInSubject(long subjectId)
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (fp, id) in Db.Query(ItemSelect + " AND d.subject_id = @subjectId", r => (r.Str("fingerprint"), r.Long("id")), new { subjectId }))
            map.TryAdd(fp, id);
        return map;
    }

    // ───────────────────────────────────────────────────────────── writes

    public long InsertItem(Item item)
    {
        var now = Now();
        item.CreatedAt = item.UpdatedAt = now;
        if (string.IsNullOrEmpty(item.Fingerprint)) item.Fingerprint = TextUtil.Fingerprint(item.Type, item.Prompt);
        item.Id = Db.Insert("""
            INSERT INTO items (deck_id, type, prompt, answer, data, explanation, page, source, tags, difficulty, fingerprint,
                               flagged, wrong_in_source, personal_note, sort_order, created_at, updated_at)
            VALUES (@DeckId, @Type, @Prompt, @Answer, @Data, @Explanation, @Page, @Source, @Tags, @Difficulty, @Fingerprint,
                    @Flagged, @WrongInSource, @PersonalNote, @SortOrder, @CreatedAt, @UpdatedAt)
            """, new
        {
            item.DeckId, item.Type, item.Prompt, item.Answer, Data = DataToJson(item.Data), item.Explanation, item.Page, item.Source,
            Tags = TextUtil.JoinTagsForDb(item.Tags), item.Difficulty, item.Fingerprint, item.Flagged, item.WrongInSource,
            item.PersonalNote, item.SortOrder, item.CreatedAt, item.UpdatedAt,
        });
        return item.Id;
    }

    /// <summary>Updates the content of an item; study progress is kept.</summary>
    public void UpdateItemContent(Item item)
    {
        item.UpdatedAt = Now();
        item.Fingerprint = TextUtil.Fingerprint(item.Type, item.Prompt);
        Db.Exec("""
            UPDATE items SET type = @Type, prompt = @Prompt, answer = @Answer, data = @Data, explanation = @Explanation, page = @Page,
                   source = @Source, tags = @Tags, difficulty = @Difficulty, fingerprint = @Fingerprint, personal_note = @PersonalNote,
                   flagged = @Flagged, wrong_in_source = @WrongInSource, deck_id = @DeckId, updated_at = @UpdatedAt
            WHERE id = @Id
            """, new
        {
            item.Id, item.Type, item.Prompt, item.Answer, Data = DataToJson(item.Data), item.Explanation, item.Page, item.Source,
            Tags = TextUtil.JoinTagsForDb(item.Tags), item.Difficulty, item.Fingerprint, item.PersonalNote, item.Flagged,
            item.WrongInSource, item.DeckId, item.UpdatedAt,
        });
    }

    public void SetFlagged(long id, bool flagged) =>
        Db.Exec("UPDATE items SET flagged = @flagged, updated_at = @now WHERE id = @id", new { id, flagged, now = Now() });

    public void SetWrongInSource(long id, bool wrong) =>
        Db.Exec("UPDATE items SET wrong_in_source = @wrong, updated_at = @now WHERE id = @id", new { id, wrong, now = Now() });

    public void SetPersonalNote(long id, string note) =>
        Db.Exec("UPDATE items SET personal_note = @note, updated_at = @now WHERE id = @id", new { id, note = note.Trim(), now = Now() });

    public void SetSuspended(long id, bool suspended) =>
        Db.Exec("UPDATE items SET suspended = @suspended, updated_at = @now WHERE id = @id", new { id, suspended, now = Now() });

    public void DeleteItem(long id) =>
        Db.Exec("UPDATE items SET deleted_at = @now WHERE id = @id", new { id, now = Now() });

    public void RestoreItem(long id) =>
        Db.Exec("UPDATE items SET deleted_at = NULL WHERE id = @id", new { id });

    public void MarkNotesRead(long id, bool read) =>
        Db.Exec("UPDATE items SET last_result = @r, last_seen_at = @now, seen_count = seen_count + @inc WHERE id = @id",
            new { id, r = read ? (int)Result.GotIt : (int)Result.None, now = Now(), inc = read ? 1 : 0 });

    /// <summary>Saves the scheduling and statistics fields after an answer.</summary>
    public void SaveProgress(Item item) =>
        Db.Exec("""
            UPDATE items SET srs_state = @SrsState, srs_step = @SrsStep, srs_ease = @SrsEase, srs_interval = @SrsInterval,
                   srs_due = @SrsDue, srs_reps = @SrsReps, srs_lapses = @SrsLapses, seen_count = @SeenCount,
                   correct_count = @CorrectCount, last_result = @LastResult, last_seen_at = @LastSeenAt
            WHERE id = @Id
            """, new
        {
            item.Id, item.SrsState, item.SrsStep, item.SrsEase, item.SrsInterval, item.SrsDue, item.SrsReps, item.SrsLapses,
            item.SeenCount, item.CorrectCount, item.LastResult, item.LastSeenAt,
        });

    /// <summary>Forget all study progress (optionally for one subject). Content is kept.</summary>
    public void ResetProgress(long? subjectId)
    {
        Db.Tx(() =>
        {
            var filter = subjectId.HasValue ? "WHERE deck_id IN (SELECT id FROM decks WHERE subject_id = @subjectId)" : "";
            Db.Exec($"""
                UPDATE items SET srs_state = 0, srs_step = 0, srs_ease = 2.5, srs_interval = 0, srs_due = NULL, srs_reps = 0,
                       srs_lapses = 0, seen_count = 0, correct_count = 0, last_result = -1, last_seen_at = NULL {filter}
                """, new { subjectId = subjectId ?? 0 });
            Db.Exec(subjectId.HasValue ? "DELETE FROM reviews WHERE subject_id = @subjectId" : "DELETE FROM reviews", new { subjectId = subjectId ?? 0 });
            Db.Exec(subjectId.HasValue ? "DELETE FROM sessions WHERE subject_id = @subjectId" : "DELETE FROM sessions", new { subjectId = subjectId ?? 0 });
        });
    }

    public int NextItemSortOrder(long deckId) =>
        (int)Db.Scalar<long>("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM items WHERE deck_id = @deckId", new { deckId });
}
