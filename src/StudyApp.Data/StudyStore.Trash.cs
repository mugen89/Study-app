using StudyApp.Domain;
using StudyApp.Domain.Models;

namespace StudyApp.Data;

public enum TrashKind
{
    Subject,
    Chapter,
    Deck,
    Item,
}

public sealed record TrashEntry(TrashKind Kind, long Id, string Title, string Context, long DeletedAt, int Children);

public sealed record SearchHit(TrashKind Kind, long Id, string Title, string Context, long SubjectId, long? DeckId, Item? Item);

public sealed partial class StudyStore
{
    // ───────────────────────────────────────────────────────────── recycle bin

    public List<TrashEntry> GetTrash()
    {
        var list = new List<TrashEntry>();
        list.AddRange(Db.Query("""
            SELECT s.id, s.name, s.deleted_at,
                   (SELECT COUNT(*) FROM items i JOIN decks d ON d.id = i.deck_id WHERE d.subject_id = s.id AND i.deleted_at IS NULL) AS n
            FROM subjects s WHERE s.deleted_at IS NOT NULL
            """, r => new TrashEntry(TrashKind.Subject, r.Long("id"), r.Str("name"), "", r.Long("deleted_at"), r.Int("n"))));

        list.AddRange(Db.Query("""
            SELECT c.id, c.number, c.title, c.deleted_at, s.name AS subject,
                   (SELECT COUNT(*) FROM items i JOIN decks d ON d.id = i.deck_id WHERE d.chapter_id = c.id AND i.deleted_at IS NULL) AS n
            FROM chapters c JOIN subjects s ON s.id = c.subject_id
            WHERE c.deleted_at IS NOT NULL AND s.deleted_at IS NULL
            """, r => new TrashEntry(TrashKind.Chapter, r.Long("id"),
                (r.Str("number").Length > 0 ? r.Str("number") + ". " : "") + r.Str("title"), r.Str("subject"), r.Long("deleted_at"), r.Int("n"))));

        // Decks deleted on their own (not as part of a deleted chapter).
        list.AddRange(Db.Query("""
            SELECT d.id, d.title, d.deleted_at, s.name AS subject,
                   (SELECT COUNT(*) FROM items i WHERE i.deck_id = d.id AND i.deleted_at IS NULL) AS n
            FROM decks d JOIN subjects s ON s.id = d.subject_id
            LEFT JOIN chapters c ON c.id = d.chapter_id
            WHERE d.deleted_at IS NOT NULL AND s.deleted_at IS NULL
              AND (c.id IS NULL OR c.deleted_at IS NULL OR c.deleted_at <> d.deleted_at)
            """, r => new TrashEntry(TrashKind.Deck, r.Long("id"), r.Str("title"), r.Str("subject"), r.Long("deleted_at"), r.Int("n"))));

        list.AddRange(Db.Query("""
            SELECT i.id, i.prompt, i.deleted_at, d.title AS deck, s.name AS subject
            FROM items i JOIN decks d ON d.id = i.deck_id JOIN subjects s ON s.id = d.subject_id
            WHERE i.deleted_at IS NOT NULL AND d.deleted_at IS NULL AND s.deleted_at IS NULL
            """, r => new TrashEntry(TrashKind.Item, r.Long("id"), TextUtil.Truncate(TextUtil.StripMarkdown(r.Str("prompt")), 140),
                r.Str("subject") + " › " + r.Str("deck"), r.Long("deleted_at"), 0)));

        return list.OrderByDescending(e => e.DeletedAt).ToList();
    }

    public void Restore(TrashKind kind, long id)
    {
        switch (kind)
        {
            case TrashKind.Subject: RestoreSubject(id); break;
            case TrashKind.Chapter: RestoreChapter(id); break;
            case TrashKind.Deck: RestoreDeck(id); break;
            case TrashKind.Item: RestoreItem(id); break;
        }
    }

