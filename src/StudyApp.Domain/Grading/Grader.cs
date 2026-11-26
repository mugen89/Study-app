using StudyApp.Domain.Models;

namespace StudyApp.Domain.Grading;

public enum Strictness
{
    /// <summary>Must match exactly (ignoring outer spaces).</summary>
    Exact = 0,
    /// <summary>Ignores case, punctuation and extra spaces.</summary>
    Normal = 1,
    /// <summary>Also tolerates small typos and missing accents.</summary>
    Lenient = 2,
}

/// <summary>What the learner answered.</summary>
public sealed class Answer
{
    public List<int> Selected { get; set; } = new();
    public bool? Truth { get; set; }
    public string Text { get; set; } = "";
    /// <summary>For self-graded items (flashcards / short answers).</summary>
    public Result? SelfGrade { get; set; }

    public bool IsEmpty => Selected.Count == 0 && Truth == null && string.IsNullOrWhiteSpace(Text) && SelfGrade == null;
}

public readonly record struct ShortCheck(Result Suggested, bool NearMiss, string? Matched);

/// <summary>Grades answers. Self-graded types return the learner's own verdict.</summary>
public static class Grader
{
    public static Result Grade(Item item, Answer answer, Strictness strictness)
    {
        if (answer.SelfGrade.HasValue) return answer.SelfGrade.Value;

        switch (item.Type)
        {
            case ItemTypes.Single:
            case ItemTypes.Multiple:
                return GradeChoice(answer.Selected, item.Data.Correct ?? new());

            case ItemTypes.TrueFalse:
                if (answer.Truth == null || item.Data.Truth == null) return Result.Missed;
                return answer.Truth == item.Data.Truth ? Result.GotIt : Result.Missed;

            case ItemTypes.Short:
                return CheckShort(answer.Text, AcceptedAnswers(item), strictness).Suggested;

            default:
                return Result.Missed;
        }
    }

    /// <summary>All correct = Got it; only correct picks but some missing = Partial; any wrong pick = Missed.</summary>
    public static Result GradeChoice(IReadOnlyCollection<int> selected, IReadOnlyCollection<int> correct)
    {
        if (selected.Count == 0 || correct.Count == 0) return Result.Missed;
        var sel = selected.ToHashSet();
        var cor = correct.ToHashSet();
        if (sel.SetEquals(cor)) return Result.GotIt;
        if (sel.IsSubsetOf(cor)) return Result.Partial;
        return Result.Missed;
    }

    public static IEnumerable<string> AcceptedAnswers(Item item)
    {
        if (!string.IsNullOrWhiteSpace(item.Answer)) yield return item.Answer;
        if (item.Data.Accept != null)
            foreach (var a in item.Data.Accept)
                if (!string.IsNullOrWhiteSpace(a)) yield return a;
    }

    /// <summary>
    /// Suggests a verdict for a typed short answer. The learner always confirms it (self-grading),
    /// so this only pre-selects a button and shows "close — check spelling".
    /// </summary>
    public static ShortCheck CheckShort(string given, IEnumerable<string> accepted, Strictness strictness)
    {
        if (string.IsNullOrWhiteSpace(given)) return new(Result.Missed, false, null);
        var list = accepted.ToList();
        if (list.Count == 0) return new(Result.Missed, false, null);

        if (strictness == Strictness.Exact)
        {
            foreach (var a in list)
                if (string.Equals(given.Trim(), a.Trim(), StringComparison.Ordinal)) return new(Result.GotIt, false, a);
            return new(Result.Missed, false, null);
        }

        bool strip = strictness == Strictness.Lenient;
        var g = TextUtil.NormalizeForMatch(given, strip);
        ShortCheck best = new(Result.Missed, false, null);

        foreach (var a in list)
        {
            var n = TextUtil.NormalizeForMatch(a, strip);
            if (n.Length == 0) continue;
            if (g == n) return new(Result.GotIt, false, a);

            // Long model answers: the typed answer is rarely identical, so compare key words.
            var words = n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3).Distinct().ToList();
            if (words.Count >= 4)
            {
                var gw = g.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                int hit = words.Count(w => gw.Contains(w) || (strictness == Strictness.Lenient && gw.Any(x => x.Length > 3 && TextUtil.Levenshtein(x, w) <= 1)));
                double ratio = (double)hit / words.Count;
                if (ratio >= 0.75) return new(Result.GotIt, false, a);
                if (ratio >= 0.4 && best.Suggested == Result.Missed) best = new(Result.Partial, false, a);
                continue;
            }

            int tolerance = strictness == Strictness.Lenient ? Math.Max(1, n.Length / 6) : (n.Length >= 8 ? 1 : 0);
            var dist = TextUtil.Levenshtein(g, n);
            if (dist <= tolerance) return new(Result.GotIt, dist > 0, a);
            if (dist <= tolerance + 2 && n.Length >= 5 && best.Suggested == Result.Missed) best = new(Result.Partial, true, a);
        }
        return best;
    }
}
