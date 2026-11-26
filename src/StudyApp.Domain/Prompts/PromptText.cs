using System.Text;
using StudyApp.Domain.Models;

namespace StudyApp.Domain.Prompts;

/// <summary>Formats app data (mistakes, errors) for inclusion in prompts.</summary>
public static class PromptText
{
    public static string WeakItems(IEnumerable<Item> items, bool bg)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            var prompt = TextUtil.Truncate(TextUtil.StripMarkdown(item.Prompt), 220);
            var answer = TextUtil.Truncate(TextUtil.StripMarkdown(item.CorrectAnswerText()), 160);
            int misses = Math.Max(0, item.SeenCount - item.CorrectCount);
            sb.Append(bg ? "- „" : "- \"").Append(prompt).Append(bg ? "“" : "\"");
            if (answer.Length > 0) sb.Append(bg ? " → верен отговор: " : " → correct answer: ").Append(answer);
            if (!string.IsNullOrWhiteSpace(item.Page)) sb.Append(bg ? " (стр. " : " (p. ").Append(item.Page.Trim()).Append(')');
            if (item.SeenCount > 0)
                sb.Append(bg ? $" — грешно {misses} от {item.SeenCount} пъти" : $" — missed {misses} of {item.SeenCount} times");
            if (item.Tags.Count > 0) sb.Append(bg ? " [теми: " : " [topics: ").Append(string.Join(", ", item.Tags)).Append(']');
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>"Explain this to me" prompt for one missed item.</summary>
    public static string Explain(Item item, string givenAnswer, Subject? subject, bool bg)
    {
        var sb = new StringBuilder();
        var subjectName = subject?.Name ?? item.SubjectName;
        var source = string.IsNullOrWhiteSpace(item.Source) ? subject?.Source ?? "" : item.Source;
        if (bg)
        {
            sb.Append("Уча ").Append(string.IsNullOrWhiteSpace(subjectName) ? "този предмет" : subjectName);
            if (source.Length > 0) sb.Append(" по учебника „").Append(source).Append('“');
            sb.Append(" и сгреших този въпрос:\n\n");
            sb.Append(TextUtil.StripMarkdown(item.Prompt)).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(givenAnswer)) sb.Append("Моят отговор: ").Append(givenAnswer.Trim()).Append('\n');
            sb.Append("Верен отговор: ").Append(TextUtil.StripMarkdown(item.CorrectAnswerText())).Append('\n');
            if (item.Page.Length > 0) sb.Append("Страница в учебника: ").Append(item.Page).Append('\n');
            sb.Append("\nОбясни ми понятието просто и ясно, като използваш учебника");
            if (item.Page.Length > 0) sb.Append(" (стр. ").Append(item.Page).Append(')');
            sb.Append(". Кажи защо моят отговор е грешен, дай пример и кратък начин да го запомня. Накрая ми задай един подобен контролен въпрос, без да казваш отговора.");
        }
        else
        {
            sb.Append("I am studying ").Append(string.IsNullOrWhiteSpace(subjectName) ? "this subject" : subjectName);
            if (source.Length > 0) sb.Append(" with the textbook \"").Append(source).Append('"');
            sb.Append(" and I got this question wrong:\n\n");
            sb.Append(TextUtil.StripMarkdown(item.Prompt)).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(givenAnswer)) sb.Append("My answer: ").Append(givenAnswer.Trim()).Append('\n');
            sb.Append("Correct answer: ").Append(TextUtil.StripMarkdown(item.CorrectAnswerText())).Append('\n');
            if (item.Page.Length > 0) sb.Append("Textbook page: ").Append(item.Page).Append('\n');
            sb.Append("\nExplain the concept simply and clearly using the textbook");
            if (item.Page.Length > 0) sb.Append(" (p. ").Append(item.Page).Append(')');
            sb.Append(". Tell me why my answer is wrong, give an example and a short way to remember it. Finally, ask me one similar check question without giving the answer.");
        }
        return sb.ToString();
    }
}