    public void DeleteForever(TrashKind kind, long id)
    {
        Db.Tx(() =>
        {
            switch (kind)
            {
                case TrashKind.Subject:
                    Db.Exec("DELETE FROM subjects WHERE id = @id", new { id });
                    break;
                case TrashKind.Chapter:
                    Db.Exec("DELETE FROM decks WHERE chapter_id = @id AND deleted_at IS NOT NULL", new { id });
                    Db.Exec("DELETE FROM chapters WHERE id = @id", new { id });
                    break;
                case TrashKind.Deck:
                    Db.Exec("DELETE FROM decks WHERE id = @id", new { id });
                    break;
                case TrashKind.Item:
                    Db.Exec("DELETE FROM items WHERE id = @id", new { id });
                    break;
            }
        });
    }

    public void EmptyTrash() => PurgeTrash(olderThanMs: null);

    /// <summary>Permanently removes everything deleted before the cut-off (null = everything).</summary>
    public int PurgeTrash(long? olderThanMs)
    {
        var cutoff = olderThanMs ?? long.MaxValue;
        int removed = 0;
        Db.Tx(() =>
        {
            removed += Db.Exec("DELETE FROM items WHERE deleted_at IS NOT NULL AND deleted_at < @cutoff", new { cutoff });
            removed += Db.Exec("DELETE FROM decks WHERE deleted_at IS NOT NULL AND deleted_at < @cutoff", new { cutoff });
            removed += Db.Exec("DELETE FROM chapters WHERE deleted_at IS NOT NULL AND deleted_at < @cutoff", new { cutoff });
            removed += Db.Exec("DELETE FROM subjects WHERE deleted_at IS NOT NULL AND deleted_at < @cutoff", new { cutoff });
        });
        return removed;
    }

    public int TrashCount() =>
        (int)Db.Scalar<long>("""
            SELECT (SELECT COUNT(*) FROM subjects WHERE deleted_at IS NOT NULL)
                 + (SELECT COUNT(*) FROM chapters WHERE deleted_at IS NOT NULL)
                 + (SELECT COUNT(*) FROM decks WHERE deleted_at IS NOT NULL)
                 + (SELECT COUNT(*) FROM items WHERE deleted_at IS NOT NULL)
            """);

    // ───────────────────────────────────────────────────────────── global search

    public List<SearchHit> Search(string text, int limit = 200)
    {
        var q = text.Trim();
        if (q.Length == 0) return new();
        var like = "%" + q.ToLowerInvariant() + "%";
        var hits = new List<SearchHit>();

        hits.AddRange(Db.Query("""
            SELECT id, name, source FROM subjects
            WHERE deleted_at IS NULL AND (ulower(name) LIKE @like OR ulower(source) LIKE @like OR ulower(description) LIKE @like)
            ORDER BY name LIMIT 20
            """, r => new SearchHit(TrashKind.Subject, r.Long("id"), r.Str("name"), r.Str("source"), r.Long("id"), null, null), new { like }));

        hits.AddRange(Db.Query("""
            SELECT d.id, d.title, d.subject_id, s.name AS subject FROM decks d JOIN subjects s ON s.id = d.subject_id
            LEFT JOIN chapters c ON c.id = d.chapter_id
            WHERE d.deleted_at IS NULL AND s.deleted_at IS NULL AND (c.id IS NULL OR c.deleted_at IS NULL)
              AND ulower(d.title) LIKE @like
            ORDER BY d.title LIMIT 30
            """, r => new SearchHit(TrashKind.Deck, r.Long("id"), r.Str("title"), r.Str("subject"), r.Long("subject_id"), r.Long("id"), null), new { like }));

        foreach (var item in QueryItems(new ItemQuery { Search = q, Limit = limit, WrongInSource = null, IncludeSuspended = true }))
            hits.Add(new SearchHit(TrashKind.Item, item.Id, item.Prompt, item.SubjectName + " › " + item.DeckTitle, item.SubjectId, item.DeckId, item));

        return hits;
    }
}
