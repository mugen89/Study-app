using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using StudyApp.Domain.Models;

namespace StudyApp.Domain.Prompts;

/// <summary>
/// Fills {{placeholders}} in a template. A line whose placeholders all resolve to nothing is removed,
/// so optional fields (focus, extra instructions…) simply disappear when left empty.
/// Placeholders: subject, source, chapter, pages, scope, count, difficulty, difficulty_rules, question_types,
/// language, focus, extra, weak_items, error, rules, option_why_rule, schema_flashcards, schema_test[:types],
/// schema_notes, schema_pack.
/// </summary>
public static partial class PromptRenderer
{
    public static readonly string[] Placeholders =
    {
        "subject", "source", "chapter", "pages", "scope", "count", "difficulty", "difficulty_rules", "question_types",
        "language", "focus", "extra", "weak_items", "error", "rules", "option_why_rule",
        "schema_flashcards", "schema_test", "schema_notes", "schema_pack",
    };

    public static string Render(string body, PromptOptions o, string lang)
    {
        var bg = lang == "bg";
        var output = new List<string>();
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var matches = PlaceholderRegex().Matches(line);
            if (matches.Count == 0)
            {
                output.Add(line);
                continue;
            }

            bool anyValue = false;
            var replaced = PlaceholderRegex().Replace(line, m =>
            {
                var value = Resolve(m.Groups["name"].Value.ToLowerInvariant(), m.Groups["arg"].Value, o, bg);
                if (!string.IsNullOrEmpty(value)) anyValue = true;
                return value;
            });
            if (anyValue) output.Add(replaced);
        }

