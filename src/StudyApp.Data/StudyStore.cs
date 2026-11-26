using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using StudyApp.Domain;
using StudyApp.Domain.Models;

namespace StudyApp.Data;

/// <summary>
/// All database access. Split into partial files by area (subjects/chapters/decks, items, study, templates…).
/// Deletes are "soft" (deleted_at) so they can be undone from the recycle bin.
/// </summary>
public sealed partial class StudyStore : IDisposable
{
    internal static readonly JsonSerializerOptions DataJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public Db Db { get; }

    public StudyStore(string path)
    {
        Db = new Db(path);
    }

    public void Dispose() => Db.Dispose();

    public static long Now() => Time.NowMs();

    // ───────────────────────────────────────────────────────────── subjects

    private static Subject ReadSubject(SqliteDataReader r) => new()
    {
        Id = r.Long("id"),
        Name = r.Str("name"),
        Description = r.Str("description"),
        Source = r.Str("source"),
        Color = r.Str("color"),
        Icon = r.Str("icon"),
        Archived = r.Bool("archived"),
        SortOrder = r.Int("sort_order"),
        CreatedAt = r.Long("created_at"),
        UpdatedAt = r.Long("updated_at"),
        LastOpenedAt = r.LongN("last_opened_at"),
        DeletedAt = r.LongN("deleted_at"),
    };

    public List<Subject> GetSubjects(bool includeArchived = true) =>
        Db.Query($"SELECT * FROM subjects WHERE deleted_at IS NULL {(includeArchived ? "" : "AND archived = 0")} " +
                 "ORDER BY archived, sort_order, COALESCE(last_opened_at, created_at) DESC", ReadSubject);

    public Subject? GetSubject(long id) =>
        Db.First("SELECT * FROM subjects WHERE id = @id AND deleted_at IS NULL", ReadSubject, new { id });

    public long CreateSubject(Subject s)
    {
        var now = Now();
        s.CreatedAt = s.UpdatedAt = now;
        s.Id = Db.Insert("""
            INSERT INTO subjects (name, description, source, color, icon, archived, sort_order, created_at, updated_at, last_opened_at)
            VALUES (@Name, @Description, @Source, @Color, @Icon, @Archived, @SortOrder, @CreatedAt, @UpdatedAt, @CreatedAt)
            """, new { s.Name, s.Description, s.Source, s.Color, s.Icon, s.Archived, s.SortOrder, s.CreatedAt, s.UpdatedAt });
        return s.Id;
    }

    public void UpdateSubject(Subject s)
    {
        s.UpdatedAt = Now();
        Db.Exec("""
            UPDATE subjects SET name = @Name, description = @Description, source = @Source, color = @Color, icon = @Icon,
                   archived = @Archived, sort_order = @SortOrder, updated_at = @UpdatedAt
            WHERE id = @Id
            """, new { s.Id, s.Name, s.Description, s.Source, s.Color, s.Icon, s.Archived, s.SortOrder, s.UpdatedAt });
    }

    public void SetSubjectArchived(long id, bool archived) =>
        Db.Exec("UPDATE subjects SET archived = @archived, updated_at = @now WHERE id = @id", new { id, archived, now = Now() });

    public void TouchSubject(long id) =>
        Db.Exec("UPDATE subjects SET last_opened_at = @now WHERE id = @id", new { id, now = Now() });

    public void DeleteSubject(long id) =>
        Db.Exec("UPDATE subjects SET deleted_at = @now WHERE id = @id", new { id, now = Now() });

    public void RestoreSubject(long id) =>
        Db.Exec("UPDATE subjects SET deleted_at = NULL WHERE id = @id", new { id });

    // ───────────────────────────────────────────────────────────── chapters

    private static Chapter ReadChapter(SqliteDataReader r) => new()
    {
        Id = r.Long("id"),
        SubjectId = r.Long("subject_id"),
        Number = r.Str("number"),
        Title = r.Str("title"),
        Pages = r.Str("pages"),
        SortOrder = r.Int("sort_order"),
        CreatedAt = r.Long("created_at"),
        DeletedAt = r.LongN("deleted_at"),
    };

    public List<Chapter> GetChapters(long subjectId) =>
        Db.Query("SELECT * FROM chapters WHERE subject_id = @subjectId AND deleted_at IS NULL ORDER BY sort_order, id",
            ReadChapter, new { subjectId });

    public Chapter? GetChapter(long id) =>
        Db.First("SELECT * FROM chapters WHERE id = @id AND deleted_at IS NULL", ReadChapter, new { id });

    public long CreateChapter(Chapter c)
    {
        c.CreatedAt = Now();
        c.SortOrder = (int)Db.Scalar<long>("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM chapters WHERE subject_id = @SubjectId", new { c.SubjectId });
        c.Id = Db.Insert("""
            INSERT INTO chapters (subject_id, number, title, pages, sort_order, created_at)
            VALUES (@SubjectId, @Number, @Title, @Pages, @SortOrder, @CreatedAt)
            """, new { c.SubjectId, c.Number, c.Title, c.Pages, c.SortOrder, c.CreatedAt });
        return c.Id;
    }

    public void UpdateChapter(Chapter c) =>
        Db.Exec("UPDATE chapters SET number = @Number, title = @Title, pages = @Pages WHERE id = @Id",
            new { c.Id, c.Number, c.Title, c.Pages });

    /// <summary>Soft-deletes the chapter together with its decks (same timestamp, so restore brings both back).</summary>
    public void DeleteChapter(long id)
    {
        var now = Now();
        Db.Tx(() =>
        {
            Db.Exec("UPDATE chapters SET deleted_at = @now WHERE id = @id", new { id, now });
            Db.Exec("UPDATE decks SET deleted_at = @now WHERE chapter_id = @id AND deleted_at IS NULL", new { id, now });
        });
    }

