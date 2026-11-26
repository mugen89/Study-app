namespace StudyApp.Data;

/// <summary>
/// Schema migrations. Each entry upgrades the database by one version; the current version is kept in
/// <c>PRAGMA user_version</c>. Never edit a released step — add a new one at the end.
/// </summary>
internal static class Migrations
{
    public static int LatestVersion => Steps.Length;

    private static readonly string[] Steps =
    {
        // ── v1: initial schema ──────────────────────────────────────────────────
        """
        CREATE TABLE subjects (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            name            TEXT    NOT NULL,
            description     TEXT    NOT NULL DEFAULT '',
            source          TEXT    NOT NULL DEFAULT '',
            color           TEXT    NOT NULL DEFAULT '#4F6BED',
            icon            TEXT    NOT NULL DEFAULT 'book',
            archived        INTEGER NOT NULL DEFAULT 0,
            sort_order      INTEGER NOT NULL DEFAULT 0,
            created_at      INTEGER NOT NULL,
            updated_at      INTEGER NOT NULL,
            last_opened_at  INTEGER,
            deleted_at      INTEGER
        );

        CREATE TABLE chapters (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            subject_id  INTEGER NOT NULL REFERENCES subjects(id) ON DELETE CASCADE,
            number      TEXT    NOT NULL DEFAULT '',
            title       TEXT    NOT NULL DEFAULT '',
            pages       TEXT    NOT NULL DEFAULT '',
            sort_order  INTEGER NOT NULL DEFAULT 0,
            created_at  INTEGER NOT NULL,
            deleted_at  INTEGER
        );
        CREATE INDEX ix_chapters_subject ON chapters(subject_id);

        CREATE TABLE decks (
            id               INTEGER PRIMARY KEY AUTOINCREMENT,
            subject_id       INTEGER NOT NULL REFERENCES subjects(id) ON DELETE CASCADE,
            chapter_id       INTEGER REFERENCES chapters(id) ON DELETE SET NULL,
            kind             INTEGER NOT NULL DEFAULT 0,
            title            TEXT    NOT NULL,
            description      TEXT    NOT NULL DEFAULT '',
            source           TEXT    NOT NULL DEFAULT '',
            difficulty       INTEGER NOT NULL DEFAULT 0,
            language         TEXT    NOT NULL DEFAULT '',
            sort_order       INTEGER NOT NULL DEFAULT 0,
            created_at       INTEGER NOT NULL,
            updated_at       INTEGER NOT NULL,
            last_studied_at  INTEGER,
            deleted_at       INTEGER
        );
        CREATE INDEX ix_decks_subject ON decks(subject_id);
        CREATE INDEX ix_decks_chapter ON decks(chapter_id);

        CREATE TABLE items (
            id               INTEGER PRIMARY KEY AUTOINCREMENT,
            deck_id          INTEGER NOT NULL REFERENCES decks(id) ON DELETE CASCADE,
            type             TEXT    NOT NULL,
            prompt           TEXT    NOT NULL DEFAULT '',
            answer           TEXT    NOT NULL DEFAULT '',
            data             TEXT    NOT NULL DEFAULT '{}',
            explanation      TEXT    NOT NULL DEFAULT '',
            page             TEXT    NOT NULL DEFAULT '',
            source           TEXT    NOT NULL DEFAULT '',
            tags             TEXT    NOT NULL DEFAULT '',
            difficulty       INTEGER NOT NULL DEFAULT 0,
            fingerprint      TEXT    NOT NULL DEFAULT '',
            flagged          INTEGER NOT NULL DEFAULT 0,
            wrong_in_source  INTEGER NOT NULL DEFAULT 0,
            personal_note    TEXT    NOT NULL DEFAULT '',
            suspended        INTEGER NOT NULL DEFAULT 0,
            srs_state        INTEGER NOT NULL DEFAULT 0,
            srs_step         INTEGER NOT NULL DEFAULT 0,
            srs_ease         REAL    NOT NULL DEFAULT 2.5,
            srs_interval     REAL    NOT NULL DEFAULT 0,
            srs_due          INTEGER,
            srs_reps         INTEGER NOT NULL DEFAULT 0,
            srs_lapses       INTEGER NOT NULL DEFAULT 0,
            seen_count       INTEGER NOT NULL DEFAULT 0,
            correct_count    INTEGER NOT NULL DEFAULT 0,
            last_result      INTEGER NOT NULL DEFAULT -1,
            last_seen_at     INTEGER,
            sort_order       INTEGER NOT NULL DEFAULT 0,
            created_at       INTEGER NOT NULL,
            updated_at       INTEGER NOT NULL,
            deleted_at       INTEGER
        );
        CREATE INDEX ix_items_deck ON items(deck_id);
        CREATE INDEX ix_items_due ON items(srs_due);
        CREATE INDEX ix_items_fingerprint ON items(fingerprint);

        CREATE TABLE sessions (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            subject_id   INTEGER REFERENCES subjects(id) ON DELETE SET NULL,
            mode         INTEGER NOT NULL,
            title        TEXT    NOT NULL DEFAULT '',
            settings     TEXT    NOT NULL DEFAULT '{}',
            started_at   INTEGER NOT NULL,
            ended_at     INTEGER,
            item_count   INTEGER NOT NULL DEFAULT 0,
            correct      INTEGER NOT NULL DEFAULT 0,
            partial      INTEGER NOT NULL DEFAULT 0,
            wrong        INTEGER NOT NULL DEFAULT 0,
            score        REAL    NOT NULL DEFAULT 0,
            duration_ms  INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE reviews (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            item_id      INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
            session_id   INTEGER REFERENCES sessions(id) ON DELETE SET NULL,
            subject_id   INTEGER NOT NULL,
            reviewed_at  INTEGER NOT NULL,
            study_day    TEXT    NOT NULL,
            rating       INTEGER NOT NULL,
            result       INTEGER NOT NULL,
            duration_ms  INTEGER NOT NULL DEFAULT 0,
            was_new      INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX ix_reviews_day ON reviews(study_day);
        CREATE INDEX ix_reviews_item ON reviews(item_id);
        CREATE INDEX ix_reviews_subject ON reviews(subject_id, study_day);

        CREATE TABLE prompt_templates (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            key          TEXT    NOT NULL,
            lang         TEXT    NOT NULL,
            name         TEXT    NOT NULL,
            description  TEXT    NOT NULL DEFAULT '',
            body         TEXT    NOT NULL,
            is_builtin   INTEGER NOT NULL DEFAULT 0,
            is_modified  INTEGER NOT NULL DEFAULT 0,
            sort_order   INTEGER NOT NULL DEFAULT 0,
            updated_at   INTEGER NOT NULL,
            UNIQUE (key, lang)
        );

        CREATE TABLE settings (
            key    TEXT PRIMARY KEY,
            value  TEXT NOT NULL
        );
        """,
    };

    public static void Apply(Db db)
    {
        var current = (int)db.Scalar<long>("PRAGMA user_version;");
        if (current > Steps.Length)
        {
            Log.Warn($"Database version {current} is newer than this app ({Steps.Length}). Running in compatibility mode.");
            return;
        }

        for (int v = current; v < Steps.Length; v++)
        {
            var step = Steps[v];
            db.Tx(() =>
            {
                db.Exec(step);
                db.Exec($"PRAGMA user_version = {v + 1};");
            });
            Log.Info($"Database migrated to version {v + 1}.");
        }
    }
}
