using StudyApp.Domain.Import;
using StudyApp.Domain.Models;
using StudyApp.Domain.Samples;

namespace StudyApp.Tests;

public class ImportParserTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("bg")]
    public void SampleFilesImportCompletely(string lang)
    {
        var result = ImportParser.Parse(SampleContent.Json(lang));
        Assert.True(result.Success, result.Error);
        Assert.Equal(17, result.Package!.ItemCount);
        Assert.Equal(3, result.Package.Decks.Count);
        Assert.DoesNotContain(result.Issues, i => i.Level == IssueLevel.Error);
        var counts = result.Package.CountsByType();
        Assert.Equal(6, counts[ItemTypes.Flashcard]);
        Assert.Equal(3, counts[ItemTypes.Note]);
        Assert.Equal(2, counts[ItemTypes.TrueFalse]);
        Assert.Equal(DeckKind.Notes, result.Package.Decks[0].EffectiveKind);
        Assert.All(result.Package.AllItems, i => Assert.False(string.IsNullOrWhiteSpace(i.Page)));
    }

    [Fact]
    public void FindsJsonInsideChatReplyWithFences()
    {
        var reply = """
            Sure! Here are your flashcards:

            ```json
            { "items": [ { "type": "flashcard", "front": "Q1", "back": "A1", "page": "3" } ] }
            ```

            Let me know if you need more.
            """;
        var result = ImportParser.Parse(reply);
        Assert.True(result.Success);
        Assert.Single(result.Package!.AllItems);
    }

    [Fact]
    public void ToleratesTrailingCommasAndComments()
    {
        var json = """
            {
              // flashcards for chapter 1
              "flashcards": [
                { "front": "A", "back": "B", },
              ],
            }
            """;
        var result = ImportParser.Parse(json);
        Assert.True(result.Success, result.Error);
        Assert.Equal(ItemTypes.Flashcard, result.Package!.AllItems.Single().Type);
    }

    [Fact]
    public void RepairsRawLineBreaksInsideStrings()
    {
        var json = "{ \"items\": [ { \"type\": \"flashcard\", \"front\": \"line one\nline two\", \"back\": \"x\" } ] }";
        var result = ImportParser.Parse(json);
        Assert.True(result.Success, result.Error);
        Assert.True(result.Repaired);
        Assert.Contains("\n", result.Package!.AllItems.Single().Prompt);
    }

    [Fact]
    public void RepairsMissingCommaBetweenObjects()
    {
        var json = """
            { "items": [
              { "type": "flashcard", "front": "1", "back": "a" }
              { "type": "flashcard", "front": "2", "back": "b" }
            ] }
            """;
        var result = ImportParser.Parse(json);
        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.Package!.ItemCount);
    }

    [Fact]
    public void RecoversCompleteItemsFromTruncatedReply()
    {
        var json = """
            { "subject": "Bio", "items": [
              { "type": "flashcard", "front": "1", "back": "a" },
              { "type": "flashcard", "front": "2", "back": "b" },
              { "type": "flashcard", "front": "3", "ba
            """;
        var result = ImportParser.Parse(json);
        Assert.True(result.Success, result.Error);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Package!.ItemCount);
        Assert.Contains(result.Issues, i => i.Level == IssueLevel.Warning);
    }

    [Fact]
    public void ReportsSyntaxErrorWithLineNumber()
    {
        var json = "{\n  \"items\": [\n    { \"front\": \"a\" \"back\": 5 :: }\n  ]\n}";
        var result = ImportParser.Parse(json);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(3, result.ErrorLine);
    }

    [Theory]
    [InlineData("\"B\"", new[] { 1 })]
    [InlineData("\"b\"", new[] { 1 })]
    [InlineData("\"B) Mitochondria\"", new[] { 1 })]
    [InlineData("\"Option C\"", new[] { 2 })]
    [InlineData("\"Mitochondria\"", new[] { 1 })]
    [InlineData("[\"A\", \"C\"]", new[] { 0, 2 })]
    [InlineData("\"A, C\"", new[] { 0, 2 })]
    [InlineData("\"Б\"", new[] { 1 })]
    [InlineData("2", new[] { 2 })]
    public void ReadsTheCorrectAnswerInManyShapes(string answerJson, int[] expected)
    {
        var json = $$"""
            { "questions": [ { "type": "mcq", "question": "Q?", "options": ["Nucleus", "Mitochondria", "Ribosome", "Golgi"], "answer": {{answerJson}} } ] }
            """;
        var result = ImportParser.Parse(json);
        Assert.True(result.Success, result.Error + string.Join("; ", result.Issues.Select(i => i.Message)));
        var item = result.Package!.AllItems.Single();
        Assert.Equal(expected, item.Data.Correct);
        Assert.Equal(expected.Length > 1 ? ItemTypes.Multiple : ItemTypes.Single, item.Type);
    }

    [Fact]
    public void ReadsOptionObjectsWithCorrectFlagsAndWhy()
    {
        var json = """
            { "items": [ { "question": "2+2?", "options": [
                { "text": "3", "correct": false, "why": "too small" },
                { "text": "4", "isCorrect": true, "why": "right" } ] } ] }
            """;
        var item = ImportParser.Parse(json).Package!.AllItems.Single();
        Assert.Equal(ItemTypes.Single, item.Type);
        Assert.Equal(new[] { 1 }, item.Data.Correct);
        Assert.Equal("too small", item.Data.Options![0].Why);
    }

    [Fact]
    public void StripsLetterPrefixesFromOptions()
    {
        var json = """{ "items": [ { "type": "single", "question": "Q", "options": ["A) one", "B. two", "(C) three"], "answer": "C" } ] }""";
        var item = ImportParser.Parse(json).Package!.AllItems.Single();
        Assert.Equal(new[] { "one", "two", "three" }, item.Data.Options!.Select(o => o.Text));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"True\"", true)]
    [InlineData("\"невярно\"", false)]
    [InlineData("\"yes\"", true)]
    public void ReadsTrueFalseAnswers(string answer, bool expected)
    {
        var json = $$"""{ "items": [ { "type": "true_false", "statement": "S", "answer": {{answer}} } ] }""";
        var item = ImportParser.Parse(json).Package!.AllItems.Single();
        Assert.Equal(ItemTypes.TrueFalse, item.Type);
        Assert.Equal(expected, item.Data.Truth);
    }

    [Fact]
    public void ShortAnswerKeepsAlternativesAndKeyPoints()
    {
        var json = """{ "items": [ { "type": "open", "question": "Why?", "answer": ["Because", "Since"], "keyPoints": ["a", "b"], "page": 12 } ] }""";
        var item = ImportParser.Parse(json).Package!.AllItems.Single();
        Assert.Equal(ItemTypes.Short, item.Type);
        Assert.Equal("Because", item.Answer);
        Assert.Equal(new[] { "Since" }, item.Data.Accept);
        Assert.Equal(new[] { "a", "b" }, item.Data.KeyPoints);
        Assert.Equal("12", item.Page);
    }

    [Fact]
    public void SkipsInvalidItemsWithNumberedMessages()
    {
        var json = """
            { "items": [
              { "type": "flashcard", "front": "ok", "back": "ok" },
              { "type": "single", "question": "No answer", "options": ["a", "b"] },
              { "type": "single", "question": "Bad letter", "options": ["a", "b"], "answer": "F" },
              { "type": "flashcard", "front": "missing back" }
            ] }
            """;
        var result = ImportParser.Parse(json);
        Assert.True(result.Success);
        Assert.Equal(1, result.Package!.ItemCount);
        var errors = result.Issues.Where(i => i.Level == IssueLevel.Error).ToList();
        Assert.Equal(3, errors.Count);
        Assert.Equal(new int?[] { 2, 3, 4 }, errors.Select(e => e.ItemNumber));
        Assert.StartsWith("Item 2:", errors[0].Message);
    }

    [Fact]
    public void ReadsBareArrayAndInfersTypes()
    {
        var json = """
            [
              { "term": "Mitosis", "definition": "Cell division" },
              { "question": "Is the sky blue?", "answer": true },
              { "heading": "Intro", "content": "Text" }
            ]
            """;
        var items = ImportParser.Parse(json).Package!.AllItems.ToList();
        Assert.Equal(new[] { ItemTypes.Flashcard, ItemTypes.TrueFalse, ItemTypes.Note }, items.Select(i => i.Type));
    }

    [Fact]
    public void ReadsChaptersArrayWithNestedItems()
    {
        var json = """
            { "subject": "Bio", "chapters": [
              { "number": "1", "title": "Cells", "pages": "1-10", "flashcards": [ { "front": "a", "back": "b" } ] },
              { "number": "2", "title": "Tissues", "questions": [ { "type": "truefalse", "question": "x", "answer": false } ] }
            ] }
            """;
        var pkg = ImportParser.Parse(json).Package!;
        Assert.Equal(2, pkg.Decks.Count);
        Assert.Equal("Cells", pkg.Decks[0].Chapter!.Title);
        Assert.Equal("1-10", pkg.Decks[0].Chapter!.Pages);
        Assert.Equal("2", pkg.Decks[1].Chapter!.Number);
        Assert.Equal("Bio", pkg.Subject);
    }

    [Fact]
    public void ParsesChapterGivenAsText()
    {
        var json = """{ "chapter": "Chapter 7: Nervous System", "items": [ { "front": "a", "back": "b" } ] }""";
        var chapter = ImportParser.Parse(json).Package!.Decks[0].Chapter!;
        Assert.Equal("7", chapter.Number);
        Assert.Equal("Nervous System", chapter.Title);
    }

    [Fact]
    public void DifficultyWordsAndNumbersAreNormalised()
    {
        var json = """{ "difficulty": "hard", "items": [ { "front": "a", "back": "b" }, { "front": "c", "back": "d", "difficulty": 2 }, { "front": "e", "back": "f", "difficulty": "лесно" } ] }""";
        var items = ImportParser.Parse(json).Package!.AllItems.ToList();
        Assert.Equal(new[] { 4, 2, 2 }, items.Select(i => i.Difficulty));
    }

    [Fact]
    public void EmptyInputGivesFriendlyError()
    {
        var result = ImportParser.Parse("Sorry, I cannot do that.");
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}
