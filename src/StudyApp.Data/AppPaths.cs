namespace StudyApp.Data;

/// <summary>
/// Everything lives next to the executable so the whole app is portable:
/// <code>
/// StudyApp\
///   StudyApp.exe
///   data\study.db      &lt;- the database
///   data\backups\      &lt;- automatic and manual backups
///   data\logs\         &lt;- error logs
///   update\            &lt;- temporary staging folder used by the updater (never inside data\)
/// </code>
/// </summary>
public static class AppPaths
{
    private static string? _baseOverride;

    /// <summary>Folder that contains StudyApp.exe.</summary>
    public static string BaseDir => _baseOverride ?? AppContext.BaseDirectory;

    public static string DataDir => Path.Combine(BaseDir, "data");
    public static string DatabasePath => Path.Combine(DataDir, "study.db");
    public static string BackupDir => Path.Combine(DataDir, "backups");
    public static string LogDir => Path.Combine(DataDir, "logs");
    public static string UpdateDir => Path.Combine(BaseDir, "update");

    /// <summary>Used by tests to redirect all files to a temp folder.</summary>
    public static void OverrideBaseDir(string? dir) => _baseOverride = dir;

    public static void EnsureDataFolders()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BackupDir);
        Directory.CreateDirectory(LogDir);
    }

    /// <summary>True when we can create files in the data folder (false e.g. inside Program Files).</summary>
    public static bool CanWriteData(out string? error)
    {
        try
        {
            EnsureDataFolders();
            var probe = Path.Combine(DataDir, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
