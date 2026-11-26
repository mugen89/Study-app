using Microsoft.Win32;
using StudyApp.App.Dialogs;
using StudyApp.App.Pages;
using StudyApp.App.Updates;
using StudyApp.Domain.Settings;

namespace StudyApp.App.Shell;

/// <summary>The single main window: sidebar + page host, navigation history, global shortcuts and file drops.</summary>
public sealed class MainForm : Form
{
    private readonly StartupArgs _startup;
    private readonly Stack<Func<PageBase>> _back = new();
    private Sidebar _sidebar = null!;
    private Panel _content = null!;
    private PageBase? _page;
    private bool _placementRestored;

    public MainForm(StartupArgs startup)
    {
        _startup = startup;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Study App";
        Icon = Host.AppIcon;
        KeyPreview = true;
        AllowDrop = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        MinimumSize = new Size(S(900), S(600));
        RestorePlacement();
        BuildShell();
        L.Changed += OnLanguageChanged;
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
    }

    public PageBase? CurrentPage => _page;
    public bool CanGoBack => _back.Count > 0;

    // ── shell ────────────────────────────────────────────────────────

    private void BuildShell()
    {
        SuspendLayout();
        foreach (Control c in Controls.Cast<Control>().ToList())
        {
            if (c is Toast) continue;
            Controls.Remove(c);
            c.Dispose();
        }
        BackColor = Theme.P.Bg;
        Font = Fonts.Ui(10f);
        _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.P.Bg };
        _sidebar = new Sidebar { Dock = DockStyle.Left };
        Controls.Add(_content);
        Controls.Add(_sidebar);
        ResumeLayout(true);
        AttachDrop(this);
    }

    /// <summary>Recreates the whole UI (after a theme, language or DPI change) and reopens the current page.</summary>
    public void Rebuild()
    {
        var factory = _page?.Factory;
        _page = null;
        Tips.Reset();
        RichLayout.ClearCaches();
        BuildShell();
        if (IsHandleCreated) Native.StyleTitleBar(Handle);
        if (factory != null) ShowPage(factory(), factory);
        else Navigate(() => new HomePage(), false);
        Invalidate(true);
    }

    public void RefreshSidebar() => _sidebar.RefreshCounts();

    /// <summary>Rebuilds the current page with fresh data (keeps the history).</summary>
    public void ReloadPage()
    {
        if (_page?.Factory is { } factory) ShowPage(factory(), factory);
        RefreshSidebar();
    }

    // ── navigation ───────────────────────────────────────────────────

    public void Navigate(Func<PageBase> factory, bool addToHistory = true)
    {
        if (_page != null && !_page.CanLeave()) return;
        PageBase page;
        try
        {
            page = factory();
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(T("This page could not be opened."), ex);
            return;
        }
        if (addToHistory && _page?.Factory != null && _page.KeepInHistory)
        {
            _back.Push(_page.Factory);
            if (_back.Count > 30) TrimHistory();
        }
        ShowPage(page, factory);
    }

    /// <summary>Replaces the current page without adding it to the history (e.g. session → summary).</summary>
    public void Replace(Func<PageBase> factory)
    {
        var page = factory();
        ShowPage(page, factory);
    }

    private void TrimHistory()
    {
        var items = _back.Reverse().Skip(_back.Count - 20).ToList();
        _back.Clear();
        foreach (var f in items) _back.Push(f);
    }

    private void ShowPage(PageBase page, Func<PageBase> factory)
    {
        page.Factory = factory;
        page.Dock = DockStyle.Fill;
        _content.SuspendLayout();
        _content.Controls.Add(page);
        page.BringToFront();
        var old = _page;
        _page = page;
        if (old != null)
        {
            old.OnDeactivated();
            _content.Controls.Remove(old);
            old.Dispose();
        }
        _content.ResumeLayout(true);
        _sidebar.Visible = !page.FocusMode;
        _sidebar.SetSelected(page.NavKey);
        AttachDrop(page);
        page.OnActivated();
        foreach (Control c in Controls) if (c is Toast) c.BringToFront();
    }

    public void GoBack()
    {
        if (_page != null && !_page.CanLeave()) return;
        if (_back.Count == 0)
        {
            if (_page is not HomePage) Navigate(() => new HomePage(), false);
            return;
        }
        var factory = _back.Pop();
        try
        {
            ShowPage(factory(), factory);
        }
        catch (Exception ex)
        {
            Log.Warn("Going back failed", ex);
            Navigate(() => new HomePage(), false);
        }
    }

    // ── lifecycle ────────────────────────────────────────────────────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleTitleBar(Handle);
        float device = DeviceDpi / 96f;
        if (Math.Abs(device - Dpi.Device) > 0.01f)
        {
            Dpi.Set(device, Dpi.Zoom);
            Fonts.Clear();
            Rebuild();
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Dpi.Set(e.DeviceDpiNew / 96f, Dpi.Zoom);
        Fonts.Clear();
        Rebuild();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_startup.DevSample && Host.Store.GetSubjects().Count == 0) HomePage.LoadSample();
        if (_startup.Files.Count > 0)
        {
            Navigate(() => new ImportPage(_startup.Files.ToArray()), false);
        }
        else if (!string.IsNullOrEmpty(_startup.OpenPage))
        {
            OpenDevPage(_startup.OpenPage);
        }
        else
        {
            Navigate(() => new HomePage(), false);
        }

        if (_startup.UpdatedFrom != null)
            Toast.Show(F("Study App was updated to version {0}.", Host.Version), Tone.Success, T("What's new"), () => UpdateDialog.ShowReleaseNotes(), 8);

        if (Host.Settings.CheckUpdatesOnStartup && _startup.Dialog == null) _ = CheckForUpdatesAsync();
        if (_startup.Dialog != null) BeginInvoke(() => OpenDevDialog(_startup.Dialog));
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            await Task.Delay(2500);
            var info = await UpdateService.CheckAsync(CancellationToken.None);
            if (info == null || IsDisposed) return;
            Host.AvailableUpdate = info;
            _sidebar.Invalidate();
            if (info.Version != Host.Settings.SkippedVersion)
                Toast.Show(F("Version {0} of Study App is available.", info.Version), Tone.Accent, T("View"), () => UpdateDialog.ShowFor(info), 10);
        }
        catch (Exception ex)
        {
            Log.Info("Update check failed: " + ex.Message);
        }
    }

    /// <summary>Developer helper: StudyApp.exe --open=settings (used for screenshots and testing).</summary>
    private void OpenDevPage(string page)
    {
        Func<PageBase> factory = page.ToLowerInvariant() switch
        {
            "subjects" => () => new SubjectsPage(),
            "review" => () => new ReviewPage(),
            "search" => () => new SearchPage(),
            "import" => () => new ImportPage(),
            "prompts" => () => new PromptsPage(),
            "trash" => () => new TrashPage(),
            "settings" => () => new SettingsPage(),
            var s when s.StartsWith("subject:") && long.TryParse(s[8..], out var id) => () => new SubjectPage(id),
            var s when s.StartsWith("deck:") && long.TryParse(s[5..], out var id) => () => new DeckPage(id),
            var s when s.StartsWith("read:") && long.TryParse(s[5..], out var id) => () => new ReaderPage(id),
            var s when s.StartsWith("study:") && long.TryParse(s[6..], out var id) => () => DevSession(id, test: false),
            var s when s.StartsWith("test:") && long.TryParse(s[5..], out var id) => () => DevSession(id, test: true),
            var s when s.StartsWith("stats:") && long.TryParse(s[6..], out var id) => () => new SubjectPage(id, 1),
            _ => () => new HomePage(),
        };
        Navigate(factory, false);
    }

    /// <summary>Developer helper: --dev-dialog=subject|study|test|item|shortcuts|deck|chapter|note.</summary>
    private void OpenDevDialog(string name)
    {
        var subject = Host.Store.GetSubjects().FirstOrDefault();
        switch (name.ToLowerInvariant())
        {
            case "subject": SubjectDialog.Show(subject); break;
            case "study": if (subject != null) StudyLauncher.StudyWithOptions(StudyLauncher.Scope(subject.Id)); break;
            case "test": if (subject != null) StudyLauncher.Test(StudyLauncher.Scope(subject.Id)); break;
            case "shortcuts": ShortcutsDialog.ShowDialogFor(this); break;
            case "chapter": if (subject != null) ChapterDialog.Show(new Chapter { SubjectId = subject.Id, Number = "2" }, true); break;
            case "deck": if (subject != null && Host.Store.GetDecks(subject.Id).FirstOrDefault() is { } d) DeckDialog.Show(d, false); break;
            case "item":
                var item = subject == null ? null : Host.Store.QueryItems(new ItemQuery { SubjectId = subject.Id, Types = { ItemTypes.Single } }).FirstOrDefault();
                if (item != null) ItemEditorDialog.Edit(item);
                break;
            case "confirm": Ask.Confirm(T("Delete subject?"), T("Everything will be moved to the recycle bin."), T("Delete"), danger: true); break;
            case "update": _ = DevUpdateAsync(); break;
        }
    }

    private async Task DevUpdateAsync()
    {
        var info = await UpdateService.CheckAsync(CancellationToken.None);
        if (info == null) { Toast.Show(F("You have the latest version ({0}).", Host.Version)); return; }
        Host.AvailableUpdate = info;
        _sidebar.Invalidate();
        UpdateDialog.ShowFor(info);
    }

    private static PageBase DevSession(long subjectId, bool test)
    {
        var o = test ? Domain.Sessions.SessionOptions.DefaultTest() : Domain.Sessions.SessionOptions.DefaultFlashcards();
        o.SubjectId = subjectId;
        o.Order = Domain.Sessions.ItemOrder.PageOrder;
        o.Types = test ? ItemTypes.Questions.ToList() : ItemTypes.Studyable.ToList();
        o.MaxItems = 0;
        o.Title = Host.Store.GetSubject(subjectId)?.Name ?? "";
        var items = Data.Services.SessionBuilder.Build(Host.Store, o, Host.Settings);
        return test ? new TestPage(o, items) : new StudyPage(o, items);
    }

    private void OnLanguageChanged()
    {
        if (IsDisposed) return;
        Rebuild();
    }

    private void OnSystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || Host.Settings.Theme != ThemeMode.System) return;
        BeginInvoke(() =>
        {
            bool wasDark = Theme.IsDark;
            Theme.Apply(Host.Settings);
            if (wasDark != Theme.IsDark) Rebuild();
        });
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Toast.Reposition();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_page != null && !_page.CanLeave())
        {
            e.Cancel = true;
            return;
        }
        SavePlacement();
        L.Changed -= OnLanguageChanged;
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        base.OnFormClosing(e);
    }

    // ── window placement ─────────────────────────────────────────────

    private void RestorePlacement()
    {
        var w = Host.Settings.Window;
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        if (w != null && w.Width > 300 && w.Height > 200)
        {
            var rect = new Rectangle(w.X, w.Y, w.Width, w.Height);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect)))
            {
                Bounds = rect;
                if (w.Maximized) WindowState = FormWindowState.Maximized;
                _placementRestored = true;
                return;
            }
        }
        var size = new Size(Math.Min(S(1320), screen.Width - S(40)), Math.Min(S(860), screen.Height - S(40)));
        Bounds = new Rectangle(screen.X + (screen.Width - size.Width) / 2, screen.Y + (screen.Height - size.Height) / 2, size.Width, size.Height);
    }

    private void SavePlacement()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        Host.Settings.Window = new WindowPlacement
        {
            X = b.X, Y = b.Y, Width = b.Width, Height = b.Height,
            Maximized = WindowState == FormWindowState.Maximized,
        };
        Host.SaveSettings();
    }

    public bool PlacementRestored => _placementRestored;

    // ── keyboard ─────────────────────────────────────────────────────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.D1: Navigate(() => new HomePage()); return true;
            case Keys.Control | Keys.D2: Navigate(() => new SubjectsPage()); return true;
            case Keys.Control | Keys.D3: Navigate(() => new ReviewPage()); return true;
            case Keys.Control | Keys.K:
            case Keys.Control | Keys.F:
                if (_page is SearchPage sp) sp.FocusSearch();
                else Navigate(() => new SearchPage());
                return true;
            case Keys.Control | Keys.I: Navigate(() => new ImportPage()); return true;
            case Keys.Control | Keys.P: Navigate(() => new PromptsPage()); return true;
            case Keys.Control | Keys.Oemcomma: Navigate(() => new SettingsPage()); return true;
            case Keys.Control | Keys.N:
                if (_page?.HandleKey(keyData) == true) return true;
                SubjectsPage.CreateSubject();
                return true;
            case Keys.Control | Keys.T: Sidebar.ToggleTheme(); return true;
            case Keys.F1: ShortcutsDialog.ShowDialogFor(this); return true;
            case Keys.Alt | Keys.Left:
            case Keys.BrowserBack:
                GoBack();
                return true;
        }

        bool plainKey = (keyData & (Keys.Control | Keys.Alt)) == 0;
        if (_page != null && (!plainKey || !PageBase.IsTyping() || keyData == Keys.Escape))
        {
            if (_page.HandleKey(keyData)) return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_XBUTTONUP = 0x020C;
        if (m.Msg == WM_XBUTTONUP && ((m.WParam.ToInt64() >> 16) & 0xFFFF) == 1)
        {
            GoBack();
            return;
        }
        base.WndProc(ref m);
    }

    // ── drag & drop ──────────────────────────────────────────────────

    private void AttachDrop(Control root)
    {
        void Hook(Control c)
        {
            if (c is Form) { }
            c.AllowDrop = true;
            c.DragEnter -= OnDragEnter;
            c.DragDrop -= OnDragDrop;
            c.DragEnter += OnDragEnter;
            c.DragDrop += OnDragDrop;
            c.ControlAdded -= OnChildAdded;
            c.ControlAdded += OnChildAdded;
            foreach (Control child in c.Controls) Hook(child);
        }
        Hook(root);
    }

    private void OnChildAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control != null) AttachDrop(e.Control);
    }

    private static string[] DroppedFiles(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => File.Exists(f) && (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))).ToArray()
            : Array.Empty<string>();

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true && DroppedFiles(e).Length > 0) e.Effect = DragDropEffects.Copy;
        else if (e.Data?.GetDataPresent(DataFormats.UnicodeText) == true && _page is ImportPage) e.Effect = DragDropEffects.Copy;
        else e.Effect = DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var files = DroppedFiles(e);
        if (files.Length > 0)
        {
            if (_page?.HandleDroppedFiles(files) == true) return;
            Navigate(() => new ImportPage(files));
            return;
        }
        if (_page is ImportPage ip && e.Data?.GetData(DataFormats.UnicodeText) is string text) ip.SetText(text);
    }

    /// <summary>Files passed by a second start of the app (e.g. double-clicking a .json file).</summary>
    public void HandleExternalArgs(string payload)
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        Native.SetForegroundWindow(Handle);
        var files = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray();
        if (files.Length > 0) Navigate(() => new ImportPage(files));
    }
}
