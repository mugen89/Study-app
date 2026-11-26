using StudyApp.Domain.Localization;
using StudyApp.Domain.Models;

namespace StudyApp.Domain.Srs;

/// <summary>Scheduler settings (Anki-style SM-2 defaults).</summary>
public sealed class SrsSettings
{
    public int[] LearningStepsMinutes { get; set; } = { 1, 10 };
    public int[] RelearningStepsMinutes { get; set; } = { 10 };
    public int GraduatingIntervalDays { get; set; } = 1;
    public int EasyIntervalDays { get; set; } = 4;
    public double StartingEase { get; set; } = 2.5;
    public double EasyBonus { get; set; } = 1.3;
    public double HardFactor { get; set; } = 1.2;
    public double IntervalModifier { get; set; } = 1.0;
    public int MaxIntervalDays { get; set; } = 365;
    public double MinEase { get; set; } = 1.3;

    public static int[] ParseSteps(string? text, int[] fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var steps = text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim().TrimEnd('m', 'M'), out var n) ? n : -1)
            .Where(n => n > 0 && n < 60 * 24)
            .ToArray();
        return steps.Length == 0 ? fallback : steps;
    }
}

/// <summary>The new scheduling state after an answer.</summary>
public readonly record struct SrsUpdate(SrsState State, int Step, double Ease, double IntervalDays, long DueMs, int Reps, int Lapses)
{
    public void ApplyTo(Item item)
    {
        item.SrsState = State;
        item.SrsStep = Step;
        item.SrsEase = Ease;
        item.SrsInterval = IntervalDays;
        item.SrsDue = DueMs;
        item.SrsReps = Reps;
        item.SrsLapses = Lapses;
    }
}

/// <summary>
/// SM-2 as popularised by Anki: learning steps in minutes, then growing day intervals multiplied
/// by an "ease" factor. Again = forgot, Hard = barely, Good = normal, Easy = trivial.
/// Review intervals are aligned to the start of a study day (see <see cref="StudyDay"/>).
/// </summary>
public static class Sm2
{
    private const long Minute = 60_000;

    public static SrsUpdate Next(Item item, Rating rating, long nowMs, SrsSettings s)
    {
        var ease = item.SrsEase <= 0 ? s.StartingEase : item.SrsEase;
        var reps = item.SrsReps + 1;
        var lapses = item.SrsLapses;

        switch (item.SrsState)
        {
            case SrsState.New:
            case SrsState.Learning:
                return Learning(item.SrsState == SrsState.New ? 0 : item.SrsStep, s.LearningStepsMinutes, rating, nowMs, s,
                    ease, reps, lapses, item.SrsInterval, relearning: false);

            case SrsState.Relearning:
                return Learning(item.SrsStep, s.RelearningStepsMinutes, rating, nowMs, s, ease, reps, lapses,
                    Math.Max(1, item.SrsInterval), relearning: true);

            default:
                return Review(item, rating, nowMs, s, ease, reps, lapses);
        }
    }

    private static SrsUpdate Learning(int step, int[] steps, Rating rating, long now, SrsSettings s,
        double ease, int reps, int lapses, double interval, bool relearning)
    {
        if (steps.Length == 0) steps = new[] { 10 };
        step = Math.Clamp(step, 0, steps.Length - 1);
        var state = relearning ? SrsState.Relearning : SrsState.Learning;

        switch (rating)
        {
            case Rating.Again:
                return new(state, 0, ease, interval, now + steps[0] * Minute, reps, lapses);

            case Rating.Hard:
                {
                    // Anki: on the first step Hard waits the average of the first two steps, otherwise repeats the step.
                    double minutes = step == 0 && steps.Length > 1 ? (steps[0] + steps[1]) / 2.0 : steps[step] * (steps.Length == 1 ? 1.5 : 1.0);
                    return new(state, step, ease, interval, now + (long)(minutes * Minute), reps, lapses);
                }

            case Rating.Good:
                if (step + 1 < steps.Length)
                    return new(state, step + 1, ease, interval, now + steps[step + 1] * Minute, reps, lapses);
                {
                    var days = relearning ? Math.Max(1, interval) : s.GraduatingIntervalDays;
                    days = Cap(days, s);
                    return new(SrsState.Review, 0, ease, days, DueInDays(now, days), reps, lapses);
                }

            default: // Easy
                {
                    var days = relearning ? Math.Max(1, interval) + 1 : s.EasyIntervalDays;
                    days = Cap(days, s);
                    return new(SrsState.Review, 0, ease, days, DueInDays(now, days), reps, lapses);
                }
        }
    }

