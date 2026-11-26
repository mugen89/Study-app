using System.Text.Json;
using System.Text.RegularExpressions;
using StudyApp.Domain;
using StudyApp.Domain.Export;
using StudyApp.Domain.Grading;
using StudyApp.Domain.Import;
using StudyApp.Domain.Localization;
using StudyApp.Domain.Models;
using StudyApp.Domain.Prompts;
using StudyApp.Domain.Samples;
using StudyApp.Domain.Srs;

namespace StudyApp.Tests;

public class Sm2Tests
{
    private static readonly SrsSettings Settings = new();
    private static readonly long Now = Time.ToMs(new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Local));

    [Fact]
    public void NewCardGoesThroughLearningStepsThenGraduates()
    {
        var item = new Item();
        var u1 = Sm2.Next(item, Rating.Good, Now, Settings);
        Assert.Equal(SrsState.Learning, u1.State);
        Assert.Equal(1, u1.Step);
        Assert.Equal(Now + 10 * 60_000, u1.DueMs);

        u1.ApplyTo(item);
        var u2 = Sm2.Next(item, Rating.Good, Now, Settings);
        Assert.Equal(SrsState.Review, u2.State);
        Assert.Equal(1, u2.IntervalDays);
    }

    [Fact]
    public void EasyGraduatesImmediately()
    {
        var u = Sm2.Next(new Item(), Rating.Easy, Now, Settings);
        Assert.Equal(SrsState.Review, u.State);
        Assert.Equal(4, u.IntervalDays);
    }

    [Fact]
    public void ReviewIntervalsGrowAndLapsesShrinkEase()
    {
        var item = new Item { SrsState = SrsState.Review, SrsInterval = 10, SrsEase = 2.5, SrsDue = Now };
        var good = Sm2.Next(item, Rating.Good, Now, Settings);
        Assert.Equal(25, good.IntervalDays);
        var easy = Sm2.Next(item, Rating.Easy, Now, Settings);
        Assert.True(easy.IntervalDays > good.IntervalDays);
        var hard = Sm2.Next(item, Rating.Hard, Now, Settings);
        Assert.True(hard.IntervalDays < good.IntervalDays);
        Assert.True(hard.Ease < 2.5);

        var again = Sm2.Next(item, Rating.Again, Now, Settings);
        Assert.Equal(SrsState.Relearning, again.State);
        Assert.Equal(1, again.Lapses);
        Assert.Equal(2.3, again.Ease, 3);
    }

    [Fact]
    public void IntervalsAreCappedAndEaseHasAFloor()
    {
        var item = new Item { SrsState = SrsState.Review, SrsInterval = 300, SrsEase = 1.3, SrsDue = Now };
        var good = Sm2.Next(item, Rating.Good, Now, Settings);
        Assert.Equal(Settings.MaxIntervalDays, good.IntervalDays);
        var again = Sm2.Next(item, Rating.Again, Now, Settings);
        Assert.Equal(Settings.MinEase, again.Ease);
    }

    [Theory]
    [InlineData("1 10", new[] { 1, 10 })]
    [InlineData("5m, 30m, 120", new[] { 5, 30, 120 })]
    [InlineData("", new[] { 1, 10 })]
    [InlineData("abc", new[] { 1, 10 })]
    public void LearningStepsParse(string text, int[] expected) =>
        Assert.Equal(expected, SrsSettings.ParseSteps(text, new[] { 1, 10 }));
}

public class GraderTests
{
    [Fact]
    public void ChoiceGrading()
    {
        Assert.Equal(Result.GotIt, Grader.GradeChoice(new[] { 1, 3 }, new[] { 3, 1 }));
        Assert.Equal(Result.Partial, Grader.GradeChoice(new[] { 1 }, new[] { 1, 3 }));
        Assert.Equal(Result.Missed, Grader.GradeChoice(new[] { 1, 2 }, new[] { 1, 3 }));
        Assert.Equal(Result.Missed, Grader.GradeChoice(Array.Empty<int>(), new[] { 1 }));
    }

