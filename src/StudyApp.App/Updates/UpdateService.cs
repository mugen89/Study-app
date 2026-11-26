using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using StudyApp.Data.Services;

namespace StudyApp.App.Updates;

public sealed class UpdateInfo
{
    public string Version { get; init; } = "";
    public string Tag { get; init; } = "";
    public string Name { get; init; } = "";
    public string Notes { get; init; } = "";
    public string PageUrl { get; init; } = "";
    public string? AssetUrl { get; init; }
    public string AssetName { get; init; } = "";
    public long AssetSize { get; init; }
    public string? ChecksumUrl { get; init; }
    public DateTime Published { get; init; }
}

/// <summary>
/// Updates from GitHub Releases. The release zip is downloaded and extracted into the "update" folder,
/// the database is backed up, and StudyApp.Updater.exe (from the new package) replaces the app files
/// after this process exits — never touching the "data" folder — and starts the new version.
/// </summary>
public static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StudyApp-Updater", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>GitHub API URL, or a local JSON file for testing (environment variable STUDYAPP_UPDATE_FEED).</summary>
    public static string FeedUrl =>
        Environment.GetEnvironmentVariable("STUDYAPP_UPDATE_FEED") is { Length: > 0 } custom
            ? custom
            : $"https://api.github.com/repos/{Host.RepoOwner}/{Host.RepoName}/releases/latest";

    public static Version ParseVersion(string? text)
    {
        var s = (text ?? "").Trim().TrimStart('v', 'V');
        var dash = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (dash > 0) s = s[..dash];
        var parts = s.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToList();
        while (parts.Count < 3) parts.Add(0);
        return new Version(parts[0], parts[1], parts[2]);
    }

    public static Version Current => ParseVersion(Host.Version);

    /// <summary>Returns the newer release, or null when the app is up to date.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        string json = IsLocal(FeedUrl) ? await File.ReadAllTextAsync(LocalPath(FeedUrl), ct) : await Http.GetStringAsync(FeedUrl, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;

        var tag = Str(root, "tag_name");
        var version = ParseVersion(tag);
        if (version <= Current) return null;

        JsonElement? asset = null, checksum = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            var list = assets.EnumerateArray().ToList();
            asset = list.Cast<JsonElement?>().FirstOrDefault(a => Str(a!.Value, "name").EndsWith("win-x64.zip", StringComparison.OrdinalIgnoreCase))
                    ?? list.Cast<JsonElement?>().FirstOrDefault(a => Str(a!.Value, "name").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset != null)
            {
                var name = Str(asset.Value, "name");
                checksum = list.Cast<JsonElement?>().FirstOrDefault(a => Str(a!.Value, "name").Equals(name + ".sha256", StringComparison.OrdinalIgnoreCase));
            }
        }

        return new UpdateInfo
        {
            Version = $"{version.Major}.{version.Minor}.{version.Build}",
            Tag = tag,
            Name = Str(root, "name"),
            Notes = Str(root, "body"),
            PageUrl = Str(root, "html_url"),
            AssetUrl = asset is { } a ? Str(a, "browser_download_url") : null,
            AssetName = asset is { } a2 ? Str(a2, "name") : "",
            AssetSize = asset is { } a3 && a3.TryGetProperty("size", out var size) && size.TryGetInt64(out var sz) ? sz : 0,
            ChecksumUrl = checksum is { } c ? Str(c, "browser_download_url") : null,
            Published = DateTime.TryParse(Str(root, "published_at"), out var pub) ? pub.ToLocalTime() : DateTime.Now,
        };
    }

    /// <summary>Downloads, verifies and extracts the update. Returns the folder that contains the new StudyApp.exe.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(info.AssetUrl)) throw new InvalidOperationException(T("This release has no download for Windows."));
        Directory.CreateDirectory(AppPaths.UpdateDir);
        var zipPath = Path.Combine(AppPaths.UpdateDir, $"StudyApp-{info.Version}.zip");

        if (IsLocal(info.AssetUrl))
        {
            File.Copy(LocalPath(info.AssetUrl), zipPath, overwrite: true);
            progress.Report((1, 1));
        }
        else
        {
            using var response = await Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? info.AssetSize;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(zipPath);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            var sw = Stopwatch.StartNew();
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (sw.ElapsedMilliseconds > 100)
                {
                    progress.Report((done, total));
                    sw.Restart();
                }
            }
            progress.Report((done, total));
        }

        if (!string.IsNullOrEmpty(info.ChecksumUrl))
        {
            var expected = (IsLocal(info.ChecksumUrl) ? await File.ReadAllTextAsync(LocalPath(info.ChecksumUrl), ct) : await Http.GetStringAsync(info.ChecksumUrl, ct))
                .Trim().Split(' ', '\t', '\n')[0].Trim().ToUpperInvariant();
            await using var zip = File.OpenRead(zipPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(zip, ct));
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(T("The downloaded file is damaged (checksum mismatch). Please try again."));
        }

        var extractDir = Path.Combine(AppPaths.UpdateDir, "package");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        var exe = Directory.GetFiles(extractDir, "StudyApp.exe", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new InvalidDataException(T("The update package does not contain StudyApp.exe."));
        var root = Path.GetDirectoryName(exe)!;
        if (!File.Exists(Path.Combine(root, "StudyApp.Updater.exe")))
            throw new InvalidDataException(T("The update package does not contain the updater."));
        return root;
    }

    /// <summary>Backs up the database, starts the updater from the new package and closes the app.</summary>
    public static void InstallAndRestart(string packageRoot, string newVersion)
    {
        try
        {
            BackupService.Create(Host.Store, BackupService.BeforeUpdate + "-" + Host.Version);
        }
        catch (Exception ex)
        {
            Log.Error("Backup before update failed", ex);
        }

        var updater = Path.Combine(packageRoot, "StudyApp.Updater.exe");
        var target = AppPaths.BaseDir.TrimEnd(Path.DirectorySeparatorChar);
        var args = $"--pid {Environment.ProcessId} --source \"{packageRoot}\" --target \"{target}\" --exe StudyApp.exe --from {Host.Version} --to {newVersion} --lang {L.Lang}";
        Log.Info("Starting updater: " + updater + " " + args);
        Process.Start(new ProcessStartInfo(updater, args) { UseShellExecute = false, WorkingDirectory = packageRoot });
        Application.Exit();
    }

    /// <summary>Removes the staging folder and leftovers from a previous update.</summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            foreach (var old in Directory.GetFiles(AppPaths.BaseDir, "*.old", SearchOption.TopDirectoryOnly))
                try { File.Delete(old); } catch { /* still locked */ }
        }
        catch { /* ignore */ }

        if (!Directory.Exists(AppPaths.UpdateDir)) return;
        // The updater may still be finishing; clean up a little later.
        Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(3000);
                try
                {
                    Directory.Delete(AppPaths.UpdateDir, recursive: true);
                    return;
                }
                catch
                {
                    // try again
                }
            }
        });
    }

    private static bool IsLocal(string url) => url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || (url.Length > 2 && url[1] == ':') || url.StartsWith(@"\\");

    private static string LocalPath(string url) => url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(url).LocalPath : url;

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
