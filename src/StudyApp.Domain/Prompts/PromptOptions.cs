using System.Text.Json.Serialization;
using StudyApp.Domain.Models;

namespace StudyApp.Domain.Prompts;

/// <summary>The fill-in fields of the prompt builder. Remembered between sessions.</summary>
public sealed class PromptOptions
{
    public string Subject { get; set; } = "";
    public string Source { get; set; } = "";
    public string ChapterNumber { get; set; } = "";
    public string ChapterTitle { get; set; } = "";
    public string Pages { get; set; } = "";
    public int Count { get; set; } = 20;
    /// <summary>1–5, or 0 for a mix.</summary>
    public int Difficulty { get; set; } = 3;
    public List<string> QuestionTypes { get; set; } = ItemTypes.Questions.ToList();
    /// <summary>"" = same as the textbook, "en", "bg", or any language name.</summary>
    public string Language { get; set; } = "";
    public string Focus { get; set; } = "";
    public string Extra { get; set; } = "";
    public bool ExplainOptions { get; set; } = true;

    /// <summary>Filled automatically from the learner's mistakes.</summary>
    [JsonIgnore] public string WeakItems { get; set; } = "";
    /// <summary>Filled automatically from the last failed import.</summary>
    [JsonIgnore] public string ErrorText { get; set; } = "";

    public PromptOptions Clone()
    {
        var c = (PromptOptions)MemberwiseClone();
        c.QuestionTypes = QuestionTypes.ToList();
        return c;
    }
}
