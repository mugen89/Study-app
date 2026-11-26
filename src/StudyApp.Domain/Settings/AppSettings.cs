using System.Text.Json;
using StudyApp.Domain.Grading;
using StudyApp.Domain.Prompts;
using StudyApp.Domain.Sessions;
using StudyApp.Domain.Srs;

namespace StudyApp.Domain.Settings;

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
    Sepia = 3,
}

public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>All user settings. Stored as one JSON document in the settings table; new properties get defaults.</summary>
public sealed class AppSettings
{
    // ── General / appearance ─────────────────────────────────────────────
    /// <summary>"en", "bg" or "" (follow Windows).</summary>
    public string Language { get; set; } = "";
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public string Accent { get; set; } = "#4F6BED";
    /// <summary>UI text size in percent (90–130).</summary>
    public int UiScale { get; set; } = 100;
    public bool Animations { get; set; } = true;
    public string UserName { get; set; } = "";

    // ── Reading comfort (study text) ─────────────────────────────────────
    /// <summary>Font family for questions, answers and notes. Empty = default.</summary>
    public string StudyFont { get; set; } = "";
    public float CardFontSize { get; set; } = 22f;
    public float QuestionFontSize { get; set; } = 17f;
    public float AnswerFontSize { get; set; } = 15f;
    public float NotesFontSize { get; set; } = 14f;
    /// <summary>Line height as a multiple of the font size.</summary>
    public float LineHeight { get; set; } = 1.5f;
    /// <summary>Maximum width of study text in pixels at 100% scale.</summary>
    public int ContentWidth { get; set; } = 820;
    public bool CenterCardText { get; set; } = true;

    // ── Study ────────────────────────────────────────────────────────────
    public int DailyGoal { get; set; } = 50;
    public int NewPerDay { get; set; } = 20;
    public int MaxReviewsPerDay { get; set; } = 200;
    public int DayStartHour { get; set; } = 4;
    public Strictness ShortAnswerStrictness { get; set; } = Strictness.Normal;
    public bool TestsUpdateSchedule { get; set; } = true;
    public bool ShowIntervals { get; set; } = true;
    public bool ConfirmLeaveSession { get; set; } = true;
    public string LearningSteps { get; set; } = "1 10";
    public string RelearningSteps { get; set; } = "10";
    public int GraduatingInterval { get; set; } = 1;
    public int EasyInterval { get; set; } = 4;
    public int StartingEase { get; set; } = 250;
    public int MaxInterval { get; set; } = 365;
    public int IntervalModifier { get; set; } = 100;
    public SessionOptions LastFlashcardSession { get; set; } = SessionOptions.DefaultFlashcards();
    public SessionOptions LastTestSession { get; set; } = SessionOptions.DefaultTest();

    // ── Data ─────────────────────────────────────────────────────────────
    public bool AutoBackup { get; set; } = true;
    public int BackupsToKeep { get; set; } = 10;
    public int RecycleBinDays { get; set; } = 30;
    public long LastAutoBackupAt { get; set; }

    // ── Updates ──────────────────────────────────────────────────────────
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public string SkippedVersion { get; set; } = "";

    // ── Remembered state ─────────────────────────────────────────────────
    public WindowPlacement? Window { get; set; }
    public long LastSubjectId { get; set; }
    public bool SidebarCollapsed { get; set; }
    public string LastPage { get; set; } = "";
    public PromptOptions PromptOptions { get; set; } = new();
    public string LastTemplateKey { get; set; } = "flashcards";

    public SrsSettings ToSrs() => new()
    {
        LearningStepsMinutes = SrsSettings.ParseSteps(LearningSteps, new[] { 1, 10 }),
        RelearningStepsMinutes = SrsSettings.ParseSteps(RelearningSteps, new[] { 10 }),
        GraduatingIntervalDays = Math.Clamp(GraduatingInterval, 1, 60),
        EasyIntervalDays = Math.Clamp(EasyInterval, 1, 120),
        StartingEase = Math.Clamp(StartingEase, 130, 500) / 100.0,
        MaxIntervalDays = Math.Clamp(MaxInterval, 7, 36500),
        IntervalModifier = Math.Clamp(IntervalModifier, 50, 300) / 100.0,
    };

    public void ResetStudyDefaults()
    {
        var d = new AppSettings();
        DailyGoal = d.DailyGoal; NewPerDay = d.NewPerDay; MaxReviewsPerDay = d.MaxReviewsPerDay; DayStartHour = d.DayStartHour;
        ShortAnswerStrictness = d.ShortAnswerStrictness; TestsUpdateSchedule = d.TestsUpdateSchedule; ShowIntervals = d.ShowIntervals;
        ConfirmLeaveSession = d.ConfirmLeaveSession; LearningSteps = d.LearningSteps; RelearningSteps = d.RelearningSteps;
        GraduatingInterval = d.GraduatingInterval; EasyInterval = d.EasyInterval; StartingEase = d.StartingEase;
        MaxInterval = d.MaxInterval; IntervalModifier = d.IntervalModifier;
    }

    public void ResetReadingDefaults()
    {
        var d = new AppSettings();
        StudyFont = d.StudyFont; CardFontSize = d.CardFontSize; QuestionFontSize = d.QuestionFontSize; AnswerFontSize = d.AnswerFontSize;
        NotesFontSize = d.NotesFontSize; LineHeight = d.LineHeight; ContentWidth = d.ContentWidth; CenterCardText = d.CenterCardText;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static AppSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public AppSettings Clone() => FromJson(ToJson());
}