    [Theory]
    [InlineData("Systole", Strictness.Normal, Result.GotIt)]
    [InlineData("  systole. ", Strictness.Normal, Result.GotIt)]
    [InlineData("sistole", Strictness.Lenient, Result.GotIt)]
    [InlineData("diastole", Strictness.Normal, Result.Missed)]
    [InlineData("systole", Strictness.Exact, Result.Missed)]
    [InlineData("Систола", Strictness.Normal, Result.GotIt)]
    public void ShortAnswerChecking(string given, Strictness strictness, Result expected)
    {
        var accepted = given.Any(c => c >= 'А' && c <= 'я') ? new[] { "систола" } : new[] { "Systole" };
        Assert.Equal(expected, Grader.CheckShort(given, accepted, strictness).Suggested);
    }

    [Fact]
    public void LongModelAnswerMatchesByKeyWords()
    {
        var model = "Because the left ventricle pumps blood through the whole body which needs higher pressure";
        var check = Grader.CheckShort("the left ventricle must pump blood through the whole body so it needs a higher pressure", new[] { model }, Strictness.Normal);
        Assert.Equal(Result.GotIt, check.Suggested);
    }

    [Fact]
    public void SelfGradeWins()
    {
        var item = new Item { Type = ItemTypes.Short, Answer = "x" };
        Assert.Equal(Result.Partial, Grader.Grade(item, new Answer { Text = "y", SelfGrade = Result.Partial }, Strictness.Normal));
    }
}

public class PromptTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("bg")]
    public void EveryBuiltInTemplateRendersWithoutLeftoverPlaceholders(string lang)
    {
        var options = new PromptOptions { Subject = "Anatomy", Source = "Gray's", ChapterNumber = "3", ChapterTitle = "Bones", Pages = "45-78", Count = 15, Difficulty = 4, Language = "bg" };
        foreach (var t in PromptDefaults.All(lang))
        {
            var text = PromptRenderer.Render(t.Body, options, lang);
            Assert.DoesNotContain("{{", text);
            Assert.True(text.Length > 200, t.Key);
        }
    }

    [Fact]
    public void EmptyOptionalFieldsRemoveTheirLines()
    {
        var text = PromptRenderer.Render("A\nFocus: {{focus}}\nB {{count}}", new PromptOptions { Count = 5 }, "en");
        Assert.Equal("A\nB 5", text);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("bg")]
    public void SchemaExamplesInPromptsAreImportable(string lang)
    {
        var o = new PromptOptions();
        foreach (var json in new[] { SchemaBuilder.Flashcards(o, lang == "bg"), SchemaBuilder.Test(o, lang == "bg", ItemTypes.Questions.ToList()), SchemaBuilder.Notes(o, lang == "bg"), SchemaBuilder.Pack(o, lang == "bg") })
        {
            using var _ = JsonDocument.Parse(json);
            var result = ImportParser.Parse(json);
            Assert.True(result.Success, result.Error);
            Assert.DoesNotContain(result.Issues, i => i.Level == IssueLevel.Error);
        }
    }

    [Fact]
    public void WeakItemsListIncludesPagesAndAnswers()
    {
        var item = new Item { Type = ItemTypes.Flashcard, Prompt = "What is X?", Answer = "Y", Page = "52", SeenCount = 4, CorrectCount = 1 };
        var text = PromptText.WeakItems(new[] { item }, bg: false);
        Assert.Contains("What is X?", text);
        Assert.Contains("p. 52", text);
        Assert.Contains("missed 3 of 4", text);
    }
}