    private static SrsUpdate Review(Item item, Rating rating, long now, SrsSettings s, double ease, int reps, int lapses)
    {
        var interval = Math.Max(1, item.SrsInterval);
        var today = StudyDay.OfMs(now);
        var dueDay = item.SrsDue.HasValue ? StudyDay.OfMs(item.SrsDue.Value) : today;
        var daysLate = Math.Max(0, today.DayNumber - dueDay.DayNumber);

        switch (rating)
        {
            case Rating.Again:
                {
                    lapses++;
                    ease = Math.Max(s.MinEase, ease - 0.20);
                    var newInterval = 1.0;
                    if (s.RelearningStepsMinutes.Length > 0)
                        return new(SrsState.Relearning, 0, ease, newInterval, now + s.RelearningStepsMinutes[0] * Minute, reps, lapses);
                    return new(SrsState.Review, 0, ease, newInterval, DueInDays(now, newInterval), reps, lapses);
                }

            case Rating.Hard:
                {
                    ease = Math.Max(s.MinEase, ease - 0.15);
                    var days = Cap(Math.Max(interval + 1, interval * s.HardFactor * s.IntervalModifier), s);
                    return new(SrsState.Review, 0, ease, days, DueInDays(now, days), reps, lapses);
                }

            case Rating.Good:
                {
                    var hard = interval * s.HardFactor;
                    var days = Cap(Math.Max(hard + 1, (interval + daysLate / 2.0) * ease * s.IntervalModifier), s);
                    return new(SrsState.Review, 0, ease, days, DueInDays(now, days), reps, lapses);
                }

            default: // Easy
                {
                    var good = Math.Max(interval * s.HardFactor + 1, (interval + daysLate / 2.0) * ease);
                    ease += 0.15;
                    var days = Cap(Math.Max(good + 1, (interval + daysLate) * ease * s.EasyBonus * s.IntervalModifier), s);
                    return new(SrsState.Review, 0, ease, days, DueInDays(now, days), reps, lapses);
                }
        }
    }

    private static double Cap(double days, SrsSettings s) => Math.Round(Math.Clamp(days, 1, Math.Max(1, s.MaxIntervalDays)));

    private static long DueInDays(long now, double days)
    {
        var day = StudyDay.OfMs(now).AddDays((int)Math.Round(days));
        return StudyDay.StartMs(day);
    }

    /// <summary>Maps a test result to a rating, so tests can feed the schedule too.</summary>
    public static Rating RatingFor(Result result) => result switch
    {
        Result.GotIt => Rating.Good,
        Result.Partial => Rating.Hard,
        _ => Rating.Again,
    };

    /// <summary>Short label for the button preview: "1m", "10m", "1d", "3.5mo", "1.2y".</summary>
    public static string Preview(Item item, Rating rating, long nowMs, SrsSettings s)
    {
        var next = Next(item, rating, nowMs, s);
        var ms = Math.Max(0, next.DueMs - nowMs);
        if (next.State is SrsState.Learning or SrsState.Relearning)
        {
            var minutes = Math.Max(1, (int)Math.Round(ms / 60000.0));
            return minutes < 60 ? L.F("{0}m", minutes) : L.F("{0}h", Math.Round(minutes / 60.0, 1));
        }

        var days = next.IntervalDays;
        if (days < 30) return L.F("{0}d", (int)days);
        if (days < 365) return L.F("{0}mo", Math.Round(days / 30.0, 1));
        return L.F("{0}y", Math.Round(days / 365.0, 1));
    }
}
