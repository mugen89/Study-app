using Microsoft.Data.Sqlite;
using StudyApp.Domain.Models;
using StudyApp.Domain.Prompts;

namespace StudyApp.Data;

public sealed partial class StudyStore
{
    private static PromptTemplate ReadTemplate(SqliteDataReader r) => new()
    {
        Id = r.Long("id"),
        Key = r.Str("key"),
        Lang = r.Str("lang"),
        Name = r.Str("name"),
        Description = r.Str("description"),
        Body = r.Str("body"),
        IsBuiltIn = r.Bool("is_builtin"),
        IsModified = r.Bool("is_modified"),
        SortOrder = r.Int("sort_order"),
        UpdatedAt = r.Long("updated_at"),
    };

    /// <summary>
    /// Copies new built-in templates into the database and refreshes built-ins the user has not edited,
    /// so app updates can improve the default prompts without overwriting the user's changes.
    /// </summary>
    public void EnsureBuiltInTemplates()
    {
        Db.Tx(() =>
        {
            foreach (var lang in new[] { "en", "bg" })
            foreach (var t in PromptDefaults.All(lang))
            {
                var existing = Db.First("SELECT * FROM prompt_templates WHERE key = @Key AND lang = @Lang", ReadTemplate, new { t.Key, t.Lang });
                if (existing == null)
                {
                    Db.Exec("""
                        INSERT INTO prompt_templates (key, lang, name, description, body, is_builtin, is_modified, sort_order, updated_at)
                        VALUES (@Key, @Lang, @Name, @Description, @Body, 1, 0, @SortOrder, @now)
                        """, new { t.Key, t.Lang, t.Name, t.Description, t.Body, t.SortOrder, now = Now() });
                }
                else if (!existing.IsModified && (existing.Body != t.Body || existing.Name != t.Name || existing.Description != t.Description))
                {
                    Db.Exec("UPDATE prompt_templates SET name = @Name, description = @Description, body = @Body, sort_order = @SortOrder, updated_at = @now WHERE id = @Id",
                        new { existing.Id, t.Name, t.Description, t.Body, t.SortOrder, now = Now() });
                }
            }
        });
    }

    public List<PromptTemplate> GetTemplates(string lang) =>
        Db.Query("SELECT * FROM prompt_templates WHERE lang = @lang ORDER BY is_builtin DESC, sort_order, name", ReadTemplate, new { lang });

    public PromptTemplate? GetTemplate(long id) =>
        Db.First("SELECT * FROM prompt_templates WHERE id = @id", ReadTemplate, new { id });

    public void SaveTemplate(PromptTemplate t)
    {
        if (t.Id == 0)
        {
            if (string.IsNullOrEmpty(t.Key)) t.Key = "custom-" + Guid.NewGuid().ToString("N")[..10];
            t.SortOrder = 100 + (int)Db.Scalar<long>("SELECT COUNT(*) FROM prompt_templates WHERE lang = @Lang", new { t.Lang });
            t.Id = Db.Insert("""
                INSERT INTO prompt_templates (key, lang, name, description, body, is_builtin, is_modified, sort_order, updated_at)
                VALUES (@Key, @Lang, @Name, @Description, @Body, 0, 0, @SortOrder, @now)
                """, new { t.Key, t.Lang, t.Name, t.Description, t.Body, t.SortOrder, now = Now() });
            return;
        }

        var builtIn = t.IsBuiltIn ? PromptDefaults.Find(t.Key, t.Lang) : null;
        t.IsModified = builtIn != null && (builtIn.Body != t.Body || builtIn.Name != t.Name || builtIn.Description != t.Description);
        Db.Exec("UPDATE prompt_templates SET name = @Name, description = @Description, body = @Body, is_modified = @IsModified, updated_at = @now WHERE id = @Id",
            new { t.Id, t.Name, t.Description, t.Body, t.IsModified, now = Now() });
    }

    public void ResetTemplate(long id)
    {
        var t = GetTemplate(id);
        var def = t == null ? null : PromptDefaults.Find(t.Key, t.Lang);
        if (t == null || def == null) return;
        Db.Exec("UPDATE prompt_templates SET name = @Name, description = @Description, body = @Body, is_modified = 0, updated_at = @now WHERE id = @id",
            new { id, def.Name, def.Description, def.Body, now = Now() });
    }

    public void DeleteTemplate(long id) =>
        Db.Exec("DELETE FROM prompt_templates WHERE id = @id AND is_builtin = 0", new { id });
}
