using StudyApp.App.Dialogs;

namespace StudyApp.App.Updates;

/// <summary>Shows the release notes of a new version and installs it.</summary>
public sealed class UpdateDialog : DialogBase
{
    private readonly UpdateInfo _info;
    private readonly ProgressBarEx _progress;
    private readonly TextLabel _status;
    private readonly ButtonEx _install;
    private readonly ButtonEx _later;
    private readonly ButtonEx _skip;
    private CancellationTokenSource? _cts;

    private UpdateDialog(UpdateInfo info) : base(T("Update available"), 600,
        F("Version {0} is available — you have {1}.", info.Version, Host.Version) + "  " + L.ShortDate(info.Published))
    {
        _info = info;
        var notes = new Card(18) { Gap = S(8) };
        notes.Controls.Add(new TextLabel(T("What's new"), Fonts.UiSemibold(10.5f), Theme.P.Text));
        notes.Controls.Add(new RichTextView(string.IsNullOrWhiteSpace(info.Notes) ? T("No release notes.") : info.Notes, RichStyle.Ui(10f)));
        Content.Controls.Add(notes);
        Content.Controls.Add(new Banner(Tone.Info, T("Your study data is kept"),
            T("The update only replaces the program files. The data folder is never touched, and a backup of your database is made first.")));

        _progress = new ProgressBarEx(0, 8) { Visible = false };
        _status = new TextLabel("", Fonts.Ui(9.5f), Theme.P.Text2) { Visible = false };
        Content.Controls.Add(_progress);
        Content.Controls.Add(_status);

        _skip = AddAction(T("Skip this version"), Kind.Ghost, () =>
        {
            Host.Settings.SkippedVersion = info.Version;
            Host.SaveSettings();
            DialogResult = DialogResult.Cancel;
        });
        _later = AddButton(T("Later"), Kind.Secondary, DialogResult.Cancel);
        _install = AddAction(T("Download and install"), Kind.Primary, () => _ = InstallAsync(), Icons.Download);
        AcceptButton = _install;
        FormClosing += (_, e) =>
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        };
    }

    private async Task InstallAsync()
    {
        if (Host.Main?.CurrentPage is { } page && !page.CanLeave()) return;

        _install.Enabled = _skip.Enabled = false;
        _progress.Visible = _status.Visible = true;
        _status.Text = T("Downloading…");
        Relayout();
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                if (p.Total > 0) _progress.Value = (float)p.Done / p.Total;
                _status.Text = p.Total > 0
                    ? F("Downloading… {0:0.0} of {1:0.0} MB", p.Done / 1048576.0, p.Total / 1048576.0)
                    : F("Downloading… {0:0.0} MB", p.Done / 1048576.0);
                _status.Invalidate();
            });
            var root = await UpdateService.DownloadAsync(_info, progress, _cts.Token);
            _status.Text = T("Installing — Study App will restart in a moment…");
            _status.Invalidate();
            await Task.Delay(600);
            DialogResult = DialogResult.OK;
            UpdateService.InstallAndRestart(root, _info.Version);
        }
        catch (OperationCanceledException)
        {
            _status.Text = T("Download cancelled.");
        }
        catch (Exception ex)
        {
            Log.Error("Update failed", ex);
            _status.Text = T("The update could not be installed:") + " " + ex.Message;
            _status.ForeColor = Theme.P.Danger;
            _install.Enabled = _skip.Enabled = true;
            _install.Text = T("Try again");
        }
        _status.Invalidate();
        Relayout();
    }

    public static void ShowFor(UpdateInfo info)
    {
        using var d = new UpdateDialog(info);
        d.ShowDialog(Form.ActiveForm ?? Host.Main);
    }

    public static void ShowReleaseNotes() => Feedback.OpenUrl($"{Host.RepoUrl}/releases/tag/v{Host.Version}");

    /// <summary>Manual "Check for updates" (Settings).</summary>
    public static async Task CheckNowAsync()
    {
        try
        {
            var info = await UpdateService.CheckAsync(CancellationToken.None);
            if (info == null)
            {
                Toast.Show(F("You have the latest version ({0}).", Host.Version), Tone.Success);
                return;
            }
            Host.AvailableUpdate = info;
            Host.Main?.RefreshSidebar();
            ShowFor(info);
        }
        catch (Exception ex)
        {
            Log.Warn("Manual update check failed", ex);
            Toast.Show(T("Could not check for updates. Are you connected to the internet?"), Tone.Warning);
        }
    }
}
