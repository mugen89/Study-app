using System.Globalization;
using StudyApp.Domain;
using StudyApp.Domain.Settings;

namespace StudyApp.Data.Services;

public sealed record BackupInfo(string Path, string FileName, DateTime Created, long Size, string Reason);

/// <summary>
/// Database backups in data\backups. Automatic ones are pruned to the configured count;
/// manual ones are kept until deleted. Restore is staged and applied on the next start
/// (before the database is opened), so files are never replaced while in use.
/// </summary>
public static class BackupService
{
    public const string Manual = "manual";
    public const string Auto = "auto";
    public const string BeforeUpdate = "before-update";
    public const string BeforeRestore = "before-restore";

    private static string PendingRestorePath => Path.Combine(AppPaths.DataDir, "restore.pending");

    public static string Create(StudyStore store, string reason)
    {
        Directory.CreateDirectory(AppPaths.BackupDir);
        var name = $"study-{DateTime.Now:yyyyMMdd-HHmmss}-{Sanitize(reason)}.db";
        var path = Path.Combine(AppPaths.BackupDir, name);
        store.Db.BackupTo(path);
        Log.Info("Backup created: " + name);
        return path;
    }

    public static List<BackupInfo> List()
    {
        if (!Directory.Exists(AppPaths.BackupDir)) return new();
        return new DirectoryInfo(AppPaths.BackupDir).GetFiles("*.db")
            .Select(f => new BackupInfo(f.FullName, f.Name, ParseDate(f) ?? f.LastWriteTime, f.Length, ParseReason(f.Name)))
            .OrderByDescending(b => b.Created)
            .ToList();
    }

    /// <summary>Keeps the newest <paramref name="keep"/> automatic backups; manual backups are never pruned.</summary>
    public static void Prune(int keep)
    {
        keep = Math.Max(1, keep);
        foreach (var b in List().Where(b => b.Reason != Manual).Skip(keep))
        {
            try { File.Delete(b.Path); }
            catch (Exception ex) { Log.Warn("Could not delete old backup " + b.FileName, ex); }
        }
    }

    /// <summary>Creates the daily automatic backup if the last one is older than 20 hours.</summary>
    public static bool AutoBackupIfDue(StudyStore store, AppSettings settings)
    {
        if (!settings.AutoBackup) return false;
        var last = settings.LastAutoBackupAt;
        if (last > 0 && Time.NowMs() - last < TimeSpan.FromHours(20).TotalMilliseconds) return false;
        try
        {
            Create(store, Auto);
            settings.LastAutoBackupAt = Time.NowMs();
            store.SaveSettings(settings);
            Prune(settings.BackupsToKeep);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Automatic backup failed", ex);
            return false;
        }
    }

    public static void Delete(BackupInfo backup) => File.Delete(backup.Path);

    /// <summary>Marks a backup to replace the database on the next start.</summary>
    public static void StageRestore(string backupPath)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        File.Copy(backupPath, PendingRestorePath, overwrite: true);
        Log.Info("Restore staged from " + Path.GetFileName(backupPath));
    }

    public static bool HasPendingRestore => File.Exists(PendingRestorePath);

    /// <summary>Called at start-up before the database is opened.</summary>
    public static bool ApplyPendingRestore()
    {
        if (!File.Exists(PendingRestorePath)) return false;
        try
        {
            Directory.CreateDirectory(AppPaths.BackupDir);
            var db = AppPaths.DatabasePath;
            if (File.Exists(db))
            {
                var safety = Path.Combine(AppPaths.BackupDir, $"study-{DateTime.Now:yyyyMMdd-HHmmss}-{BeforeRestore}.db");
                File.Move(db, safety);
                foreach (var ext in new[] { "-wal", "-shm" })
                    if (File.Exists(db + ext)) File.Move(db + ext, safety + ext);
            }
            File.Move(PendingRestorePath, db);
            Log.Info("Database restored from backup.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Applying the staged restore failed", ex);
            return false;
        }
    }

    private static string Sanitize(string reason) =>
        new string(reason.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

    private static DateTime? ParseDate(FileInfo f)
    {
        var parts = f.Name.Split('-');
        if (parts.Length >= 3 && DateTime.TryParseExact(parts[1] + parts[2], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return null;
    }

    private static string ParseReason(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var parts = name.Split('-', 4);
        return parts.Length == 4 ? parts[3] : "";
    }
}