    public void RestoreChapter(long id)
    {
        Db.Tx(() =>
        {
            var ts = Db.Scalar<long?>("SELECT deleted_at FROM chapters WHERE id = @id", new { id });
            Db.Exec("UPDATE chapters SET deleted_at = NULL WHERE id = @id", new { id });
            if (ts.HasValue)
                Db.Exec("UPDATE decks SET deleted_at = NULL WHERE chapter_id = @id AND deleted_at = @ts", new { id, ts = ts.Value });
        });
    }

    /// <summary>Moves a chapter up (-1) or down (+1).</summary>
    public void MoveChapter(long id, int direction)
    {
        var chapter = GetChapter(id);
        if (chapter == null) return;
        var list = GetChapters(chapter.SubjectId);
        int index = list.FindIndex(c => c.Id == id);
        int target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]);
        Db.Tx(() =>
        {
            for (int i = 0; i < list.Count; i++)
                Db.Exec("UPDATE chapters SET sort_order = @i WHERE id = @id", new { i, id = list[i].Id });
        });
    }

    // ───────────────────────────────────────────────────────────── decks

    private static Deck ReadDeck(SqliteDataReader r) => new()
    {
        Id = r.Long("id"),
        SubjectId = r.Long("subject_id"),
        ChapterId = r.LongN("chapter_id"),
        Kind = (DeckKind)r.Int("kind"),
        Title = r.Str("title"),
        Description = r.Str("description"),
        Source = r.Str("source"),
        Difficulty = r.Int("difficulty"),
        Language = r.Str("language"),
        SortOrder = r.Int("sort_order"),
        CreatedAt = r.Long("created_at"),
        UpdatedAt = r.Long("updated_at"),
        LastStudiedAt = r.LongN("last_studied_at"),
        DeletedAt = r.LongN("deleted_at"),
    };

    public List<Deck> GetDecks(long subjectId) =>
        Db.Query("""
            SELECT d.* FROM decks d
            LEFT JOIN chapters c ON c.id = d.chapter_id
            WHERE d.subject_id = @subjectId AND d.deleted_at IS NULL AND (c.id IS NULL OR c.deleted_at IS NULL)
            ORDER BY d.sort_order, d.id
            """, ReadDeck, new { subjectId });

    public Deck? GetDeck(long id) =>
        Db.First("SELECT * FROM decks WHERE id = @id AND deleted_at IS NULL", ReadDeck, new { id });

    public long CreateDeck(Deck d)
    {
        var now = Now();
        d.CreatedAt = d.UpdatedAt = now;
        d.SortOrder = (int)Db.Scalar<long>("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM decks WHERE subject_id = @SubjectId", new { d.SubjectId });
        d.Id = Db.Insert("""
            INSERT INTO decks (subject_id, chapter_id, kind, title, description, source, difficulty, language, sort_order, created_at, updated_at)
            VALUES (@SubjectId, @ChapterId, @Kind, @Title, @Description, @Source, @Difficulty, @Language, @SortOrder, @CreatedAt, @UpdatedAt)
            """, new { d.SubjectId, d.ChapterId, d.Kind, d.Title, d.Description, d.Source, d.Difficulty, d.Language, d.SortOrder, d.CreatedAt, d.UpdatedAt });
        return d.Id;
    }

    public void UpdateDeck(Deck d)
    {
        d.UpdatedAt = Now();
        Db.Exec("""
            UPDATE decks SET chapter_id = @ChapterId, kind = @Kind, title = @Title, description = @Description, source = @Source,
                   difficulty = @Difficulty, language = @Language, updated_at = @UpdatedAt
            WHERE id = @Id
            """, new { d.Id, d.ChapterId, d.Kind, d.Title, d.Description, d.Source, d.Difficulty, d.Language, d.UpdatedAt });
    }

    public void DeleteDeck(long id) =>
        Db.Exec("UPDATE decks SET deleted_at = @now WHERE id = @id", new { id, now = Now() });

    public void RestoreDeck(long id) =>
        Db.Exec("UPDATE decks SET deleted_at = NULL WHERE id = @id", new { id });

    public void TouchDeckStudied(long id) =>
        Db.Exec("UPDATE decks SET last_studied_at = @now WHERE id = @id", new { id, now = Now() });

    /// <summary>Moves a deck up (-1) or down (+1) within its subject.</summary>
    public void MoveDeck(long id, int direction)
    {
        var deck = GetDeck(id);
        if (deck == null) return;
        var list = GetDecks(deck.SubjectId).Where(d => d.ChapterId == deck.ChapterId).ToList();
        int index = list.FindIndex(d => d.Id == id);
        int target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]);
        Db.Tx(() =>
        {
            for (int i = 0; i < list.Count; i++)
                Db.Exec("UPDATE decks SET sort_order = @i WHERE id = @id", new { i, id = list[i].Id });
        });
    }

    // ───────────────────────────────────────────────────────────── settings

    public string? GetSetting(string key) =>
        Db.Scalar<string?>("SELECT value FROM settings WHERE key = @key", new { key });

    public void SetSetting(string key, string value) =>
        Db.Exec("INSERT INTO settings (key, value) VALUES (@key, @value) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            new { key, value });

    public Domain.Settings.AppSettings LoadSettings() => Domain.Settings.AppSettings.FromJson(GetSetting("app"));

    public void SaveSettings(Domain.Settings.AppSettings settings) => SetSetting("app", settings.ToJson());
}
