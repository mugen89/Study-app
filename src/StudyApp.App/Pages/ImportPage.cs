using StudyApp.App.Shell;
using StudyApp.Data.Services;
using StudyApp.Domain.Import;
using StudyApp.Domain.Prompts;

namespace StudyApp.App.Pages;

/// <summary>Import JSON from the AI: paste, browse or drag &amp; drop → validate → preview → choose target → import.</summary>
public sealed class ImportPage : PageBase
{
    private readonly InputBox _json;
    private readonly DropZone _drop;
    private readonly Column _files = new() { Gap = S(6) };
    private readonly Column _check = new() { Gap = S(12) };
    private readonly Column _target = new() { Gap = S(14) };
    private readonly Card _targetCard;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 450 };
    private readonly List<(string Name, string Text)> _loaded = new();
    private readonly long? _presetSubject;
    private readonly long? _presetChapter;

    private ImportPackage? _package;
    private DropDown? _subject;
    private InputBox? _newName;
    private DropDown? _chapter;
    private ChoiceList? _duplicates;
    private ButtonEx? _importButton;
    private bool _suppressTextEvent;

    public override string NavKey => "import";

    public ImportPage(string[]? files = null, string? text = null, long? subjectId = null, long? chapterId = null)
    {
        _presetSubject = subjectId;
        _presetChapter = chapterId;
        var body = UseStandardLayout(1240);
        var promptBtn = new ButtonEx(T("Get a prompt"), Icons.Chat, Kind.Secondary).WithTip(T("Prompts that make the AI answer in the right format"), "Ctrl+P");
        promptBtn.Click += (_, _) => Go(() => new PromptsPage());
        body.Controls.Add(PageHeader(T("Import"), T("Bring in flashcards, tests and notes that ChatGPT or Claude made for you."), false, promptBtn));

        // ── 1. Source
        var source = new Card(22) { Gap = S(14) };
        source.Controls.Add(new SectionHeader(T("1. Add the AI's answer")));
        var paste = new ButtonEx(T("Paste from clipboard"), Icons.Paste, Kind.Primary, 42).WithTip(T("Paste the copied reply"), "Ctrl+V");
        paste.Click += (_, _) => PasteFromClipboard();
        var browse = new ButtonEx(T("Browse files…"), Icons.OpenFile, Kind.Secondary, 42).WithTip(T("Choose one or more .json files"), "Ctrl+O");
        browse.Click += (_, _) => Browse();
        source.Controls.Add(Ui.Row(10, paste, browse).Also(r => r.Wrap = true));
        _drop = new DropZone();
        _drop.Click += (_, _) => Browse();
        source.Controls.Add(_drop);
        source.Controls.Add(_files);
        source.Controls.Add(new TextLabel(T("…or paste or type the JSON here. The whole chat reply is fine — the app finds the JSON in it."), Fonts.Ui(9.5f), Theme.P.Text2));
        _json = new InputBox(T("{ \"schemaVersion\": 1, \"items\": [ … ] }"), multiline: true, lines: 14, mono: true);
        _json.Changed += (_, _) =>
        {
            if (_suppressTextEvent) return;
            if (_loaded.Count > 0) { _loaded.Clear(); RenderFiles(); }
            _debounce.Stop();
            _debounce.Start();
        };
        source.Controls.Add(_json);
        _debounce.Tick += (_, _) => { _debounce.Stop(); Parse(); };

        // ── 2 + 3. Check and target
        var checkCard = new Card(22) { Gap = S(12) };
        checkCard.Controls.Add(new SectionHeader(T("2. Check")));
        checkCard.Controls.Add(_check);
        _targetCard = new Card(22) { Gap = S(12), Visible = false };
        _targetCard.Controls.Add(new SectionHeader(T("3. Choose where it goes")));
        _targetCard.Controls.Add(_target);
        var right = Ui.Stack(18, checkCard, _targetCard);

        body.Controls.Add(new TwoColumns(source, right, 0.52f) { StackBelow = S(980) });

        if (files is { Length: > 0 }) LoadFiles(files);
        else if (!string.IsNullOrEmpty(text)) SetText(text);
        else ShowIdle();
    }

    public override void OnDeactivated()
    {
        _debounce.Stop();
        _debounce.Dispose();
    }

    public override bool HandleKey(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.V) && !IsTyping()) { PasteFromClipboard(); return true; }
        if (keyData == (Keys.Control | Keys.O)) { Browse(); return true; }
        if (keyData == (Keys.Control | Keys.Enter) && _importButton?.Enabled == true) { _importButton.PerformClick(); return true; }
        return false;
    }

    public override bool HandleDroppedFiles(string[] files)
    {
        LoadFiles(files);
        return true;
    }

    public void SetText(string text)
    {
        _loaded.Clear();
        RenderFiles();
        _suppressTextEvent = true;
        _json.Text = text;
        _suppressTextEvent = false;
        Parse();
    }

    private void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                LoadFiles(Clipboard.GetFileDropList().Cast<string>().ToArray());
                return;
            }
            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                Toast.Show(T("The clipboard is empty. Copy the AI's reply first."), Tone.Warning);
                return;
            }
            SetText(text);
        }
        catch (Exception ex)
        {
            Log.Warn("Clipboard read failed", ex);
            Toast.Show(T("Could not read the clipboard. Try again."), Tone.Warning);
        }
    }

    private void Browse()
    {
        using var dlg = new OpenFileDialog
        {
            Title = T("Choose JSON files"),
            Filter = T("JSON files") + " (*.json)|*.json|" + T("Text files") + " (*.txt;*.md)|*.txt;*.md|" + T("All files") + " (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(Host.Main) == DialogResult.OK) LoadFiles(dlg.FileNames);
    }

    private void LoadFiles(string[] paths)
    {
        _loaded.Clear();
        foreach (var p in paths)
        {
            try
            {
                var info = new FileInfo(p);
                if (info.Length > 30 * 1024 * 1024) { Toast.Show(F("{0} is too large.", info.Name), Tone.Warning); continue; }
                _loaded.Add((info.Name, File.ReadAllText(p)));
            }
            catch (Exception ex)
            {
                Toast.Show(F("Could not read {0}: {1}", Path.GetFileName(p), ex.Message), Tone.Danger);
            }
        }
        _suppressTextEvent = true;
        _json.Text = _loaded.Count == 1 ? _loaded[0].Text : "";
        _suppressTextEvent = false;
        RenderFiles();
        Parse();
    }

    private void RenderFiles()
    {
        _files.SuspendLayout();
        foreach (Control c in _files.Controls.Cast<Control>().ToList()) { _files.Controls.Remove(c); c.Dispose(); }
        foreach (var (name, text) in _loaded)
        {
            var row = new Row { Gap = S(8) };
            row.Controls.Add(Chip.Accent(name, Icons.Document));
            row.Controls.Add(new TextLabel(F("{0:0.0} KB", text.Length / 1024.0), Fonts.Ui(9f), Theme.P.Text3, wrap: false));
            _files.Controls.Add(row);
        }
        _files.ResumeLayout(true);
        LayoutProps.Relayout(_files);
    }

    // ── parsing & preview ────────────────────────────────────────────

    private void ShowIdle()
    {
        Clear(_check);
        _check.Controls.Add(new Banner(Tone.Neutral, T("Waiting for JSON"),
            T("Paste the AI's reply, browse for a file, or drag files onto this window. The preview appears here.")));
        _targetCard.Visible = false;
        _package = null;
        LayoutProps.Relayout(_check);
    }

    private void Parse()
    {
        var sources = _loaded.Count > 0 ? _loaded : (_json.Text.Trim().Length > 0 ? new List<(string, string)> { ("", _json.Text) } : new());
        if (sources.Count == 0)
        {
            ShowIdle();
            return;
        }

        var results = sources.Select(s => ImportParser.Parse(s.Item2, s.Item1)).ToList();
        var good = results.Where(r => r.Package != null && r.Package.ItemCount > 0).ToList();
        _package = good.Count > 0 ? ImportPackage.Merge(good.Select(r => r.Package!)) : null;

        Clear(_check);
        var failed = results.Where(r => r.Error != null && (r.Package == null || r.Package.ItemCount == 0)).ToList();
        foreach (var r in failed) _check.Controls.Add(ErrorBanner(r, results.Count > 1));

        if (_package != null)
        {
            var counts = _package.CountsByType();
            int errors = results.Sum(r => r.Issues.Count(i => i.Level == IssueLevel.Error));
            var title = F("Ready to import {0}", N(_package.ItemCount, "item", "items"));
            var detail = errors > 0 ? F("{0} could not be read and will be skipped (see below).", N(errors, "item", "items")) : T("Everything looks good.");
            _check.Controls.Add(new Banner(errors > 0 ? Tone.Warning : Tone.Success, title, detail));

            var chips = new Row { Gap = S(8), Wrap = true };
            foreach (var t in ItemTypes.All)
                if (counts.TryGetValue(t, out var n)) chips.Controls.Add(Chip.Neutral($"{ItemTypes.PluralName(t)}: {n}", ItemRow.TypeVisual(t).Glyph));
            _check.Controls.Add(chips);

            var issues = results.SelectMany(r => r.Issues.Select(i => (r.FileName, Issue: i))).ToList();
            if (issues.Count > 0) _check.Controls.Add(IssueList(issues, results.Count > 1));
            BuildTarget();
        }
        else
        {
            _targetCard.Visible = false;
        }
        LayoutProps.Relayout(_check);
    }

    private Control ErrorBanner(ParseResult r, bool showFile)
    {
        var text = r.Error ?? "";
        if (r.ErrorSnippet is { Length: > 0 } snip) text += "\n" + T("Near:") + " " + snip;
        Host.LastImportError = (r.Error ?? "") + (r.ErrorLine.HasValue ? $" (line {r.ErrorLine}, column {r.ErrorColumn})" : "") +
                               (r.ErrorSnippet is { Length: > 0 } s ? "\n" + s : "");
        var fix = new ButtonEx(T("Copy a \"fix it\" prompt"), Icons.Copy, Kind.Secondary, 34).WithTip(T("Paste this into the AI chat and it will resend corrected JSON"));
        fix.Click += (_, _) =>
        {
            var template = Host.Store.GetTemplates(L.Lang).FirstOrDefault(t => t.Key == PromptDefaults.Fix);
            var o = Host.Settings.PromptOptions.Clone();
            o.ErrorText = Host.LastImportError;
            var prompt = PromptRenderer.Render(template?.Body ?? PromptDefaults.Find(PromptDefaults.Fix, L.Lang)!.Body, o, L.Lang);
            Clipboard.SetText(prompt.Replace("\n", "\r\n"));
            Toast.Show(T("Prompt copied — paste it into your AI chat."), Tone.Success);
        };
        return new Banner(Tone.Danger, (showFile && r.FileName.Length > 0 ? r.FileName + ": " : "") + T("Could not read this JSON"), text, fix);
    }

    private static Control IssueList(List<(string File, ImportIssue Issue)> issues, bool showFile)
    {
        var col = new Column { Gap = S(6) };
        int errors = issues.Count(i => i.Issue.Level == IssueLevel.Error);
        int warnings = issues.Count(i => i.Issue.Level == IssueLevel.Warning);
        col.Controls.Add(new TextLabel(F("Notes from the check: {0}, {1}", N(errors, "error", "errors"), N(warnings, "warning", "warnings")), Fonts.UiSemibold(9.5f), Theme.P.Text2));
        foreach (var (file, issue) in issues.OrderByDescending(i => i.Issue.Level).Take(40))
        {
            var row = new Row { Gap = S(8), Align = VAlign.Top };
            var (glyph, color) = issue.Level switch
            {
                IssueLevel.Error => (Icons.ErrorCircle, Theme.P.Danger),
                IssueLevel.Warning => (Icons.Warning, Theme.P.Warning),
                _ => (Icons.Info, Theme.P.Info),
            };
            row.Controls.Add(new GlyphLabel(glyph, color));
            row.Controls.Add(new TextLabel((showFile && file.Length > 0 ? file + ": " : "") + issue.Message, Fonts.Ui(9.5f), Theme.P.Text).Flex());
            col.Controls.Add(row);
        }
        if (issues.Count > 40) col.Controls.Add(Ui.Faint(F("…and {0} more", issues.Count - 40)));
        return col;
    }

    private void BuildTarget()
    {
        if (_package == null) return;
        Clear(_target);
        var subjects = Host.Store.GetSubjects();

        _subject = new DropDown(380) { Glyph = Icons.Library };
        var suggestedName = string.IsNullOrWhiteSpace(_package.Subject) ? T("Imported") : _package.Subject;
        _subject.Add(F("New subject: {0}", suggestedName), -1L);
        foreach (var s in subjects) _subject.Add(s.Name, s.Id);
        var match = subjects.FirstOrDefault(s => s.Id == _presetSubject)
                    ?? subjects.FirstOrDefault(s => string.Equals(s.Name, _package.Subject, StringComparison.CurrentCultureIgnoreCase));
        _subject.SelectedValue = match?.Id ?? -1L;
        _newName = new InputBox(T("Name of the new subject")) { Text = suggestedName };

        _subject.SelectionChanged += (_, _) => { RefreshChapters(); RefreshAnalysis(); };
        _target.Controls.Add(Ui.Field(T("Subject"), _subject.Align(HAlign.Left)));
        _target.Controls.Add(Ui.Field(T("New subject name"), _newName).Also(f => f.Visible = (long)_subject.SelectedValue! == -1L).Also(f => f.Name = "newNameField"));

        _chapter = new DropDown(380) { Glyph = Icons.Layers };
        _chapter.SelectionChanged += (_, _) => RefreshAnalysis();
        _target.Controls.Add(Ui.Field(T("Chapter"), _chapter.Align(HAlign.Left), T("Chapters named in the file are matched by number or title, or created.")));
        RefreshChapters();

        _target.Controls.Add(new Column { Name = "analysis", Gap = S(10) });

        _importButton = new ButtonEx(T("Import"), Icons.Import, Kind.Primary, 46) { FontPt = 11f }.WithTip(T("Import"), "Ctrl+Enter");
        _importButton.Click += (_, _) => DoImport();
        _target.Controls.Add(_importButton.Align(HAlign.Left));
        _targetCard.Visible = true;
        RefreshAnalysis();
    }

    private long SelectedSubjectId => _subject?.SelectedValue is long id ? id : -1;

    private void RefreshChapters()
    {
        if (_chapter == null || _package == null) return;
        var nameField = _target.Controls.Cast<Control>().FirstOrDefault(c => c.Name == "newNameField");
        if (nameField != null) nameField.Visible = SelectedSubjectId == -1;

        _chapter.Items.Clear();
        bool fileHasChapters = _package.Decks.Any(d => d.Chapter != null && !d.Chapter.IsEmpty);
        if (fileHasChapters) _chapter.Add(T("Use the chapters from the file"), "file");
        _chapter.Add(T("No chapter"), "none");
        if (SelectedSubjectId > 0)
            foreach (var c in Host.Store.GetChapters(SelectedSubjectId)) _chapter.Add(c.DisplayName, c.Id);
        if (_presetChapter.HasValue && SelectedSubjectId == _presetSubject && _chapter.Items.Any(i => Equals(i.Value, _presetChapter.Value)))
            _chapter.SelectedValue = _presetChapter.Value;
        else _chapter.SelectedIndex = 0;
        _chapter.Invalidate();
        LayoutProps.Relayout(_target);
    }

    private ImportPlan Plan()
    {
        var plan = new ImportPlan
        {
            Package = _package!,
            SubjectId = SelectedSubjectId > 0 ? SelectedSubjectId : null,
            NewSubjectName = _newName?.Text.Trim() ?? "",
            NewSubjectSource = _package!.Source,
            NewSubjectColor = Theme.AccentPresets[Host.Store.GetSubjects().Count % Theme.AccentPresets.Length],
        };
        switch (_chapter?.SelectedValue)
        {
            case "file": plan.ChapterMode = ChapterTarget.FromFile; break;
            case long cid: plan.ChapterMode = ChapterTarget.Existing; plan.ChapterId = cid; break;
            default: plan.ChapterMode = ChapterTarget.None; break;
        }
        plan.Duplicates = _duplicates == null ? DuplicatePolicy.Skip : (DuplicatePolicy)_duplicates.SelectedIndex;
        return plan;
    }

    private void RefreshAnalysis()
    {
        if (_package == null) return;
        var host = _target.Controls.Cast<Control>().FirstOrDefault(c => c.Name == "analysis") as Column;
        if (host == null) return;
        int dupChoice = _duplicates?.SelectedIndex ?? 0;
        Clear(host);
        var analysis = ImportService.Analyze(Host.Store, Plan());

        host.Controls.Add(new TextLabel(T("Decks"), Fonts.UiSemibold(9.5f), Theme.P.Text2));
        foreach (var d in analysis.Decks)
        {
            var row = new Row { Gap = S(10) };
            var kind = d.Deck.EffectiveKind;
            row.Controls.Add(new GlyphLabel(kind == DeckKind.Test ? Icons.CheckCircle : kind == DeckKind.Notes ? Icons.Reading : Icons.Layers, Theme.P.Accent));
            var col = new Column { Gap = S(2) }.Flex();
            col.Controls.Add(new TextLabel(d.Title, Fonts.UiSemibold(10f), Theme.P.Text, wrap: false));
            col.Controls.Add(new TextLabel($"{d.ChapterLabel}  ·  {N(d.Deck.Items.Count, "item", "items")}" + (d.MergesIntoExisting ? "  ·  " + T("adds to the existing deck") : ""), Fonts.Ui(9f), Theme.P.Text2, wrap: false));
            row.Controls.Add(col);
            host.Controls.Add(row);
        }

        _duplicates = null;
        int dups = analysis.DuplicatesExisting + analysis.DuplicatesInFile;
        if (dups > 0)
        {
            host.Controls.Add(new TextLabel(F("{0} already exist in this subject", N(dups, "duplicate", "duplicates")), Fonts.UiSemibold(9.5f), Theme.P.Warning));
            _duplicates = new ChoiceList(dupChoice,
                (T("Skip duplicates"), T("Keep what you already have (recommended).")),
                (T("Replace with the new version"), T("Update the text; your study progress is kept.")),
                (T("Keep both"), T("Import them again as separate items.")));
            host.Controls.Add(_duplicates);
        }
        int toImport = dups > 0 && dupChoice == 0 ? analysis.Total - dups : analysis.Total;
        if (_importButton != null)
        {
            _importButton.Text = toImport > 0 ? F("Import {0}", N(toImport, "item", "items")) : T("Import");
            _importButton.Width = _importButton.GetPreferredSize(Size.Empty).Width;
            _importButton.Invalidate();
        }
        LayoutProps.Relayout(host);
    }

    private void DoImport()
    {
        if (_package == null) return;
        try
        {
            var plan = Plan();
            if (plan.SubjectId == null && string.IsNullOrWhiteSpace(plan.NewSubjectName))
            {
                _newName?.Focus();
                return;
            }
            var outcome = ImportService.Execute(Host.Store, plan);
            var msg = F("Imported {0}.", N(outcome.Added, "item", "items"));
            if (outcome.Replaced > 0) msg += " " + F("{0} updated.", N(outcome.Replaced, "item", "items"));
            if (outcome.Skipped > 0) msg += " " + F("{0} skipped.", N(outcome.Skipped, "duplicate", "duplicates"));
            Toast.Show(msg, Tone.Success);
            Host.Main?.RefreshSidebar();
            Go(() => new SubjectPage(outcome.SubjectId));
        }
        catch (Exception ex)
        {
            Dialogs.ErrorDialog.Show(T("The import failed. Nothing was changed."), ex);
        }
    }

    private static void Clear(Control c)
    {
        c.SuspendLayout();
        foreach (Control child in c.Controls.Cast<Control>().ToList()) { c.Controls.Remove(child); child.Dispose(); }
        c.ResumeLayout(false);
    }

    /// <summary>Small coloured icon used in lists.</summary>
    private sealed class GlyphLabel : UiControl
    {
        private readonly string _glyph;
        private readonly Color _color;

        public GlyphLabel(string glyph, Color color)
        {
            _glyph = glyph;
            _color = color;
            Size = new Size(S(22), S(22));
        }

        public override Size GetPreferredSize(Size proposedSize) => Size;

        protected override void OnPaint(PaintEventArgs e) => Gfx.Glyph(e.Graphics, _glyph, Fonts.Icon(10.5f), _color, ClientRectangle);
    }

    /// <summary>Dashed drop area that lights up while files are dragged over the window.</summary>
    private sealed class DropZone : UiControl, IHeightForWidth
    {
        public DropZone()
        {
            Cursor = Cursors.Hand;
            Height = S(130);
        }

        public int HeightFor(int width) => S(130);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Smooth(g);
            var p = Theme.P;
            float h = HoverAnim.Value;
            var r = new RectangleF(Sf(1), Sf(1), Width - Sf(2), Height - Sf(2));
            Gfx.FillRound(g, Gfx.Blend(p.Surface2, p.AccentSoft, h), r, S(14));
            using (var path = Gfx.Round(r, S(14)))
            using (var pen = new Pen(Gfx.Blend(p.BorderStrong, p.Accent, h), Sf(1.6f)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                g.DrawPath(pen, path);
            Gfx.Glyph(g, Icons.Download, Fonts.Icon(20f), Gfx.Blend(p.Text3, p.Accent, h), new Rectangle(0, S(18), Width, S(44)));
            Gfx.Text(g, T("Drag & drop .json files here"), Fonts.UiSemibold(10.5f), p.Text, new Rectangle(0, S(66), Width, S(22)), Gfx.Center);
            Gfx.Text(g, T("or click to browse — several files at once are fine"), Fonts.Ui(9f), p.Text2, new Rectangle(0, S(90), Width, S(20)), Gfx.Center);
        }
    }
}
