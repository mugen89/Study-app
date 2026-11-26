using StudyApp.App.Dialogs;
using StudyApp.App.Shell;
using StudyApp.App.Updates;
using StudyApp.Data.Services;
using StudyApp.Domain.Grading;
using StudyApp.Domain.Settings;

namespace StudyApp.App.Pages;

/// <summary>All settings, grouped in sections. Every change is saved immediately.</summary>
public sealed class SettingsPage : PageBase
{
    private readonly int _section;
    private readonly AppSettings _s = Host.Settings;
    private Column? _previewHost;

    public override string NavKey => "settings";

    private static readonly string[] SectionKeys = { "Appearance", "Reading", "Study", "Data & backups", "Updates", "About & feedback" };
    private static readonly string[] SectionIcons = { Icons.Palette, Icons.Reading, Icons.Cap, Icons.Database, Icons.Update, Icons.Info };

    public SettingsPage(int section = 0)
    {
        _section = Math.Clamp(section, 0, SectionKeys.Length - 1);
        var body = UseStandardLayout(1240);
        body.Controls.Add(PageHeader(T("Settings"), T("Changes are saved automatically.")));

        var nav = new SectionNav(SectionKeys.Select(T).ToArray(), SectionIcons, _section, i => Host.Main?.Replace(() => new SettingsPage(i)));
        var content = new Column { Gap = S(18) };
        switch (_section)
        {
            case 1: BuildReading(content); break;
            case 2: BuildStudy(content); break;
            case 3: BuildData(content); break;
            case 4: BuildUpdates(content); break;
            case 5: BuildAbout(content); break;
            default: BuildAppearance(content); break;
        }
        body.Controls.Add(new TwoColumns(nav, content, 0.22f) { StackBelow = S(860) });
    }

    private static void Save() => Host.SaveSettings();

    private static void SaveAndRebuild()
    {
        Host.SaveSettings();
        Theme.Apply(Host.Settings);
        Host.Main?.Rebuild();
    }

    private static Card Section(string title, string? subtitle = null)
    {
        var card = new Card(22) { Gap = S(16) };
        card.Controls.Add(new SectionHeader(title, subtitle));
        return card;
    }

    // ───────────────────────────────────────────────────────────── appearance

    private void BuildAppearance(Column content)
    {
        var lang = Section(T("Language"), T("The language of the app. AI prompts are available in both languages."));
        var langSeg = new Segmented(L.IsBg ? 1 : 0, "English", "Български").Align(HAlign.Left);
        langSeg.SelectionChanged += (_, _) =>
        {
            _s.Language = langSeg.SelectedIndex == 1 ? L.BulgarianCode : L.English;
            Save();
            L.SetLanguage(_s.Language);
        };
        lang.Controls.Add(langSeg);
        content.Controls.Add(lang);

        var look = Section(T("Theme and colour"));
        var theme = new Segmented((int)_s.Theme, T("Follow Windows"), T("Light"), T("Dark"), T("Sepia")).Align(HAlign.Left);
        theme.SelectionChanged += (_, _) => { _s.Theme = (ThemeMode)theme.SelectedIndex; SaveAndRebuild(); };
        look.Controls.Add(Ui.Field(T("Theme"), theme, T("Sepia is a warm, paper-like theme that is easy on the eyes for long reading.")));
        var accent = new ColorSwatches(Theme.AccentPresets, Gfx.FromHex(_s.Accent, Theme.DefaultAccent));
        accent.SelectedChanged += (_, _) => { _s.Accent = Gfx.ToHex(accent.Selected); SaveAndRebuild(); };
        look.Controls.Add(Ui.Field(T("Accent colour"), accent));
        content.Controls.Add(look);

        var size = Section(T("Size and motion"));
        var scales = new[] { 90, 100, 110, 125 };
        int idx = Array.IndexOf(scales, _s.UiScale);
        var scale = new Segmented(idx < 0 ? 1 : idx, "90%", "100%", "110%", "125%").Align(HAlign.Left);
        scale.SelectionChanged += (_, _) =>
        {
            _s.UiScale = scales[scale.SelectedIndex];
            Host.SaveSettings();
            Dpi.Set(Dpi.Device, _s.UiScale / 100f);
            Fonts.Clear();
            Host.Main?.Rebuild();
        };
        size.Controls.Add(Ui.Field(T("Size of the app"), scale, T("Makes buttons, menus and labels bigger or smaller. Windows display scaling is applied as well.")));
        var anim = new Toggle(T("Animations"), _s.Animations, T("Card flips, smooth scrolling and hover effects."));
        anim.Toggled += (_, _) => { _s.Animations = anim.On; Theme.Animations = anim.On; Save(); };
        size.Controls.Add(anim);
        content.Controls.Add(size);

        var personal = Section(T("Personal"));
        var name = new InputBox(T("Your first name (optional)")) { Text = _s.UserName, Width = S(320) };
        name.Changed += (_, _) => { _s.UserName = name.Text.Trim(); Save(); };
        personal.Controls.Add(Ui.Field(T("Name for the greeting"), name.Align(HAlign.Left)));
        content.Controls.Add(personal);
    }