        var text = string.Join("\n", output).Trim();
        while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
        return text;
    }

    /// <summary>Which fill-in fields a template needs (the UI shows only those).</summary>
    public static HashSet<string> UsedPlaceholders(string body) =>
        PlaceholderRegex().Matches(body).Select(m => m.Groups["name"].Value.ToLowerInvariant()).ToHashSet();

    private static string Resolve(string name, string arg, PromptOptions o, bool bg) => name switch
    {
        "subject" => Or(o.Subject, bg ? "този предмет" : "this subject"),
        "source" => o.Source.Trim(),
        "chapter" => Chapter(o),
        "pages" => o.Pages.Trim(),
        "scope" => Scope(o, bg),
        "count" => Math.Max(1, o.Count).ToString(),
        "difficulty" => DifficultyName(o.Difficulty, bg),
        "difficulty_rules" => DifficultyRules(o.Difficulty, bg),
        "question_types" => QuestionTypes(o.QuestionTypes, bg),
        "language" => LanguageName(o.Language, bg),
        "focus" => o.Focus.Trim(),
        "extra" => o.Extra.Trim(),
        "weak_items" => Or(o.WeakItems, bg
            ? "(Все още няма записани грешки – наблегни на понятията, които най-често се разбират погрешно.)"
            : "(No mistakes recorded yet – focus on the concepts that are most often misunderstood.)"),
        "error" => Or(o.ErrorText, bg ? "(постави съобщението за грешка тук)" : "(paste the error message here)"),
        "rules" => Rules(bg).Replace("{{language}}", LanguageName(o.Language, bg)),
        "option_why_rule" => o.ExplainOptions
            ? (bg ? "- За всеки вариант добави \"why\": едно изречение защо е верен или грешен." : "- For every option add \"why\": one sentence explaining why it is right or wrong.")
            : "",
        "schema_flashcards" => Fence(SchemaBuilder.Flashcards(o, bg)),
        "schema_test" => Fence(SchemaBuilder.Test(o, bg, ParseTypes(arg, o.QuestionTypes))),
        "schema_notes" => Fence(SchemaBuilder.Notes(o, bg)),
        "schema_pack" => Fence(SchemaBuilder.Pack(o, bg)),
        _ => "{{" + name + (arg.Length > 0 ? ":" + arg : "") + "}}",
    };

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Fence(string json) => "```json\n" + json + "\n```";

    private static List<string> ParseTypes(string arg, List<string> fallback)
    {
        if (string.IsNullOrWhiteSpace(arg)) return fallback.Count > 0 ? fallback : ItemTypes.Questions.ToList();
        return arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ItemTypes.Normalize).Where(t => t != null && ItemTypes.IsQuestion(t)).Select(t => t!).Distinct().ToList();
    }

    public static string Chapter(PromptOptions o)
    {
        var n = o.ChapterNumber.Trim();
        var t = o.ChapterTitle.Trim();
        if (n.Length > 0 && t.Length > 0) return $"{n}. {t}";
        return n.Length > 0 ? n : t;
    }

    public static string Scope(PromptOptions o, bool bg)
    {
        var n = o.ChapterNumber.Trim();
        var t = o.ChapterTitle.Trim();
        var p = o.Pages.Trim();
        string chapter;
        if (bg)
        {
            chapter = n.Length > 0 && t.Length > 0 ? $"глава {n} „{t}“"
                : n.Length > 0 ? $"глава {n}"
                : t.Length > 0 ? $"главата „{t}“" : "";
            if (chapter.Length > 0) return p.Length > 0 ? $"{chapter} (страници {p})" : chapter;
            return p.Length > 0 ? $"страници {p} от учебника" : "целия качен материал";
        }

        chapter = n.Length > 0 && t.Length > 0 ? $"chapter {n} \"{t}\""
            : n.Length > 0 ? $"chapter {n}"
            : t.Length > 0 ? $"the chapter \"{t}\"" : "";
        if (chapter.Length > 0) return p.Length > 0 ? $"{chapter} (pages {p})" : chapter;
        return p.Length > 0 ? $"pages {p} of the textbook" : "the whole uploaded material";
    }

    public static string LanguageName(string code, bool bg)
    {
        var c = code.Trim();
        if (c.Length == 0) return bg ? "същия език като учебника" : "the same language as the textbook";
        if (c.Equals("en", StringComparison.OrdinalIgnoreCase)) return bg ? "английски език" : "English";
        if (c.Equals("bg", StringComparison.OrdinalIgnoreCase)) return bg ? "български език" : "Bulgarian";
        return c;
    }

    public static string DifficultyName(int d, bool bg) => d switch
    {
        1 => bg ? "1 (много лесно)" : "1 (very easy)",
        2 => bg ? "2 (лесно)" : "2 (easy)",
        3 => bg ? "3 (средно)" : "3 (medium)",
        4 => bg ? "4 (трудно)" : "4 (hard)",
        5 => bg ? "5 (експертно)" : "5 (expert)",
        _ => bg ? "смесена (1–5)" : "mixed (1–5)",
    };

    public static string DifficultyRules(int d, bool bg) => d switch
    {
        1 => bg ? "Много лесно (1/5) – основно запомняне на ключови термини и факти, посочени директно в текста."
                : "Very easy (1/5) – basic recall of key terms and facts stated directly in the text.",
        2 => bg ? "Лесно (2/5) – запомняне на определения, факти и прости връзки."
                : "Easy (2/5) – recall of definitions, facts and simple relationships.",
        3 => bg ? "Средно (3/5) – разбиране: обясни, сравни, класифицирай, посочи причини и следствия."
                : "Medium (3/5) – understanding: explain, compare, classify, identify causes and effects.",
        4 => bg ? "Трудно (4/5) – прилагане и анализ: нови ситуации, разсъждение в няколко стъпки, разграничаване на сходни понятия; грешните отговори да отразяват често срещани заблуди."
                : "Hard (4/5) – application and analysis: new situations, multi-step reasoning, telling similar concepts apart; wrong options should reflect common misconceptions.",
        5 => bg ? "Експертно / изпитно ниво (5/5) – сложни ситуации, които съчетават няколко понятия, изключения и гранични случаи; всеки грешен отговор трябва да е правдоподобен."
                : "Expert / exam level (5/5) – complex scenarios that combine several concepts, exceptions and edge cases; every wrong option must be plausible.",
        _ => bg ? "Смесена – около 30% лесни, 40% средни и 30% трудни. Задай \"difficulty\" (1–5) на всеки елемент според трудността му."
                : "Mixed – about 30% easy, 40% medium and 30% hard. Set each item's \"difficulty\" (1–5) to match.",
    };

    public static string QuestionTypes(IReadOnlyCollection<string> types, bool bg)
    {
        var list = types.Count > 0 ? types : ItemTypes.Questions;
        var sb = new StringBuilder();
        foreach (var t in ItemTypes.Questions.Where(list.Contains))
        {
            sb.Append(t switch
            {
                ItemTypes.Single => bg ? "- \"single\": точно един верен отговор от 4 варианта (\"answer\": \"B\")"
                                       : "- \"single\": exactly one correct answer out of 4 options (\"answer\": \"B\")",
                ItemTypes.Multiple => bg ? "- \"multiple\": 2–3 верни отговора от 4–6 варианта (\"answer\": [\"A\", \"C\"])"
                                         : "- \"multiple\": 2–3 correct answers out of 4–6 options (\"answer\": [\"A\", \"C\"])",
                ItemTypes.TrueFalse => bg ? "- \"truefalse\": твърдение, което е ясно вярно или невярно (\"answer\": true)"
                                          : "- \"truefalse\": a statement that is clearly true or false (\"answer\": true)",
                _ => bg ? "- \"short\": отговор с няколко думи или 1–3 изречения (примерен \"answer\" и \"keyPoints\")"
                        : "- \"short\": answered in a few words or 1–3 sentences (a model \"answer\" plus \"keyPoints\")",
            }).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    public static string Rules(bool bg) => bg
        ? """
          Общи правила:
          - Използвай САМО учебника/материалите, които качих в този чат или проект. Не добавяй факти, които ги няма в тях.
          - Всеки елемент ТРЯБВА да има "page": номера на страницата (или страниците) в учебника, където е отговорът, напр. "52" или "52-53". Използвай номерата, отпечатани в книгата; ако няма такива – номерата на страниците в PDF файла.
          - Всеки елемент ТРЯБВА да има "explanation" (1–3 изречения), което обяснява защо отговорът е верен, за да се уча от грешките си.
          - Задай "difficulty" на всеки елемент (1 = много лесно … 5 = експертно) и добави 1–3 тематични "tags".
          - Пиши всички текстове на {{language}}. Ключовете в JSON и стойностите на "type" остави точно както в примера (на английски).
          - Върни САМО един валиден JSON блок с код, без текст преди или след него. Използвай двойни кавички, без коментари, без запетая след последния елемент и екранирай кавичките в текста.
          - Ако всичко не се събира в един отговор, върни по-малко, но завършени елементи, вместо да прекъсваш JSON-а.
          """
        : """
          General rules:
          - Use ONLY the textbook/material I have uploaded to this chat or project. Do not add facts that are not in it.
          - Every item MUST have "page": the page number(s) in the textbook where the answer can be found, e.g. "52" or "52-53". Use the page numbers printed in the book; if it has none, use the PDF page numbers.
          - Every item MUST have an "explanation" (1–3 sentences) that explains why the answer is correct, so I can learn from my mistakes.
          - Set "difficulty" on every item (1 = very easy … 5 = expert) and add 1–3 topic "tags".
          - Write all text in {{language}}. Keep the JSON keys and the "type" values exactly as in the example (in English).
          - Output ONLY one valid JSON code block, with no text before or after it. Use double quotes, no comments, no trailing commas, and escape quotes inside text.
          - If everything does not fit in one reply, output fewer complete items rather than cutting the JSON off.
          """;

    [GeneratedRegex(@"\{\{\s*(?<name>[A-Za-z_]+)(?::(?<arg>[A-Za-z_,]+))?\s*\}\}")]
    private static partial Regex PlaceholderRegex();
}

/// <summary>Builds the JSON examples embedded in prompts (always valid JSON, keys in English).</summary>
public static class SchemaBuilder
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record Words(
        string Subject, string Source, string Number, string ChapterTitle, string Pages, string DeckTitle,
        string Front, string Back, string Hint, string Explanation, string Topic, string Question,
        string Option, string Why, string Statement, string ModelAnswer, string Accept, string KeyPoint,
        string Heading, string Content);

    private static readonly Words En = new(
        "<subject>", "<textbook title>", "<number>", "<chapter title>", "<first-last page>", "<short title for this set>",
        "<question or term>", "<short, precise answer>", "<small hint that does not give the answer away>",
        "<why this is the answer, with extra context>", "<topic>", "<question text>", "<option {0}>",
        "<why {0} is right or wrong>", "<statement that is clearly true or false>", "<model answer>",
        "<another accepted wording>", "<point a good answer must mention>", "<section heading>",
        "<Markdown text: short paragraphs, - bullet points, **bold** key terms>");

    private static readonly Words Bg = new(
        "<предмет>", "<заглавие на учебника>", "<номер>", "<заглавие на главата>", "<първа-последна страница>", "<кратко заглавие на комплекта>",
        "<въпрос или термин>", "<кратък, точен отговор>", "<подсказка, която не издава отговора>",
        "<защо това е отговорът, с допълнителен контекст>", "<тема>", "<текст на въпроса>", "<вариант {0}>",
        "<защо {0} е верен или грешен>", "<твърдение, което е ясно вярно или невярно>", "<примерен отговор>",
        "<друга приемлива формулировка>", "<точка, която добрият отговор трябва да спомене>", "<заглавие на раздела>",
        "<Markdown текст: кратки абзаци, - точки, **удебелени** ключови термини>");

    public static string Flashcards(PromptOptions o, bool bg) => Build(o, bg, (w, words, d) =>
    {
        Deck(w, words, "deck", d);
        w.WriteStartArray("items");
        Flashcard(w, words, o, d);
        w.WriteEndArray();
    });

    public static string Test(PromptOptions o, bool bg, IReadOnlyCollection<string> types) => Build(o, bg, (w, words, d) =>
    {
        Deck(w, words, "test", d);
        w.WriteStartArray("items");
        foreach (var t in ItemTypes.Questions.Where(types.Contains)) Question(w, words, o, t, d);
        w.WriteEndArray();
    });

    public static string Notes(PromptOptions o, bool bg) => Build(o, bg, (w, words, d) =>
    {
        Deck(w, words, "notes", d);
        w.WriteStartArray("items");
        Note(w, words, o);
        w.WriteEndArray();
    });

    public static string Pack(PromptOptions o, bool bg) => Build(o, bg, (w, words, d) =>
    {
        w.WriteStartArray("decks");
        foreach (var kind in new[] { "notes", "deck", "test" })
        {
            w.WriteStartObject();
            w.WriteString("title", words.DeckTitle);
            w.WriteString("kind", kind);
            w.WriteStartArray("items");
            if (kind == "notes") Note(w, words, o);
            else if (kind == "deck") Flashcard(w, words, o, d);
            else foreach (var t in ItemTypes.Questions.Where(o.QuestionTypes.Contains)) Question(w, words, o, t, d);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    });

    private static string Build(PromptOptions o, bool bg, Action<Utf8JsonWriter, Words, int> body)
    {
        var words = bg ? Bg : En;
        int difficulty = o.Difficulty is >= 1 and <= 5 ? o.Difficulty : 3;
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, Options))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", 1);
            w.WriteString("subject", Or(o.Subject, words.Subject));
            w.WriteString("source", Or(o.Source, words.Source));
            w.WriteStartObject("chapter");
            w.WriteString("number", Or(o.ChapterNumber, words.Number));
            w.WriteString("title", Or(o.ChapterTitle, words.ChapterTitle));
            w.WriteString("pages", Or(o.Pages, words.Pages));
            w.WriteEndObject();
            body(w, words, difficulty);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void Deck(Utf8JsonWriter w, Words words, string kind, int difficulty)
    {
        w.WriteStartObject("deck");
        w.WriteString("title", words.DeckTitle);
        w.WriteString("kind", kind);
        w.WriteNumber("difficulty", difficulty);
        w.WriteEndObject();
    }

    private static void Common(Utf8JsonWriter w, Words words, PromptOptions o, int difficulty)
    {
        w.WriteString("explanation", words.Explanation);
        w.WriteString("page", ExamplePage(o));
        w.WriteNumber("difficulty", difficulty);
        w.WriteStartArray("tags");
        w.WriteStringValue(words.Topic);
        w.WriteEndArray();
    }

    private static void Flashcard(Utf8JsonWriter w, Words words, PromptOptions o, int d)
    {
        w.WriteStartObject();
        w.WriteString("type", "flashcard");
        w.WriteString("front", words.Front);
        w.WriteString("back", words.Back);
        w.WriteString("hint", words.Hint);
        Common(w, words, o, d);
        w.WriteEndObject();
    }

    private static void Question(Utf8JsonWriter w, Words words, PromptOptions o, string type, int d)
    {
        w.WriteStartObject();
        w.WriteString("type", type);
        if (type == ItemTypes.TrueFalse)
        {
            w.WriteString("question", words.Statement);
            w.WriteBoolean("answer", false);
        }
        else if (type == ItemTypes.Short)
        {
            w.WriteString("question", words.Question);
            w.WriteString("answer", words.ModelAnswer);
            w.WriteStartArray("accept");
            w.WriteStringValue(words.Accept);
            w.WriteEndArray();
            w.WriteStartArray("keyPoints");
            w.WriteStringValue(words.KeyPoint);
            w.WriteStringValue(words.KeyPoint);
            w.WriteEndArray();
        }
        else
        {
            w.WriteString("question", words.Question);
            w.WriteStartArray("options");
            foreach (var letter in new[] { "A", "B", "C", "D" })
            {
                if (o.ExplainOptions)
                {
                    w.WriteStartObject();
                    w.WriteString("text", string.Format(words.Option, letter));
                    w.WriteString("why", string.Format(words.Why, letter));
                    w.WriteEndObject();
                }
                else w.WriteStringValue(string.Format(words.Option, letter));
            }
            w.WriteEndArray();
            if (type == ItemTypes.Single) w.WriteString("answer", "B");
            else
            {
                w.WriteStartArray("answer");
                w.WriteStringValue("A");
                w.WriteStringValue("C");
                w.WriteEndArray();
            }
        }
        Common(w, words, o, d);
        w.WriteEndObject();
    }

    private static void Note(Utf8JsonWriter w, Words words, PromptOptions o)
    {
        w.WriteStartObject();
        w.WriteString("type", "note");
        w.WriteString("heading", words.Heading);
        w.WriteString("content", words.Content);
        w.WriteString("page", ExamplePage(o));
        w.WriteEndObject();
    }

    private static string ExamplePage(PromptOptions o)
    {
        var first = TextUtil.FirstPageNumber(o.Pages);
        return first.HasValue ? (first.Value + 2).ToString() : "52";
    }

    private static string Or(string? v, string fallback) => string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
}
