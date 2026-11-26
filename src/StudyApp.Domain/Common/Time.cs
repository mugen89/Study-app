using System.Globalization;

namespace StudyApp.Domain;

/// <summary>All timestamps are stored as Unix milliseconds (UTC).</summary>
public static class Time
{
    /// <summary>Overridable clock (tests).</summary>
    public static Func<DateTime> UtcNowProvider { get; set; } = () => DateTime.UtcNow;

    public static DateTime UtcNow => UtcNowProvider();
    public static long NowMs() => ToMs(UtcNow);

    public static DateTime FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    public static DateTime LocalFromMs(long ms) => FromMs(ms).ToLocalTime();

    public static long ToMs(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
        };
        return new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }
}

/// <summary>
/// A "study day" starts at <see cref="RolloverHour"/> local time (default 04:00), like Anki,
/// so late-night sessions count towards the same day.
/// </summary>
public static class StudyDay
{
    public static int RolloverHour { get; set; } = 4;

    public static DateOnly Of(DateTime local) => DateOnly.FromDateTime(local.AddHours(-RolloverHour));
    public static DateOnly OfMs(long utcMs) => Of(Time.LocalFromMs(utcMs));
    public static DateOnly Today => Of(Time.UtcNow.ToLocalTime());

    public static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string TodayKey => Key(Today);

    public static DateOnly Parse(string key) => DateOnly.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Local start of a study day.</summary>
    public static DateTime StartLocal(DateOnly day) =>
        DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue).AddHours(RolloverHour), DateTimeKind.Local);

    public static long StartMs(DateOnly day) => Time.ToMs(StartLocal(day));

    /// <summary>End of today's study day (exclusive), in UTC ms.</summary>
    public static long EndOfTodayMs() => StartMs(Today.AddDays(1));
}