    // ───────────────────────────────────────────────────────────── reading

    private static readonly string[] ReadingFonts =
    {
        "Segoe UI Variable Text", "Segoe UI", "Calibri", "Aptos", "Verdana", "Tahoma", "Arial", "Trebuchet MS", "Candara", "Corbel",
        "Bahnschrift", "Georgia", "Cambria", "Constantia", "Sitka Text", "Palatino Linotype", "Book Antiqua", "Times New Roman",
        "Atkinson Hyperlegible", "OpenDyslexic", "Lexend", "Inter", "Noto Sans", "Noto Serif", "Merriweather", "Literata", "Consolas",
    };

    private void BuildReading(Column content)
    {
        var card = Section(T("Study text"), T("How questions, answers and notes look while you study. The preview updates as you change things."));

        var font = new DropDown(300) { Glyph = Icons.Font };
        var current = string.IsNullOrEmpty(_s.StudyFont) ? Fonts.Text : _s.StudyFont;
        foreach (var f in ReadingFonts.Where(Fonts.Has)) font.Add(f, f);
        if (!font.Items.Any(i => Equals(i.Value, current))) font.Add(current, current);
        font.Add(T("More fonts…"), "?");
        font.SelectedValue = current;
        font.SelectionChanged += (_, _) =>
        {
            if (font.SelectedValue as string == "?")
            {
                var picked = FontPickerDialog.Show(current);
                if (picked == null) { font.SelectedValue = current; return; }
                _s.StudyFont = picked;
            }
            else _s.StudyFont = font.SelectedValue as string ?? "";
            Fonts.SetStudyFont(_s.StudyFont);
            Save();
            Host.Main?.Replace(() => new SettingsPage(1));
        };
        card.Controls.Add(Ui.Field(T("Font"), font.Align(HAlign.Left), T("Fonts like Sitka Text, Georgia or Verdana are very readable. Dyslexia-friendly fonts (OpenDyslexic, Lexend, Atkinson Hyperlegible) appear here when installed.")));

        Slider SizeSlider(string label, float value, float min, float max, Action<float> set)
        {
            var slider = new Slider(min, max, value, 0.5) { Format = v => $"{v:0.#} pt" };
            slider.ValueChanged += (_, _) => { set((float)slider.Value); Save(); RefreshPreview(); };
            card.Controls.Add(Ui.Field(label, slider));
            return slider;
        }
        SizeSlider(T("Flashcard text size"), _s.CardFontSize, 14, 40, v => _s.CardFontSize = v);
        SizeSlider(T("Question text size"), _s.QuestionFontSize, 11, 30, v => _s.QuestionFontSize = v);
        SizeSlider(T("Answer and explanation text size"), _s.AnswerFontSize, 10, 26, v => _s.AnswerFontSize = v);
        SizeSlider(T("Notes text size"), _s.NotesFontSize, 10, 30, v => _s.NotesFontSize = v);

        var lh = new Slider(1.1, 2.3, _s.LineHeight, 0.05) { Format = v => $"{v:0.00}×" };
        lh.ValueChanged += (_, _) => { _s.LineHeight = (float)lh.Value; Save(); RefreshPreview(); };
        card.Controls.Add(Ui.Field(T("Line spacing"), lh, T("More space between lines makes long texts easier to follow. 1.5 is a good start.")));
        var width = new Slider(520, 1400, _s.ContentWidth, 20) { Format = v => $"{v:0} px" };
        width.ValueChanged += (_, _) => { _s.ContentWidth = (int)width.Value; Save(); RefreshPreview(); };
        card.Controls.Add(Ui.Field(T("Text width"), width, T("Shorter lines (about 60–80 characters) are easier to read.")));
        var center = new Toggle(T("Centre flashcard text"), _s.CenterCardText);
        center.Toggled += (_, _) => { _s.CenterCardText = center.On; Save(); RefreshPreview(); };
        card.Controls.Add(center);
        card.Controls.Add(new ButtonEx(T("Reset reading settings"), Icons.Undo, Kind.Ghost, 34).Align(HAlign.Left).OnClick(() =>
        {
            _s.ResetReadingDefaults();
            Fonts.SetStudyFont(_s.StudyFont);
            Save();
            Host.Main?.Replace(() => new SettingsPage(1));
        }));
        content.Controls.Add(card);

        var preview = Section(T("Preview"));
        _previewHost = new Column { Gap = S(14) };
        preview.Controls.Add(_previewHost);
        content.Controls.Add(preview);
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_previewHost == null) return;
        _previewHost.SuspendLayout();
        foreach (Control c in _previewHost.Controls.Cast<Control>().ToList()) { _previewHost.Controls.Remove(c); c.Dispose(); }
        bool bg = L.IsBg;
        var card = new Item
        {
            Type = ItemTypes.Flashcard,
            Prompt = bg ? "Коя кухина на сърцето има най-дебела стена?" : "Which chamber of the heart has the thickest wall?",
            Answer = bg ? "**Лявата камера** – тя изпомпва кръв към цялото тяло." : "The **left ventricle** – it pumps blood to the whole body.",
            Explanation = bg ? "По-високото налягане в големия кръг изисква по-силен мускул." : "The higher pressure of the systemic circulation needs a stronger muscle.",
            Page = "11",
        };
        int width = Math.Min(S(_s.ContentWidth), Math.Max(S(320), (_previewHost.Width > 0 ? _previewHost.Width : S(700))));
        var flash = new FlashcardView(card);
        flash.Reveal();
        _previewHost.Controls.Add(flash);
        var q = new Item
        {
            Type = ItemTypes.Single,
            Prompt = bg ? "Кой съд пренася бедна на кислород кръв към белите дробове?" : "Which vessel carries oxygen-poor blood to the lungs?",
            Data = new ItemData { Options = new() { new(bg ? "Аорта" : "Aorta"), new(bg ? "Белодробна артерия" : "Pulmonary artery"), new(bg ? "Белодробна вена" : "Pulmonary vein") }, Correct = new() { 1 } },
        };
        _previewHost.Controls.Add(new QuestionView(q, new[] { 0, 1, 2 }, new Domain.Grading.Answer(), false));
        _previewHost.Controls.Add(new RichTextView(bg
            ? "### Сърдечният цикъл\nВсеки удар започва в **синоатриалния възел**. По време на *систола* сърцето се свива, а по време на *диастола* се отпуска и се пълни с кръв.\n- Систола = свиване\n- Диастола = отпускане"
            : "### The cardiac cycle\nEach heartbeat starts in the **sinoatrial node**. During *systole* the heart contracts; during *diastole* it relaxes and fills with blood.\n- Systole = contraction\n- Diastole = relaxation",
            RichStyle.Study(_s.NotesFontSize, Theme.P.Text)));
        _ = width;
        _previewHost.ResumeLayout(true);
        LayoutProps.Relayout(_previewHost);
    }

    // ───────────────────────────────────────────────────────────── study

    private void BuildStudy(Column content)
    {
        var daily = Section(T("Daily study"));
        var goal = new Stepper(5, 1000, _s.DailyGoal, 5, 170) { Format = v => N(v, "answer", "answers") };
        goal.ValueChanged += (_, _) => { _s.DailyGoal = goal.Value; Save(); };
        var newPer = new Stepper(0, 500, _s.NewPerDay, 5, 170) { Format = v => N(v, "item", "items") };
        newPer.ValueChanged += (_, _) => { _s.NewPerDay = newPer.Value; Save(); };
        var maxRev = new Stepper(10, 2000, _s.MaxReviewsPerDay, 10, 170) { Format = v => N(v, "item", "items") };
        maxRev.ValueChanged += (_, _) => { _s.MaxReviewsPerDay = maxRev.Value; Save(); };
        var dayStart = new Stepper(0, 12, _s.DayStartHour, 1, 170) { Format = v => $"{v:00}:00" };
        dayStart.ValueChanged += (_, _) => { _s.DayStartHour = dayStart.Value; StudyDay.RolloverHour = dayStart.Value; Save(); };
        var row1 = new Row { Gap = S(24), Wrap = true, Align = VAlign.Top };
        row1.Controls.Add(Ui.Field(T("Daily goal"), goal));
        row1.Controls.Add(Ui.Field(T("New items per day"), newPer));
        row1.Controls.Add(Ui.Field(T("Max reviews per day"), maxRev));
        row1.Controls.Add(Ui.Field(T("A new day starts at"), dayStart));
        daily.Controls.Add(row1);
        daily.Controls.Add(Ui.Faint(T("New items per day and the maximum reviews apply to the smart review, per subject. \"A new day starts at\" lets late-night study count for the same day.")));
        content.Controls.Add(daily);

        var answers = Section(T("Answers and tests"));
        var strict = new Segmented((int)_s.ShortAnswerStrictness, T("Exact"), T("Normal"), T("Lenient")).Align(HAlign.Left);
        strict.SelectionChanged += (_, _) => { _s.ShortAnswerStrictness = (Strictness)strict.SelectedIndex; Save(); };
        answers.Controls.Add(Ui.Field(T("Checking typed answers"), strict, T("Normal ignores capitals and punctuation. Lenient also forgives small typos and missing accents. You always confirm the result yourself.")));
        var testsSchedule = new Toggle(T("Tests update the review schedule"), _s.TestsUpdateSchedule, T("Questions you miss in a test come back sooner in your reviews."));
        testsSchedule.Toggled += (_, _) => { _s.TestsUpdateSchedule = testsSchedule.On; Save(); };
        answers.Controls.Add(testsSchedule);
        var intervals = new Toggle(T("Show the next review time on the rating buttons"), _s.ShowIntervals);
        intervals.Toggled += (_, _) => { _s.ShowIntervals = intervals.On; Save(); };
        answers.Controls.Add(intervals);
        var confirm = new Toggle(T("Ask before leaving a session"), _s.ConfirmLeaveSession);
        confirm.Toggled += (_, _) => { _s.ConfirmLeaveSession = confirm.On; Save(); };
        answers.Controls.Add(confirm);
        content.Controls.Add(answers);

        var srs = Section(T("Spaced repetition (advanced)"), T("The app uses the SM-2 method (like Anki). The defaults work well — change them only if you know why."));
        var learning = new InputBox("1 10") { Text = _s.LearningSteps, Width = S(160) };
        learning.Changed += (_, _) => { _s.LearningSteps = learning.Text; Save(); };
        var relearning = new InputBox("10") { Text = _s.RelearningSteps, Width = S(160) };
        relearning.Changed += (_, _) => { _s.RelearningSteps = relearning.Text; Save(); };
        var grad = new Stepper(1, 30, _s.GraduatingInterval, 1, 150) { Format = v => N(v, "day", "days") };
        grad.ValueChanged += (_, _) => { _s.GraduatingInterval = grad.Value; Save(); };
        var easy = new Stepper(1, 60, _s.EasyInterval, 1, 150) { Format = v => N(v, "day", "days") };
        easy.ValueChanged += (_, _) => { _s.EasyInterval = easy.Value; Save(); };
        var ease = new Stepper(130, 400, _s.StartingEase, 10, 150) { Format = v => $"{v}%" };
        ease.ValueChanged += (_, _) => { _s.StartingEase = ease.Value; Save(); };
        var max = new Stepper(7, 3650, _s.MaxInterval, 30, 150) { Format = v => N(v, "day", "days") };
        max.ValueChanged += (_, _) => { _s.MaxInterval = max.Value; Save(); };
        var mod = new Stepper(50, 200, _s.IntervalModifier, 5, 150) { Format = v => $"{v}%" };
        mod.ValueChanged += (_, _) => { _s.IntervalModifier = mod.Value; Save(); };
        var grid = new Row { Gap = S(24), Wrap = true, Align = VAlign.Top };
        grid.Controls.Add(Ui.Field(T("Learning steps (minutes)"), learning));
        grid.Controls.Add(Ui.Field(T("Relearning steps (minutes)"), relearning));
        grid.Controls.Add(Ui.Field(T("Graduating interval"), grad));
        grid.Controls.Add(Ui.Field(T("Easy interval"), easy));
        grid.Controls.Add(Ui.Field(T("Starting ease"), ease));
        grid.Controls.Add(Ui.Field(T("Maximum interval"), max));
        grid.Controls.Add(Ui.Field(T("Interval modifier"), mod));
        srs.Controls.Add(grid);
        srs.Controls.Add(new ButtonEx(T("Reset study settings"), Icons.Undo, Kind.Ghost, 34).Align(HAlign.Left).OnClick(() =>
        {
            _s.ResetStudyDefaults();
            StudyDay.RolloverHour = _s.DayStartHour;
            Save();
            Host.Main?.Replace(() => new SettingsPage(2));
        }));
        content.Controls.Add(srs);
    }

    // ───────────────────────────────────────────────────────────── data

    private void BuildData(Column content)
    {
        var where = Section(T("Where your data is"), T("Everything is stored next to the app, so you can copy the whole folder to another computer."));
        var size = File.Exists(AppPaths.DatabasePath) ? new FileInfo(AppPaths.DatabasePath).Length : 0;
        where.Controls.Add(new TextLabel(AppPaths.DatabasePath, Fonts.Code(9.5f), Theme.P.Text));
        where.Controls.Add(Ui.Faint(F("Database size: {0:0.0} MB", size / 1048576.0)));
        where.Controls.Add(Ui.Row(10,
            new ButtonEx(T("Open data folder"), Icons.Folder, Kind.Secondary).OnClick(() => Feedback.OpenFolder(AppPaths.DataDir)),
            new ButtonEx(T("Export all subjects…"), Icons.Export, Kind.Secondary).OnClick(ExportAll)).Also(r => r.Wrap = true));
        content.Controls.Add(where);

        var backups = Section(T("Backups"), T("A copy of your database is made automatically once a day and before every update."));
        var auto = new Toggle(T("Automatic daily backup"), _s.AutoBackup);
        auto.Toggled += (_, _) => { _s.AutoBackup = auto.On; Save(); };
        backups.Controls.Add(auto);
        var keep = new Stepper(3, 100, _s.BackupsToKeep, 1, 170) { Format = v => N(v, "backup", "backups") };
        keep.ValueChanged += (_, _) => { _s.BackupsToKeep = keep.Value; Save(); };
        backups.Controls.Add(Ui.Field(T("Keep the newest automatic backups"), keep.Align(HAlign.Left), T("Backups you make yourself are never deleted automatically.")));
        backups.Controls.Add(Ui.Row(10,
            new ButtonEx(T("Back up now"), Icons.Save, Kind.Primary).OnClick(() =>
            {
                try
                {
                    BackupService.Create(Host.Store, BackupService.Manual);
                    Toast.Show(T("Backup created."));
                    Host.Main?.Replace(() => new SettingsPage(3));
                }
                catch (Exception ex) { ErrorDialog.Show(T("The backup failed."), ex); }
            }),
            new ButtonEx(T("Restore from a file…"), Icons.OpenFile, Kind.Secondary).OnClick(() =>
            {
                using var dlg = new OpenFileDialog { Filter = T("Backups") + " (*.db)|*.db", InitialDirectory = AppPaths.BackupDir };
                if (dlg.ShowDialog(Host.Main) == DialogResult.OK) Restore(dlg.FileName, Path.GetFileName(dlg.FileName));
            })).Also(r => r.Wrap = true));

        var list = BackupService.List().Take(12).ToList();
        if (list.Count > 0)
        {
            var col = new Column { Gap = S(4) };
            foreach (var b in list)
            {
                var row = new Row { Gap = S(10) };
                row.Controls.Add(Chip.Neutral(ReasonText(b.Reason)));
                row.Controls.Add(new TextLabel(L.DateTimeShort(b.Created), Fonts.Ui(9.5f), Theme.P.Text, wrap: false).Flex());
                row.Controls.Add(new TextLabel($"{b.Size / 1048576.0:0.0} MB", Fonts.Ui(9f), Theme.P.Text3, wrap: false));
                var backup = b;
                row.Controls.Add(new ButtonEx(T("Restore"), Icons.Undo, Kind.Ghost, 32).OnClick(() => Restore(backup.Path, L.DateTimeShort(backup.Created))));
                col.Controls.Add(row);
            }
            backups.Controls.Add(col);
        }
        content.Controls.Add(backups);

        var trash = Section(T("Recycle bin"));
        var days = new Stepper(1, 365, _s.RecycleBinDays, 1, 170) { Format = v => N(v, "day", "days") };
        days.ValueChanged += (_, _) => { _s.RecycleBinDays = days.Value; Save(); };
        trash.Controls.Add(Ui.Field(T("Keep deleted things for"), days.Align(HAlign.Left)));
        content.Controls.Add(trash);

        var danger = Section(T("Start over"));
        danger.Controls.Add(new ButtonEx(T("Reset all study progress…"), Icons.Refresh, Kind.Danger).Align(HAlign.Left).OnClick(() =>
        {
            if (!Ask.Confirm(T("Reset all progress?"), T("All answers, statistics and review schedules in every subject will be cleared. Your subjects, flashcards and questions stay. A backup is made first."), T("Reset everything"), danger: true))
                return;
            BackupService.Create(Host.Store, "before-reset");
            Host.Store.ResetProgress(null);
            Toast.Show(T("All progress was reset."), Tone.Info);
            Host.Main?.RefreshSidebar();
        }));
        content.Controls.Add(danger);
    }

    private static string ReasonText(string reason) => reason switch
    {
        BackupService.Manual => T("Manual"),
        BackupService.Auto => T("Automatic"),
        BackupService.BeforeRestore => T("Before restore"),
        var r when r.StartsWith(BackupService.BeforeUpdate) => T("Before update"),
        var r when r.StartsWith("before-reset") => T("Before reset"),
        _ => reason,
    };

    private static void Restore(string path, string label)
    {
        if (!Ask.Confirm(T("Restore this backup?"),
                F("Your current data will be replaced by the backup from {0}. (A copy of the current data is kept in the backups folder.) Study App will restart.", label),
                T("Restore and restart")))
            return;
        BackupService.StageRestore(path);
        Application.Restart();
        Environment.Exit(0);
    }

    private static void ExportAll()
    {
        using var dlg = new FolderBrowserDialog { Description = T("Choose a folder for the exported JSON files"), UseDescriptionForTitle = true };
        if (dlg.ShowDialog(Host.Main) != DialogResult.OK) return;
        int files = 0;
        foreach (var s in Host.Store.GetSubjects())
        {
            var chapters = Host.Store.GetChapters(s.Id).ToDictionary(c => c.Id);
            var decks = Host.Store.GetDecks(s.Id).Select(d => new Domain.Export.ExportDeck
            {
                Deck = d,
                Chapter = d.ChapterId is long cid && chapters.TryGetValue(cid, out var c) ? c : null,
                Items = Host.Store.GetItems(d.Id),
            }).ToList();
            var name = string.Concat(s.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            File.WriteAllText(Path.Combine(dlg.SelectedPath, name + ".json"), Domain.Export.JsonExporter.Export(s, decks, Host.Version), new System.Text.UTF8Encoding(false));
            files++;
        }
        Toast.Show(F("Exported {0}.", N(files, "file", "files")), Tone.Success, T("Open folder"), () => Feedback.OpenFolder(dlg.SelectedPath));
    }

    // ───────────────────────────────────────────────────────────── updates

    private void BuildUpdates(Column content)
    {
        var card = Section(T("Updates"), T("New versions are published on GitHub. Your data stays untouched when the app updates."));
        card.Controls.Add(new TextLabel(F("You have version {0}.", Host.Version), Fonts.UiSemibold(11f), Theme.P.Text));
        if (Host.AvailableUpdate is { } u)
            card.Controls.Add(new Banner(Tone.Accent, F("Version {0} is available", u.Version), null,
                new ButtonEx(T("View update"), Icons.Update, Kind.Primary, 34).OnClick(() => UpdateDialog.ShowFor(u))));
        var check = new ButtonEx(T("Check for updates now"), Icons.Sync, Kind.Primary);
        check.Click += async (_, _) =>
        {
            check.Enabled = false;
            check.Text = T("Checking…");
            check.Invalidate();
            await UpdateDialog.CheckNowAsync();
            if (check.IsDisposed) return;
            check.Enabled = true;
            check.Text = T("Check for updates now");
            check.Invalidate();
        };
        card.Controls.Add(Ui.Row(10, check,
            new ButtonEx(T("All versions"), Icons.Link, Kind.Ghost).OnClick(() => Feedback.OpenUrl(Host.RepoUrl + "/releases"))));
        var startup = new Toggle(T("Check for updates when the app starts"), _s.CheckUpdatesOnStartup, T("Only asks GitHub for the latest version number. Nothing is installed without your OK."));
        startup.Toggled += (_, _) => { _s.CheckUpdatesOnStartup = startup.On; Save(); };
        card.Controls.Add(startup);
        if (_s.SkippedVersion.Length > 0)
            card.Controls.Add(new ButtonEx(F("Stop skipping version {0}", _s.SkippedVersion), Icons.Undo, Kind.Ghost, 32).Align(HAlign.Left).OnClick(() =>
            {
                _s.SkippedVersion = "";
                Save();
                Host.Main?.Replace(() => new SettingsPage(4));
            }));
        content.Controls.Add(card);
    }

    // ───────────────────────────────────────────────────────────── about

    private void BuildAbout(Column content)
    {
        var about = Section("Study App");
        about.Controls.Add(new TextLabel(F("Version {0}", Host.Version), Fonts.UiSemibold(11f), Theme.P.Text));
        about.Controls.Add(new TextLabel(T("A self-study tool for textbooks: let ChatGPT or Claude create flashcards, tests and notes from your book, then learn them here with spaced repetition, tests and comfortable reading."), Fonts.Ui(10f), Theme.P.Text2));
        about.Controls.Add(Ui.Row(10,
            new ButtonEx(T("Keyboard shortcuts"), Icons.Keyboard, Kind.Secondary).WithTip(T("Keyboard shortcuts"), "F1").OnClick(() => ShortcutsDialog.ShowDialogFor(Host.Main)),
            new ButtonEx(T("Project page"), Icons.Link, Kind.Ghost).OnClick(() => Feedback.OpenUrl(Host.RepoUrl))).Also(r => r.Wrap = true));
        content.Controls.Add(about);

        var feedback = Section(T("Feedback"), T("Something not working, or an idea to make studying easier? Feedback shapes the next version."));
        var text = new InputBox(T("Describe the problem or idea… (what did you do, what did you expect?)"), multiline: true, lines: 5);
        feedback.Controls.Add(text);
        feedback.Controls.Add(Ui.Row(10,
            new ButtonEx(T("Send via GitHub"), Icons.Feedback, Kind.Primary).WithTip(T("Opens a pre-filled issue on GitHub (needs a free GitHub account)")).OnClick(() =>
                Feedback.OpenIssue(TextUtil.Truncate(text.Text.Trim().Length > 0 ? text.Text.Trim() : T("Feedback"), 70), text.Text.Trim())),
            new ButtonEx(T("Copy for email or chat"), Icons.Copy, Kind.Secondary).OnClick(() =>
            {
                Clipboard.SetText((text.Text.Trim() + "\r\n\r\n---\r\n" + Feedback.SystemInfo()).Trim());
                Toast.Show(T("Copied — paste it into an email or message."));
            })).Also(r => r.Wrap = true));
        content.Controls.Add(feedback);

        var trouble = Section(T("Troubleshooting"));
        trouble.Controls.Add(Ui.Muted(T("If something goes wrong, the details are written to a log file. Send it along with your feedback.")));
        trouble.Controls.Add(Ui.Row(10,
            new ButtonEx(T("Open log folder"), Icons.Folder, Kind.Secondary).OnClick(() => Feedback.OpenFolder(AppPaths.LogDir)),
            new ButtonEx(T("Copy system info"), Icons.Copy, Kind.Ghost).OnClick(() => { Clipboard.SetText(Feedback.SystemInfo()); Toast.Show(T("Copied.")); }))
            .Also(r => r.Wrap = true));
        content.Controls.Add(trouble);
    }

    /// <summary>Vertical list of settings sections.</summary>
    private sealed class SectionNav : UiControl, IHeightForWidth
    {
        private readonly string[] _titles;
        private readonly string[] _icons;
        private readonly int _selected;
        private readonly Action<int> _go;
        private int _hover = -1;

        public SectionNav(string[] titles, string[] icons, int selected, Action<int> go)
        {
            _titles = titles;
            _icons = icons;
            _selected = selected;
            _go = go;
            Cursor = Cursors.Hand;
        }

        private int RowH => S(44);

        public int HeightFor(int width) => _titles.Length * (RowH + S(4));

        private Rectangle RowRect(int i) => new(0, i * (RowH + S(4)), Width, RowH);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < _titles.Length; i++) if (RowRect(i).Contains(e.Location)) h = i;
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (int i = 0; i < _titles.Length; i++) if (RowRect(i).Contains(e.Location) && i != _selected) _go(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Smooth(g);
            var p = Theme.P;
            for (int i = 0; i < _titles.Length; i++)
            {
                var r = RowRect(i);
                bool sel = i == _selected;
                if (sel) Gfx.FillRound(g, p.AccentSoft, r, S(10));
                else if (i == _hover) Gfx.FillRound(g, Gfx.Blend(p.Bg, p.Text, 0.05f), r, S(10));
                var fore = sel ? p.AccentText : p.Text2;
                Gfx.Glyph(g, _icons[i], Fonts.Icon(11f), fore, new Rectangle(r.X + S(12), r.Y, S(22), r.Height));
                Gfx.Text(g, _titles[i], sel ? Fonts.UiSemibold(10f) : Fonts.Ui(10f), sel ? p.AccentText : p.Text, new Rectangle(r.X + S(44), r.Y, r.Width - S(52), r.Height), Gfx.Left);
            }
        }
    }
}

