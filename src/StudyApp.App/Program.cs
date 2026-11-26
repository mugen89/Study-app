using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using StudyApp.App.Dialogs;
using StudyApp.App.Shell;
using StudyApp.App.Updates;
using StudyApp.Data.Services;

namespace StudyApp.App;

/// <summary>Command-line options. A .json path imports that file; --updated shows "what's new".</summary>
public sealed class StartupArgs
{
    public List<string> Files { get; } = new();
    public string? UpdatedFrom { get; set; }

    // Developer helpers (screenshots / testing): --open=page, --dev-sample, --lang=bg, --theme=dark
    public string? OpenPage { get; set; }
    public bool DevSample { get; set; }
    public string? Lang { get; set; }
    public string? Theme { get; set; }
    public string? Dialog { get; set; }

    public static StartupArgs Parse(IEnumerable<string> args)
    {
        var result = new StartupArgs();
        var list = args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (a.Equals("--updated", StringComparison.OrdinalIgnoreCase))
            {
                result.UpdatedFrom = i + 1 < list.Count && !list[i + 1].StartsWith("--") ? list[++i] : "";
            }
            else if (a.StartsWith("--open=", StringComparison.OrdinalIgnoreCase))
            {
                result.OpenPage = a[7..];
            }
            else if (a.Equals("--dev-sample", StringComparison.OrdinalIgnoreCase))
            {
                result.DevSample = true;
            }
            else if (a.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
            {
                result.Lang = a[7..];
            }
            else if (a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase))
            {
                result.Theme = a[8..];
            }
            else if (a.StartsWith("--dev-dialog=", StringComparison.OrdinalIgnoreCase))
            {
                result.Dialog = a[13..];
            }
            else if (!a.StartsWith("--") && File.Exists(a))
            {
                result.Files.Add(Path.GetFullPath(a));
            }
        }
        return result;
    }

    public string Serialize() => string.Join("\n", Files);
}

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    private static int Main(string[] args)
    {
        var startup = StartupArgs.Parse(args);
        var instanceKey = InstanceKey();

        // One window per app folder: a second start hands its files to the running window.
        _mutex = new Mutex(true, @"Local\StudyApp-" + instanceKey, out bool first);
        if (!first)
        {
            SingleInstance.Send(instanceKey, startup.Serialize());
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorDialog.ShowUnhandled(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        Application.AddMessageFilter(new WheelRouter());

        Fonts.Init();
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) Dpi.Set(g.DpiX / 96f, 1f);

        if (!AppPaths.CanWriteData(out var writeError))
        {
            L.SetLanguage(L.SystemDefault, raise: false);
            MessageBox.Show(
                T("Study App cannot save its data in this folder:") + "\n" + AppPaths.DataDir + "\n\n" +
                T("Please move the whole Study App folder to a place where you can save files, for example Documents or the Desktop, and start it again.") +
                "\n\n" + writeError,
                "Study App", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        Log.Info($"Starting Study App {Host.Version} from {AppPaths.BaseDir}" + (args.Length > 0 ? " args: " + string.Join(" | ", args) : ""));

        BackupService.ApplyPendingRestore();
        if (!OpenDatabase()) return 2;

        var settings = Host.Store.LoadSettings();
        Host.Settings = settings;
        L.SetLanguage(startup.Lang ?? (string.IsNullOrEmpty(settings.Language) ? L.SystemDefault : settings.Language), raise: false);
        StudyDay.RolloverHour = Math.Clamp(settings.DayStartHour, 0, 12);
        if (startup.Theme != null && Enum.TryParse<Domain.Settings.ThemeMode>(startup.Theme, true, out var forced))
        {
            var preview = settings.Clone();
            preview.Theme = forced;
            Theme.Apply(preview);
        }
        else Theme.Apply(settings);
        Fonts.SetStudyFont(settings.StudyFont);
        Dpi.Set(Dpi.Device, settings.UiScale / 100f);

        try
        {
            Host.Store.EnsureBuiltInTemplates();
            BackupService.AutoBackupIfDue(Host.Store, settings);
            Host.Store.PurgeTrash(Time.NowMs() - (long)TimeSpan.FromDays(Math.Max(1, settings.RecycleBinDays)).TotalMilliseconds);
            UpdateService.CleanupAfterUpdate();
        }
        catch (Exception ex)
        {
            Log.Error("Start-up maintenance failed", ex);
        }

        var main = new MainForm(startup);
        Host.Main = main;
        SingleInstance.Listen(instanceKey, payload => main.HandleExternalArgs(payload));
        Application.Run(main);

        try { Host.Store.Dispose(); } catch { /* ignore */ }
        Log.Info("Study App closed.");
        return 0;
    }

    private static bool OpenDatabase()
    {
        try
        {
            Host.Store = new StudyStore(AppPaths.DatabasePath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Opening the database failed", ex);
            var latest = BackupService.List().FirstOrDefault();
            var message = T("The study database could not be opened.") + "\n\n" + ex.Message;
            if (latest != null)
            {
                message += "\n\n" + F("Restore the latest backup from {0}?", L.DateTimeShort(latest.Created));
                if (MessageBox.Show(message, "Study App", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                {
                    BackupService.StageRestore(latest.Path);
                    BackupService.ApplyPendingRestore();
                    try
                    {
                        Host.Store = new StudyStore(AppPaths.DatabasePath);
                        return true;
                    }
                    catch (Exception ex2)
                    {
                        Log.Error("Opening the restored database failed", ex2);
                    }
                }
            }
            else
            {
                MessageBox.Show(message, "Study App", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }
    }

    private static string InstanceKey()
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.BaseDir.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16];
    }
}

/// <summary>Passes command-line files from a second start to the running window (named pipe).</summary>
internal static class SingleInstance
{
    public static void Send(string key, string payload)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", "StudyApp-" + key, PipeDirection.Out);
            client.Connect(1500);
            var bytes = Encoding.UTF8.GetBytes(payload);
            client.Write(bytes, 0, bytes.Length);
        }
        catch
        {
            // The other instance is closing; nothing to do.
        }
    }

    public static void Listen(string key, Action<string> onMessage)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream("StudyApp-" + key, PipeDirection.In, 1);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var payload = reader.ReadToEnd();
                    Host.Main?.BeginInvoke(() => onMessage(payload));
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        })
        { IsBackground = true, Name = "SingleInstancePipe" };
        thread.Start();
    }
}