public class ExportTests
{
    [Fact]
    public void ExportedJsonImportsBackIdentically()
    {
        var original = ImportParser.Parse(SampleContent.Json("en")).Package!;
        var decks = original.Decks.Select((d, i) => new ExportDeck
        {
            Deck = new Deck { Title = d.Title, Kind = d.EffectiveKind, Difficulty = d.Difficulty },
            Chapter = d.Chapter == null ? null : new Chapter { Number = d.Chapter.Number, Title = d.Chapter.Title, Pages = d.Chapter.Pages },
            Items = d.Items,
        }).ToList();
        var json = JsonExporter.Export(new Subject { Name = original.Subject, Source = original.Source }, decks, "test");
        var again = ImportParser.Parse(json);
        Assert.True(again.Success, again.Error);
        Assert.Equal(original.ItemCount, again.Package!.ItemCount);
        var a = original.AllItems.ToList();
        var b = again.Package.AllItems.ToList();
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Type, b[i].Type);
            Assert.Equal(a[i].Prompt, b[i].Prompt);
            Assert.Equal(a[i].Answer, b[i].Answer);
            Assert.Equal(a[i].Page, b[i].Page);
            Assert.Equal(a[i].Data.Correct, b[i].Data.Correct);
            Assert.Equal(a[i].Data.Truth, b[i].Data.Truth);
            Assert.Equal(a[i].Data.Options?.Select(o => o.Text), b[i].Data.Options?.Select(o => o.Text));
        }
    }
}

public class TextTests
{
    [Theory]
    [InlineData("pp. 45–48", 45)]
    [InlineData("52-53", 52)]
    [InlineData("§3.2", 3)]
    [InlineData("", null)]
    public void FirstPageNumber(string page, int? expected) => Assert.Equal(expected, TextUtil.FirstPageNumber(page));

    [Fact]
    public void FingerprintIgnoresCaseAndPunctuation() =>
        Assert.Equal(TextUtil.Fingerprint("flashcard", "What is a cell?"), TextUtil.Fingerprint("flashcard", "  what IS a cell "));

    [Fact]
    public void FingerprintSeparatesCardsFromQuestions() =>
        Assert.NotEqual(TextUtil.Fingerprint("flashcard", "Q"), TextUtil.Fingerprint("single", "Q"));
}

public class LocalizationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StudyApp.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found");
    }

    /// <summary>Every English UI string in the source code must have a Bulgarian translation.</summary>
    [Fact]
    public void EveryUiStringHasABulgarianTranslation()
    {
        var root = RepoRoot();
        var patterns = new[]
        {
            new Regex(@"(?<![\w])(?:L\.)?[TF]\(\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled),
            new Regex(@"ctx\.(?:Error|Warn|Info)\(\s*n\s*,\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled),
            new Regex(@"notes\.Add\(\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled),
        };
        var missing = new SortedSet<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (file.EndsWith("Bulgarian.Strings.cs") || file.Contains("StudyApp.Updater")) continue;
            var text = File.ReadAllText(file);
            foreach (var p in patterns)
                foreach (Match m in p.Matches(text))
                {
                    var key = Regex.Unescape(m.Groups[1].Value);
                    if (!Bulgarian.Strings.ContainsKey(key)) missing.Add($"{Path.GetFileName(file)}: {key}");
                }
        }
        Assert.True(missing.Count == 0, "Missing Bulgarian translations:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void TranslationsKeepFormatPlaceholders()
    {
        var placeholder = new Regex(@"\{(\d+)(?::[^}]*)?\}");
        foreach (var (en, bg) in Bulgarian.Strings)
        {
            var a = placeholder.Matches(en).Select(m => m.Groups[1].Value).OrderBy(x => x).ToList();
            var b = placeholder.Matches(bg).Select(m => m.Groups[1].Value).OrderBy(x => x).ToList();
            Assert.True(a.SequenceEqual(b), $"Placeholders differ: \"{en}\" → \"{bg}\"");
        }
    }

    [Fact]
    public void BulgarianPluralsUseCountForms()
    {
        Assert.Equal("1 въпрос", BgN(1, "question", "questions"));
        Assert.Equal("5 въпроса", BgN(5, "question", "questions"));
        Assert.Equal("2 карти", BgN(2, "card", "cards"));
    }

    private static string BgN(long n, string one, string many)
    {
        var forms = Bulgarian.Plurals[one];
        return n + " " + (n == 1 ? forms.One : forms.Many);
    }
}