/// <summary>Searchable list of every installed font, each shown in its own typeface.</summary>
public static class FontPickerDialog
{
    public static string? Show(string current)
    {
        using var d = new DialogBase(T("Choose a font"), 560);
        var search = new InputBox(T("Search fonts…"), glyph: Icons.Search);
        var list = new Column { Gap = S(4) };
        string? chosen = null;
        var families = Fonts.Installed.Where(f => !f.Contains("Icons", StringComparison.OrdinalIgnoreCase) && !f.Contains("MDL2") && !f.Contains("Emoji") && !f.Contains("Symbol"))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        void Fill()
        {
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) { list.Controls.Remove(c); c.Dispose(); }
            var q = search.Text.Trim();
            foreach (var f in families.Where(f => q.Length == 0 || f.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(160))
            {
                var row = new FontRow(f, f == current);
                row.Click += (_, _) =>
                {
                    chosen = f;
                    d.DialogResult = DialogResult.OK;
                };
                list.Controls.Add(row);
            }
            list.ResumeLayout(true);
            LayoutProps.Relayout(list);
        }

        search.Changed += (_, _) => Fill();
        d.Content.Controls.Add(search);
        d.Content.Controls.Add(list);
        d.AddButton(T("Cancel"), Kind.Secondary, DialogResult.Cancel);
        Fill();
        d.Shown += (_, _) => search.Focus();
        return d.ShowDialog(Form.ActiveForm ?? Host.Main) == DialogResult.OK ? chosen : null;
    }

    private sealed class FontRow : UiControl, IHeightForWidth
    {
        private readonly string _family;
        private readonly bool _current;
        private readonly Font _font;

        public FontRow(string family, bool current)
        {
            _family = family;
            _current = current;
            _font = Fonts.GetDevice(family, 13f);
            Cursor = Cursors.Hand;
        }

        public int HeightFor(int width) => S(56);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Smooth(g);
            var p = Theme.P;
            var r = new RectangleF(0, 0, Width, Height);
            if (_current) Gfx.FillRound(g, p.AccentSoft, r, S(8));
            else if (HoverAnim.Value > 0) Gfx.FillRound(g, Gfx.Blend(p.Surface, p.Text, 0.05f * HoverAnim.Value), r, S(8));
            Gfx.Text(g, _family, Fonts.Ui(9f), p.Text2, new Rectangle(S(12), S(6), Width - S(24), S(18)), Gfx.Left);
            Gfx.Text(g, "Aa Bb Cc · Аа Бб Вв · 0123", _font, p.Text, new Rectangle(S(12), S(24), Width - S(24), S(28)), Gfx.Left);
        }
    }
}
