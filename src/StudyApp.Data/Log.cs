using System.Text;

namespace StudyApp.Data;

/// <summary>Minimal file logger: Data\Logs\app-yyyy-MM.log. Never throws.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string CurrentFile => Path.Combine(AppPaths.LogDir, $"app-{DateTime.Now:yyyy-MM}.log");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" [").Append(level).Append("] ").AppendLine(message);
            if (ex != null) sb.AppendLine(ex.ToString());
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDir);
                File.AppendAllText(CurrentFile, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
