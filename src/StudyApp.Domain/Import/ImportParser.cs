using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StudyApp.Domain.Localization;
using StudyApp.Domain.Models;

namespace StudyApp.Domain.Import;

/// <summary>
/// Reads Study App JSON (schemaVersion 1) and the many variations LLMs produce.
/// Invalid items are skipped with a readable message; the rest of the file still imports.
/// </summary>
public static partial class ImportParser
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 128,
    };

    private sealed class Ctx
    {
        public int Counter;
        public required ParseResult Result;
        public void Error(int n, string msg, params object?[] args) => Result.Issues.Add(ImportIssue.Error(Format(n, msg, args), n));
        public void Warn(int n, string msg, params object?[] args) => Result.Issues.Add(ImportIssue.Warning(Format(n, msg, args), n));
        public void Info(int n, string msg, params object?[] args) => Result.Issues.Add(ImportIssue.Info(Format(n, msg, args), n));
        private static string Format(int n, string msg, object?[] args)
        {
            var all = new object?[args.Length + 1];
            all[0] = n;
            Array.Copy(args, 0, all, 1, args.Length);
            return L.F(msg, all);
        }
    }

    // ───────────────────────────────────────────────────────────── entry point

    public static ParseResult Parse(string? raw, string fileName = "")
    {
        var result = new ParseResult { FileName = fileName };
        var notes = new List<string>();
        var json = JsonExtractor.Extract(raw, notes);
        if (string.IsNullOrWhiteSpace(json))
        {
            result.Error = L.T("No JSON found. Paste the AI's whole reply, or at least the part from the first { to the last }.");
            return result;
        }

        var doc = TryParse(json, out var firstError);
        if (doc == null)
        {
            var sanitized = JsonExtractor.EscapeControlCharsInStrings(json);
            var commas = JsonExtractor.InsertMissingCommas(sanitized);
            var quotes = JsonExtractor.NormalizeQuotes(commas);
            var attempts = new (string Text, string Note)[]
            {
                (sanitized, "Fixed line breaks inside text values."),
                (commas, "Added missing commas between items."),
                (quotes, "Replaced typographic quotes with normal quotes."),
            };
            foreach (var (text, note) in attempts)
            {
                doc = TryParse(text, out _);
                if (doc != null)
                {
                    notes.Add(note);
                    result.Repaired = true;
                    break;
                }
            }

            if (doc == null)
            {
                foreach (var candidate in new[] { json, sanitized, commas, quotes })
                {
                    if (!JsonExtractor.TryCloseTruncated(candidate, out var closed)) continue;
                    doc = TryParse(closed, out _);
                    if (doc == null) continue;
                    result.Truncated = true;
                    result.Repaired = true;
                    break;
                }
            }
        }

        if (doc == null)
        {
            DescribeSyntaxError(result, json, firstError);
            return result;
        }

        var ctx = new Ctx { Result = result };
        using (doc)
        {
            result.Package = MapRoot(doc.RootElement, ctx, fileName);
        }

        var lead = notes.Select(n => ImportIssue.Info(L.T(n))).ToList();
        if (result.Truncated)
            lead.Insert(0, ImportIssue.Warning(L.T("The AI's reply was cut off. All complete items were recovered — ask the AI to continue for the rest.")));
        result.Issues.InsertRange(0, lead);

        if (result.Package.ItemCount == 0 && result.Error == null)
            result.Error = L.T("The JSON was read, but no study items were found in it.");
        else
            AddSummaryWarnings(result);

        return result;
    }

    private static JsonDocument? TryParse(string text, out JsonException? error)
    {
        try
        {
            error = null;
            return JsonDocument.Parse(text, DocOptions);
        }
        catch (JsonException ex)
        {
            error = ex;
            return null;
        }
    }

    private static void DescribeSyntaxError(ParseResult result, string json, JsonException? ex)
    {
        var lines = json.Replace("\r\n", "\n").Split('\n');
        int line = (int)(ex?.LineNumber ?? lines.Length - 1);
        line = Math.Clamp(line, 0, Math.Max(0, lines.Length - 1));
        var lineText = lines.Length > 0 ? lines[line] : "";
        int col = ByteToCharColumn(lineText, (int)(ex?.BytePositionInLine ?? 0));

        result.ErrorLine = line + 1;
        result.ErrorColumn = col + 1;
        int from = Math.Max(0, col - 60);
        int len = Math.Min(lineText.Length - from, 120);
        result.ErrorSnippet = len > 0 ? lineText.Substring(from, len).Trim() : "";

        bool nearEnd = line >= lines.Length - 3;
        if (nearEnd && LooksTruncated(json))
            result.Error = L.T("The JSON ends too early — the AI's reply was probably cut off. Ask it to continue, or to send fewer items per reply.");
        else
            result.Error = L.F("The JSON has a syntax error at line {0}, column {1}.", line + 1, col + 1);
    }

    private static bool LooksTruncated(string json)
    {
        int depth = 0;
        bool inString = false, escape = false;
        foreach (var c in json)
        {
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c is '{' or '[') depth++;
            else if (c is '}' or ']') depth--;
        }
        return inString || depth > 0;
    }

    private static int ByteToCharColumn(string line, int bytePos)
    {
        int bytes = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (bytes >= bytePos) return i;
            bytes += Encoding.UTF8.GetByteCount(line[i].ToString());
        }
        return line.Length;
    }

    private static void AddSummaryWarnings(ParseResult result)
    {
        var items = result.Package!.AllItems.ToList();
        int noPage = items.Count(i => string.IsNullOrWhiteSpace(i.Page));
        if (noPage > 0)
            result.Issues.Add(ImportIssue.Warning(L.F("{0} without a page reference.", L.N(noPage, "item", "items"))));
        int noExplanation = items.Count(i => i.Type != ItemTypes.Note && string.IsNullOrWhiteSpace(i.Explanation));
        if (noExplanation > 0)
            result.Issues.Add(ImportIssue.Info(L.F("{0} without an explanation.", L.N(noExplanation, "item", "items"))));
        if (result.Package.SchemaVersion > SupportedSchemaVersion)
            result.Issues.Add(ImportIssue.Warning(L.F("This file uses format version {0}, which is newer than this app supports. Check for updates.", result.Package.SchemaVersion)));
    }

    // ───────────────────────────────────────────────────────────── structure

    private static readonly string[] AnyItems = { "items", "entries", "cards_and_questions", "content" };
    private static readonly string[] CardItems = { "flashcards", "cards", "flashCards", "flash_cards", "terms", "glossary", "keyTerms", "key_terms", "vocabulary" };
    private static readonly string[] QuestionItems = { "questions", "quiz", "test", "tests", "exam", "mcq", "mcqs", "multipleChoice", "multiple_choice", "quizQuestions", "quiz_questions", "practiceQuestions" };
    private static readonly string[] NoteItems = { "notes", "sections", "summary", "summaries", "studyNotes", "study_notes" };

    private static ImportPackage MapRoot(JsonElement root, Ctx ctx, string fileName)
    {
        var pkg = new ImportPackage();

        if (root.ValueKind == JsonValueKind.Array)
        {
            var deck = new ImportDeck { FileName = fileName };
            ParseArray(root, deck, null, ctx);
            if (deck.Items.Count > 0) pkg.Decks.Add(deck);
            return pkg;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            ctx.Result.Error = L.T("The JSON must be an object { … } or a list [ … ] of items.");
            return pkg;
        }

        var version = Prop(root, "schemaVersion", "schema_version", "studyapp", "formatVersion", "version");
        if (version is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var ver)) pkg.SchemaVersion = ver;

        if (Prop(root, "subject", "subjectName", "subject_name", "course", "project") is JsonElement subject)
        {
            if (subject.ValueKind == JsonValueKind.Object)
            {
                pkg.Subject = Str(subject, "name", "title");
                pkg.Source = Str(subject, "source", "textbook", "book");
            }
            else pkg.Subject = AsString(subject);
        }
        if (pkg.Source.Length == 0) pkg.Source = Str(root, "source", "textbook", "book", "sourceName", "source_name");
        pkg.Language = Str(root, "language", "lang");

        int rootDifficulty = Difficulty.Parse(Str(root, "difficulty", "level"));
        var rootChapter = ParseChapterRef(root);

        if (Prop(root, "decks", "sets", "studySets", "study_sets") is { ValueKind: JsonValueKind.Array } decks)
            foreach (var d in decks.EnumerateArray())
                if (d.ValueKind == JsonValueKind.Object) AddDeck(pkg, d, rootChapter, rootDifficulty, pkg.Source, ctx, fileName, containerIsChapter: false);

        if (Prop(root, "chapters", "units", "lessons", "modules") is { ValueKind: JsonValueKind.Array } chapters)
        {
            foreach (var ch in chapters.EnumerateArray())
            {
                if (ch.ValueKind != JsonValueKind.Object) continue;
                var info = ParseChapterObject(ch) ?? rootChapter;
                if (Prop(ch, "decks", "sets") is { ValueKind: JsonValueKind.Array } chDecks)
                    foreach (var d in chDecks.EnumerateArray())
                        if (d.ValueKind == JsonValueKind.Object) AddDeck(pkg, d, info, rootDifficulty, pkg.Source, ctx, fileName, containerIsChapter: false);
                if (HasItemArrays(ch)) AddDeck(pkg, ch, info, rootDifficulty, pkg.Source, ctx, fileName, containerIsChapter: true);
            }
        }

        if (HasItemArrays(root)) AddDeck(pkg, root, rootChapter, rootDifficulty, pkg.Source, ctx, fileName, containerIsChapter: false);
        return pkg;
    }

    private static bool HasItemArrays(JsonElement obj) =>
        AnyItems.Concat(CardItems).Concat(QuestionItems).Concat(NoteItems)
            .Any(n => Prop(obj, n) is { ValueKind: JsonValueKind.Array });

    private static void AddDeck(ImportPackage pkg, JsonElement container, ImportChapter? inheritedChapter, int inheritedDifficulty,
        string inheritedSource, Ctx ctx, string fileName, bool containerIsChapter)
    {
        var deck = new ImportDeck { FileName = fileName };
        JsonElement? meta = Prop(container, "deck", "deckInfo", "deck_info", "meta", "metadata", "set");
        if (meta is not { ValueKind: JsonValueKind.Object }) meta = containerIsChapter ? null : container;

        if (meta is JsonElement m)
        {
            deck.Title = Str(m, "title", "name", "deckTitle", "deck_title", "deckName", "deck_name", "setTitle");
            deck.Kind = ParseKind(Str(m, "kind", "deckKind", "deck_kind", "deckType", "deck_type", "type"));
            deck.Description = Str(m, "description", "about", "intro");
            deck.Difficulty = Difficulty.Parse(Str(m, "difficulty", "level"));
            deck.Source = Str(m, "source", "textbook", "book");
            deck.Language = Str(m, "language", "lang");
        }
        if (deck.Difficulty == 0) deck.Difficulty = inheritedDifficulty;
        if (deck.Source.Length == 0) deck.Source = inheritedSource;
        deck.Chapter = (containerIsChapter ? null : ParseChapterRef(container)) ?? inheritedChapter;

        foreach (var name in AnyItems)
            if (Prop(container, name) is { ValueKind: JsonValueKind.Array } arr) { ParseArray(arr, deck, null, ctx); break; }
        foreach (var name in CardItems)
            if (Prop(container, name) is { ValueKind: JsonValueKind.Array } arr) { ParseArray(arr, deck, ItemTypes.Flashcard, ctx); break; }
        foreach (var name in QuestionItems)
            if (Prop(container, name) is { ValueKind: JsonValueKind.Array } arr) { ParseArray(arr, deck, "question", ctx); break; }
        foreach (var name in NoteItems)
            if (Prop(container, name) is { ValueKind: JsonValueKind.Array } arr) { ParseArray(arr, deck, ItemTypes.Note, ctx); break; }

        if (deck.Items.Count > 0) pkg.Decks.Add(deck);
    }

    private static DeckKind? ParseKind(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var k = new string(raw.ToLowerInvariant().Where(char.IsLetter).ToArray());
        return k switch
        {
            "deck" or "flashcards" or "flashcard" or "cards" or "card" or "glossary" or "terms" => DeckKind.Deck,
            "test" or "quiz" or "exam" or "questions" or "mcq" or "practicetest" or "mixedtest" => DeckKind.Test,
            "notes" or "note" or "summary" or "reading" or "studynotes" => DeckKind.Notes,
            _ => null,
        };
    }

    private static ImportChapter? ParseChapterRef(JsonElement container)
    {
        var ch = Prop(container, "chapter", "chapterInfo", "chapter_info", "unit", "lesson");
        if (ch is { ValueKind: JsonValueKind.Object } obj) return ParseChapterObject(obj);

        var info = new ImportChapter();
        if (ch is JsonElement el)
        {
            var text = AsString(el);
            var m = ChapterTextRegex().Match(text);
            if (m.Success && m.Groups["num"].Success)
            {
                info.Number = m.Groups["num"].Value;
                info.Title = m.Groups["title"].Value.Trim();
            }
            else info.Title = text;
        }

        var number = Str(container, "chapterNumber", "chapter_number", "chapterNo");
        var title = Str(container, "chapterTitle", "chapter_title", "chapterName");
        if (number.Length > 0) info.Number = number;
        if (title.Length > 0) info.Title = title;
        var pages = PageStr(container, "pages", "pageRange", "page_range", "chapterPages", "chapter_pages");
        if (!info.IsEmpty && pages.Length > 0) info.Pages = pages;
        return info.IsEmpty ? null : info;
    }

    private static ImportChapter? ParseChapterObject(JsonElement obj)
    {
        var info = new ImportChapter
        {
            Number = Str(obj, "number", "no", "num", "chapterNumber", "chapter_number", "index"),
            Title = Str(obj, "title", "name", "chapterTitle", "chapter_title", "heading"),
            Pages = PageStr(obj, "pages", "pageRange", "page_range", "page"),
        };
        return info.IsEmpty ? null : info;
    }

    private static void ParseArray(JsonElement array, ImportDeck deck, string? defaultType, Ctx ctx)
    {
        foreach (var el in array.EnumerateArray())
        {
            int n = ++ctx.Counter;
            var item = ParseItem(el, defaultType, deck, n, ctx);
            if (item == null) continue;
            item.SortOrder = deck.Items.Count;
            item.Fingerprint = TextUtil.Fingerprint(item.Type, item.Prompt);
            deck.Items.Add(item);
        }
    }

    // ───────────────────────────────────────────────────────────── items

    private static Item? ParseItem(JsonElement el, string? defaultType, ImportDeck deck, int n, Ctx ctx)
    {
        if (el.ValueKind == JsonValueKind.String && defaultType == ItemTypes.Note)
        {
            var text = TextUtil.Clean(el.GetString());
            return text.Length == 0 ? null : new Item { Type = ItemTypes.Note, Answer = text, Difficulty = deck.Difficulty, Source = deck.Source };
        }
        if (el.ValueKind != JsonValueKind.Object)
        {
            ctx.Warn(n, "Item {0}: is not an object {{ … }} – skipped.");
            return null;
        }

        var typeRaw = Str(el, "type", "kind", "itemType", "item_type", "questionType", "question_type", "format");
        var type = ItemTypes.Normalize(typeRaw);
        if (type == null)
        {
            var inferred = defaultType is null or "question" ? Infer(el, defaultType) : defaultType;
            if (typeRaw.Length > 0)
                ctx.Warn(n, "Item {0}: unknown type \"{1}\" – imported as {2}.", typeRaw, ItemTypes.DisplayName(inferred));
            type = inferred;
        }

        var item = new Item
        {
            Type = type,
            Explanation = Clean(StrOnly(el, "explanation", "explain", "rationale", "reasoning", "reason", "feedback", "solutionExplanation", "details", "why", "comment")),
            Page = PageStr(el, "page", "pages", "pageRef", "page_ref", "pageReference", "page_reference", "pageNumber", "page_number", "pageNumbers", "ref", "reference", "sourcePage", "source_page", "location", "p"),
            Difficulty = Difficulty.Parse(Str(el, "difficulty", "level", "diff", "difficultyLevel", "difficulty_level")),
            Tags = StrList(el, "tags", "tag", "topics", "topic", "keywords", "category", "categories", "concepts"),
            Source = Str(el, "source", "textbook", "book"),
        };
        if (item.Difficulty == 0) item.Difficulty = deck.Difficulty;
        if (item.Source.Length == 0) item.Source = deck.Source;
        var hint = Clean(Str(el, "hint", "clue", "tip", "mnemonic"));
        if (hint.Length > 0) item.Data.Hint = hint;

        bool ok = type switch
        {
            ItemTypes.Flashcard => ParseFlashcard(el, item, n, ctx),
            ItemTypes.Single or ItemTypes.Multiple => ParseChoice(el, item, n, ctx),
            ItemTypes.TrueFalse => ParseTrueFalse(el, item, n, ctx),
            ItemTypes.Short => ParseShort(el, item, n, ctx),
            ItemTypes.Note => ParseNote(el, item, n, ctx),
            _ => false,
        };
        return ok ? item : null;
    }

    private static string Infer(JsonElement el, string? hint)
    {
        if (Prop(el, "options", "choices", "alternatives", "variants", "answerOptions", "answer_options") is { ValueKind: JsonValueKind.Array or JsonValueKind.Object })
            return ItemTypes.Single;
        var answer = Prop(el, "answer", "correct", "correctAnswer", "correct_answer", "isTrue", "truth");
        if (answer is { ValueKind: JsonValueKind.True or JsonValueKind.False } || Prop(el, "statement") != null)
            return ItemTypes.TrueFalse;
        if (Prop(el, "heading", "content", "body", "markdown") != null && Prop(el, "front", "question", "back") == null)
            return ItemTypes.Note;
        if (Prop(el, "front", "back", "term", "definition") != null)
            return ItemTypes.Flashcard;
        if (Prop(el, "keyPoints", "key_points", "rubric", "accept", "acceptable", "modelAnswer", "model_answer") != null)
            return ItemTypes.Short;
        return hint == "question" ? ItemTypes.Short : ItemTypes.Flashcard;
    }

    private static bool ParseFlashcard(JsonElement el, Item item, int n, Ctx ctx)
    {
        item.Prompt = Clean(Str(el, "front", "question", "q", "term", "prompt", "word", "concept", "cue", "title", "text", "side1"));
        item.Answer = Clean(Str(el, "back", "answer", "a", "definition", "meaning", "response", "def", "side2", "answers"));
        if (item.Prompt.Length == 0) { ctx.Error(n, "Item {0}: missing front side – skipped."); return false; }
        if (item.Answer.Length == 0) { ctx.Error(n, "Item {0}: missing back side – skipped."); return false; }
        return true;
    }

    private static bool ParseChoice(JsonElement el, Item item, int n, Ctx ctx)
    {
        item.Prompt = Clean(Str(el, "question", "prompt", "q", "text", "stem", "title", "statement", "front"));
        if (item.Prompt.Length == 0) { ctx.Error(n, "Item {0}: missing question text – skipped."); return false; }

        JsonElement? optsEl = Prop(el, "options", "choices", "alternatives", "variants", "answerOptions", "answer_options", "possibleAnswers", "possible_answers");
        if (optsEl == null && Prop(el, "answers") is { ValueKind: JsonValueKind.Array } answersArr && answersArr.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.Object))
            optsEl = answersArr;

        var options = new List<ChoiceOption>();
        var flags = new List<int>();
        if (optsEl is { ValueKind: JsonValueKind.Array } arr)
        {
            foreach (var o in arr.EnumerateArray())
            {
                var (opt, correct) = ParseOption(o);
                if (opt == null) continue;
                if (correct == true) flags.Add(options.Count);
                options.Add(opt);
            }
        }
        else if (optsEl is { ValueKind: JsonValueKind.Object } map)
        {
            foreach (var p in map.EnumerateObject().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var (opt, correct) = ParseOption(p.Value);
                if (opt == null) continue;
                if (correct == true) flags.Add(options.Count);
                options.Add(opt);
            }
        }

        if (options.Count < 2) { ctx.Error(n, "Item {0}: needs at least 2 answer options – skipped."); return false; }

        // Per-option explanations given as a parallel list.
        if (Prop(el, "optionExplanations", "option_explanations", "explanations", "whys", "rationales", "optionFeedback") is { ValueKind: JsonValueKind.Array } whys)
        {
            int i = 0;
            foreach (var w in whys.EnumerateArray())
            {
                if (i < options.Count && string.IsNullOrWhiteSpace(options[i].Why)) options[i].Why = Clean(AsString(w));
                i++;
            }
        }

        List<int> correctIdx = flags;
        if (correctIdx.Count == 0)
        {
            var (ansEl, ansName) = PropWithName(el, "answer", "answers", "correct", "correctAnswer", "correct_answer", "correctAnswers", "correct_answers",
                "correctOption", "correct_option", "correctOptions", "correct_options", "correctIndex", "correct_index", "correctIndices", "correct_indices",
                "answerIndex", "answer_index", "solution", "key", "answerKey", "answer_key", "right");
            if (ansEl == null) { ctx.Error(n, "Item {0}: missing correct answer – skipped."); return false; }

            var refs = ParseAnswerRefs(ansEl.Value, options, ansName.Contains("index", StringComparison.OrdinalIgnoreCase), out var bad, out var numericGuess);
            if (bad != null) { ctx.Error(n, "Item {0}: correct answer \"{1}\" does not match any option – skipped.", bad); return false; }
            if (refs.Count == 0) { ctx.Error(n, "Item {0}: missing correct answer – skipped."); return false; }
            if (numericGuess) ctx.Info(n, "Item {0}: the answer is a number; it was read as option {1}. Please check it.", string.Join(", ", refs.Select(r => ((char)('A' + r)).ToString())));
            correctIdx = refs;
        }

        correctIdx = correctIdx.Distinct().OrderBy(i => i).ToList();
        if (item.Type == ItemTypes.Single && correctIdx.Count > 1)
        {
            item.Type = ItemTypes.Multiple;
            ctx.Info(n, "Item {0}: has several correct answers – imported as multiple choice.");
        }

        item.Data.Options = options;
        item.Data.Correct = correctIdx;
        return true;
    }

    private static (ChoiceOption? Option, bool? Correct) ParseOption(JsonElement o)
    {
        switch (o.ValueKind)
        {
            case JsonValueKind.String:
                var s = TextUtil.StripOptionPrefix(Clean(o.GetString()));
                return s.Length == 0 ? (null, null) : (new ChoiceOption(s), null);
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return (new ChoiceOption(o.GetRawText()), null);
            case JsonValueKind.Object:
                var text = TextUtil.StripOptionPrefix(Clean(Str(o, "text", "option", "label", "value", "answer", "content", "choice", "title")));
                if (text.Length == 0) return (null, null);
                bool? correct = null;
                if (Prop(o, "correct", "isCorrect", "is_correct", "right", "isAnswer", "is_answer", "isRight") is JsonElement c)
                    correct = ParseBool(c);
                var why = Clean(Str(o, "why", "explanation", "reason", "feedback", "rationale", "comment"));
                return (new ChoiceOption(text, why.Length > 0 ? why : null), correct);
            default:
                return (null, null);
        }
    }

    private static List<int> ParseAnswerRefs(JsonElement el, List<ChoiceOption> options, bool nameSaysIndex, out string? bad, out bool numericGuess)
    {
        bad = null;
        numericGuess = false;
        var result = new List<int>();
        var numbers = new List<int>();
        string? badLocal = null;

        void HandleString(string raw)
        {
            var s = raw.Trim();
            if (s.Length == 0) return;
            var letters = LetterListRegex().Match(s);
            if (letters.Success)
            {
                foreach (Capture cap in letters.Groups["l"].Captures) result.Add(LetterIndex(cap.Value[0]));
                return;
            }
            if (int.TryParse(s, out var num)) { numbers.Add(num); return; }

            // Match the option text itself (before letter prefixes, so "A cell" is not read as "A").
            var norm = TextUtil.NormalizeForMatch(s);
            int idx = options.FindIndex(o => TextUtil.NormalizeForMatch(o.Text) == norm);
            if (idx < 0)
            {
                var lead = LeadingLetterRegex().Match(s);
                if (lead.Success)
                {
                    result.Add(LetterIndex(lead.Groups["l"].Value[0]));
                    return;
                }
                var stripped = TextUtil.NormalizeForMatch(TextUtil.StripOptionPrefix(s));
                idx = options.FindIndex(o => TextUtil.NormalizeForMatch(o.Text) == stripped);
            }
            if (idx < 0 && norm.Length > 2)
            {
                var candidates = options.Select((o, i) => (o, i))
                    .Where(t => { var on = TextUtil.NormalizeForMatch(t.o.Text); return on.Length > 2 && (on.Contains(norm) || norm.Contains(on)); })
                    .ToList();
                if (candidates.Count == 1) idx = candidates[0].i;
            }
            if (idx >= 0) result.Add(idx);
            else badLocal = raw;
        }

        void Visit(JsonElement v)
        {
            switch (v.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var x in v.EnumerateArray()) Visit(x);
                    break;
                case JsonValueKind.Number:
                    if (v.TryGetInt32(out var num)) numbers.Add(num);
                    break;
                case JsonValueKind.String:
                    HandleString(v.GetString() ?? "");
                    break;
                case JsonValueKind.Object:
                    var text = Str(v, "text", "option", "answer", "value", "letter");
                    if (text.Length > 0) HandleString(text);
                    break;
            }
        }
        Visit(el);

        if (numbers.Count > 0)
        {
            bool oneBased = !nameSaysIndex && !numbers.Contains(0) && numbers.Max() == options.Count;
            bool zeroBased = nameSaysIndex || numbers.Contains(0);
            if (!oneBased && !zeroBased) numericGuess = true;
            foreach (var num in numbers) result.Add(oneBased ? num - 1 : num);
        }

        foreach (var r in result)
            if (r < 0 || r >= options.Count)
            {
                bad = r < 0 ? "?" : ((char)('A' + Math.Min(r, 25))).ToString();
                return new();
            }
        if (badLocal != null) bad = badLocal;
        return result;
    }

    private static int LetterIndex(char c)
    {
        var u = char.ToUpperInvariant(c);
        if (u is >= 'A' and <= 'Z') return u - 'A';
        // Bulgarian option letters: А Б В Г Д Е Ж З
        const string cyr = "АБВГДЕЖЗ";
        int i = cyr.IndexOf(u);
        return i >= 0 ? i : -1;
    }

    private static bool ParseTrueFalse(JsonElement el, Item item, int n, Ctx ctx)
    {
        item.Prompt = Clean(Str(el, "statement", "question", "prompt", "q", "text", "claim", "front"));
        if (item.Prompt.Length == 0) { ctx.Error(n, "Item {0}: missing statement – skipped."); return false; }

        bool? truth = null;
        if (Prop(el, "answer", "correct", "truth", "isTrue", "is_true", "value", "correctAnswer", "correct_answer", "solution", "true") is JsonElement a)
        {
            truth = ParseBool(a);
            if (truth == null && a.ValueKind == JsonValueKind.String)
            {
                // "A"/"B" against options ["True","False"]
                var letter = LeadingLetterRegex().Match(a.GetString() ?? "");
                if (letter.Success && Prop(el, "options", "choices") is { ValueKind: JsonValueKind.Array } opts)
                {
                    var idx = LetterIndex(letter.Groups["l"].Value[0]);
                    var list = opts.EnumerateArray().Select(AsString).ToList();
                    if (idx >= 0 && idx < list.Count) truth = ParseBoolText(list[idx]);
                }
            }
        }
        if (truth == null) { ctx.Error(n, "Item {0}: the answer must be true or false – skipped."); return false; }
        item.Data.Truth = truth;
        return true;
    }

    private static bool ParseShort(JsonElement el, Item item, int n, Ctx ctx)
    {
        item.Prompt = Clean(Str(el, "question", "prompt", "q", "text", "stem", "task", "front"));
        if (item.Prompt.Length == 0) { ctx.Error(n, "Item {0}: missing question text – skipped."); return false; }

        var accept = StrList(el, "accept", "accepted", "acceptable", "acceptableAnswers", "acceptable_answers", "alternatives", "alternativeAnswers",
            "alternative_answers", "synonyms", "acceptedAnswers", "accepted_answers", "otherAnswers", "other_answers");
        var answerEl = Prop(el, "answer", "modelAnswer", "model_answer", "sampleAnswer", "sample_answer", "expectedAnswer", "expected_answer",
            "expected", "correctAnswer", "correct_answer", "solution", "back", "idealAnswer", "ideal_answer", "answers");
        string answer = "";
        if (answerEl is { ValueKind: JsonValueKind.Array } arr)
        {
            var all = arr.EnumerateArray().Select(AsString).Where(s => s.Length > 0).ToList();
            if (all.Count > 0)
            {
                answer = all[0];
                accept.InsertRange(0, all.Skip(1));
            }
        }
        else if (answerEl is JsonElement a) answer = AsString(a);

        var keyPoints = StrList(el, "keyPoints", "key_points", "points", "rubric", "criteria", "mustInclude", "must_include", "keyIdeas", "key_ideas", "checklist");
        if (answer.Length == 0 && keyPoints.Count > 0) answer = string.Join("\n", keyPoints.Select(p => "- " + p));
        if (answer.Length == 0) { ctx.Error(n, "Item {0}: missing model answer – skipped."); return false; }

        item.Answer = Clean(answer);
        if (accept.Count > 0) item.Data.Accept = accept.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keyPoints.Count > 0) item.Data.KeyPoints = keyPoints;
        return true;
    }

    private static bool ParseNote(JsonElement el, Item item, int n, Ctx ctx)
    {
        item.Prompt = Clean(Str(el, "heading", "title", "section", "name", "topic", "header", "subheading"));
        var bodyEl = Prop(el, "content", "body", "text", "markdown", "summary", "notes", "points", "bullets");
        string body = "";
        if (bodyEl is { ValueKind: JsonValueKind.Array } list)
            body = string.Join("\n", list.EnumerateArray().Select(AsString).Where(s => s.Length > 0).Select(s => s.StartsWith("- ") ? s : "- " + s));
        else if (bodyEl is JsonElement b) body = AsString(b);
        body = Clean(FixDoubleEscapes(body));

        var sb = new StringBuilder(body);
        if (Prop(el, "keyTerms", "key_terms", "terms", "glossary") is { ValueKind: JsonValueKind.Array } terms)
        {
            var lines = new List<string>();
            foreach (var t in terms.EnumerateArray())
            {
                if (t.ValueKind == JsonValueKind.Object)
                {
                    var term = Str(t, "term", "name", "word");
                    var def = Str(t, "definition", "meaning", "description");
                    if (term.Length > 0) lines.Add(def.Length > 0 ? $"- **{term}** — {def}" : $"- **{term}**");
                }
                else if (AsString(t) is { Length: > 0 } s) lines.Add("- " + s);
            }
            if (lines.Count > 0) sb.Append("\n\n**").Append(L.T("Key terms")).Append("**\n").Append(string.Join("\n", lines));
        }
        var keyPoints = StrList(el, "keyPoints", "key_points", "takeaways", "key_takeaways");
        if (keyPoints.Count > 0)
            sb.Append("\n\n**").Append(L.T("Key points")).Append("**\n").Append(string.Join("\n", keyPoints.Select(p => "- " + p)));

        item.Answer = TextUtil.Clean(sb.ToString());
        if (item.Answer.Length == 0) { ctx.Error(n, "Item {0}: the note has no content – skipped."); return false; }
        return true;
    }

    // ───────────────────────────────────────────────────────────── JSON helpers

    private static string NormKey(string s)
    {
        Span<char> buf = stackalloc char[Math.Min(s.Length, 64)];
        int len = 0;
        foreach (var ch in s)
        {
            if (len >= buf.Length) break;
            if (char.IsLetterOrDigit(ch)) buf[len++] = char.ToLowerInvariant(ch);
        }
        return new string(buf[..len]);
    }

    private static JsonElement? Prop(JsonElement obj, params string[] names) => PropWithName(obj, names).Value;

    private static (JsonElement? Value, string Name) PropWithName(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return (null, "");
        foreach (var name in names)
        {
            var nk = NormKey(name);
            foreach (var p in obj.EnumerateObject())
                if (NormKey(p.Name) == nk && p.Value.ValueKind != JsonValueKind.Null) return (p.Value, p.Name);
        }
        return (null, "");
    }

    private static string AsString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString()?.Trim() ?? "",
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => string.Join("\n", el.EnumerateArray().Select(AsString).Where(s => s.Length > 0)),
        JsonValueKind.Object => Str(el, "text", "value", "content"),
        _ => "",
    };

    private static string Str(JsonElement obj, params string[] names) => Prop(obj, names) is JsonElement v ? AsString(v) : "";

    /// <summary>Like <see cref="Str"/> but ignores arrays/objects (e.g. a per-option "why" list).</summary>
    private static string StrOnly(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
            if (Prop(obj, name) is { ValueKind: JsonValueKind.String or JsonValueKind.Number } v) return AsString(v);
        return "";
    }

    private static string PageStr(JsonElement obj, params string[] names)
    {
        if (Prop(obj, names) is not JsonElement v) return "";
        if (v.ValueKind == JsonValueKind.Array)
            return string.Join(", ", v.EnumerateArray().Select(AsString).Where(s => s.Length > 0));
        if (v.ValueKind == JsonValueKind.Object)
        {
            var from = Str(v, "from", "start");
            var to = Str(v, "to", "end");
            return from.Length > 0 && to.Length > 0 ? $"{from}-{to}" : from + to;
        }
        return AsString(v);
    }

    private static List<string> StrList(JsonElement obj, params string[] names)
    {
        if (Prop(obj, names) is not JsonElement v) return new();
        if (v.ValueKind == JsonValueKind.Array)
            return v.EnumerateArray().Select(AsString).Select(Clean).Where(s => s.Length > 0).ToList();
        if (v.ValueKind == JsonValueKind.String)
            return TextUtil.SplitTags(v.GetString());
        return new();
    }

    private static bool? ParseBool(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => el.TryGetInt32(out var i) ? i != 0 : null,
        JsonValueKind.String => ParseBoolText(el.GetString()),
        _ => null,
    };

    private static bool? ParseBoolText(string? raw)
    {
        var s = TextUtil.NormalizeForMatch(raw);
        return s switch
        {
            "true" or "t" or "yes" or "y" or "correct" or "right" or "1" or "вярно" or "истина" or "да" or "правилно" or "вярно е" => true,
            "false" or "f" or "no" or "n" or "incorrect" or "wrong" or "0" or "невярно" or "грешно" or "лъжа" or "не" or "неправилно" or "не е вярно" => false,
            _ => null,
        };
    }

    private static string Clean(string? s) => TextUtil.Clean(s);

    /// <summary>Turns a literal "\n" typed by the AI (double-escaped) into a real line break.</summary>
    private static string FixDoubleEscapes(string s)
    {
        if (s.Contains('\n') || !s.Contains("\\n")) return s;
        return s.Replace("\\n", "\n");
    }

    [GeneratedRegex(@"^(?:chapter|ch\.?|глава|гл\.?|unit|lesson|тема)?\s*(?<num>\d+(?:\.\d+)*)\s*[:.\-–—)]?\s*(?<title>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterTextRegex();

    // "A", "b", "A, C", "A and C", "A & C", "A/C", "Б, Г"
    [GeneratedRegex(@"^\(?(?<l>[A-Ha-hА-За-з])\)?(?:\s*(?:,|;|/|&|\+|and|и)\s*\(?(?<l>[A-Ha-hА-За-з])\)?)*\.?$")]
    private static partial Regex LetterListRegex();

    // "B) Mitochondria", "Option C", "Answer: D", "(A)"
    [GeneratedRegex(@"^(?:option|answer|correct answer|отговор|вариант)?\s*:?\s*\(?(?<l>[A-Ha-hА-За-з])(?:\)|\.|:|\s*[-–—]|$)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingLetterRegex();
}
