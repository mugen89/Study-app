using System.Reflection;
using StudyApp.App.Shell;
using StudyApp.App.Updates;
using StudyApp.Domain.Settings;

namespace StudyApp.App;

/// <summary>Application-wide services (one user, one window, one database).</summary>
public static class Host
{
    public const string RepoOwner = "mugen89";
    public const string RepoName = "Study-app";
    public const string RepoUrl = "https://github.com/" + RepoOwner + "/" + RepoName;

    public static StudyStore Store { get; set; } = null!;
    public static AppSettings Settings { get; set; } = new();
    public static MainForm? Main { get; set; }
    public static UpdateInfo? AvailableUpdate { get; set; }

    /// <summary>Text of the last import error (pre-fills the "Fix my JSON" prompt).</summary>
    public static string LastImportError { get; set; } = "";

    public static string Version
    {
        get
        {
            var info = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                       ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
    }

    public static void SaveSettings()
    {
        try { Store.SaveSettings(Settings); }
        catch (Exception ex) { Log.Error("Saving settings failed", ex); }
    }

    public static Icon? AppIcon
    {
        get
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StudyApp.app.ico");
                return stream == null ? null : new Icon(stream);
            }
            catch
            {
                return null;
            }
        }
    }
}
