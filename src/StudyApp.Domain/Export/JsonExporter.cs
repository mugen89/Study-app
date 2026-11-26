using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using StudyApp.Domain.Models;

namespace StudyApp.Domain.Export;

public sealed class ExportDeck
{
    public required Deck Deck { get; init; }
    public Chapter? Chapter { get; init; }
    public required IReadOnlyList<Item> Items { get; init; }
}

/// <summary>Writes decks in the same JSON format the importer reads (schemaVersion 1, "decks" list).</summary>
public static class JsonExporter
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Cyrillic readable
    };

    public static string Export(Subject? subject, IEnumerable<ExportDeck> decks, string appVersion)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", 1);
            if (subject != null)
            {
                w.WriteString("subject", subject.Name);
                WriteIf(w, "source", subject.Source);
                WriteIf(w, "description", subject.Description);
            }
            w.WriteString("exportedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            w.WriteString("exportedBy", "Study App " + appVersion);

            w.WriteStartArray("decks");
            foreach (var d in decks)
            {
                w.WriteStartObject();
                w.WriteString("title", d.Deck.Title);
                w.WriteString("kind", d.Deck.Kind switch { DeckKind.Test => "test", DeckKind.Notes => "notes", _ => "deck" });
                if (d.Deck.Difficulty > 0) w.WriteNumber("difficulty", d.Deck.Difficulty);
                WriteIf(w, "description", d.Deck.Description);
                WriteIf(w, "source", d.Deck.Source);
                WriteIf(w, "language", d.Deck.Language);
                if (d.Chapter != null)
                {
                    w.WriteStartObject("chapter");
                    WriteIf(w, "number", d.Chapter.Number);
                    w.WriteString("title", d.Chapter.Title);
                    WriteIf(w, "pages", d.Chapter.Pages);
                    w.WriteEndObject();
                }

                w.WriteStartArray("items");
                foreach (var item in d.Items) WriteItem(w, item, d.Deck.Source);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void WriteItem(Utf8JsonWriter w, Item item, string deckSource = "")
    {
        w.WriteStartObject();
        w.WriteString("type", item.Type);
        switch (item.Type)
        {
            case ItemTypes.Flashcard:
                w.WriteString("front", item.Prompt);
                w.WriteString("back", item.Answer);
                break;

            case ItemTypes.Single:
            case ItemTypes.Multiple:
                {
                    w.WriteString("question", item.Prompt);
                    var options = item.Data.Options ?? new();
                    bool withWhy = options.Any(o => !string.IsNullOrWhiteSpace(o.Why));
                    w.WriteStartArray("options");
                    foreach (var o in options)
                    {
                        if (withWhy)
                        {
                            w.WriteStartObject();
                            w.WriteString("text", o.Text);
                            WriteIf(w, "why", o.Why);
                            w.WriteEndObject();
                        }
                        else w.WriteStringValue(o.Text);
                    }
                    w.WriteEndArray();
                    var correct = item.Data.Correct ?? new();
                    if (item.Type == ItemTypes.Single && correct.Count == 1)
                        w.WriteString("answer", Letter(correct[0]));
                    else
                    {
                        w.WriteStartArray("answer");
                        foreach (var c in correct) w.WriteStringValue(Letter(c));
                        w.WriteEndArray();
                    }
                    break;
                }

            case ItemTypes.TrueFalse:
                w.WriteString("question", item.Prompt);
                w.WriteBoolean("answer", item.Data.Truth == true);
                break;

            case ItemTypes.Short:
                w.WriteString("question", item.Prompt);
                w.WriteString("answer", item.Answer);
                WriteList(w, "accept", item.Data.Accept);
                WriteList(w, "keyPoints", item.Data.KeyPoints);
                break;

            case ItemTypes.Note:
                w.WriteString("heading", item.Prompt);
                w.WriteString("content", item.Answer);
                break;

            default:
                w.WriteString("prompt", item.Prompt);
                w.WriteString("answer", item.Answer);
                break;
        }

        WriteIf(w, "hint", item.Data.Hint);
        WriteIf(w, "explanation", item.Explanation);
        WriteIf(w, "page", item.Page);
        if (item.Difficulty > 0) w.WriteNumber("difficulty", item.Difficulty);
        WriteList(w, "tags", item.Tags);
        if (!string.IsNullOrWhiteSpace(item.Source) && item.Source != deckSource) w.WriteString("source", item.Source);
        w.WriteEndObject();
    }

    private static string Letter(int index) => ((char)('A' + index)).ToString();

    private static void WriteIf(Utf8JsonWriter w, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) w.WriteString(name, value);
    }

    private static void WriteList(Utf8JsonWriter w, string name, IEnumerable<string>? values)
    {
        var list = values?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (list == null || list.Count == 0) return;
        w.WriteStartArray(name);
        foreach (var v in list) w.WriteStringValue(v);
        w.WriteEndArray();
    }
}
