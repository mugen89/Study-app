using StudyApp.App.Dialogs;
using StudyApp.App.Shell;
using StudyApp.Domain.Export;

namespace StudyApp.App.Pages;

/// <summary>All subjects as cards, with search, sorting and archived subjects.</summary>
public sealed class SubjectsPage : PageBase
{
    private readonly CardGrid _grid = new() { MinCellWidth = S(270), Gap = S(18) };
    private readonly InputBox _search;
    private readonly DropDown _sort;
    private readonly CheckBoxEx _archived;
    private readonly List<Subject> _subjects;
    private readonly Dictionary<long, SubjectSummary> _summaries;
    private readonly Column _emptyHost = new();

    public override string NavKey => "subjects";

    public SubjectsPage()
    {
        var body = UseStandardLayout();
        _subjects = Host.Store.GetSubjects(includeArchived: true);
        _summaries = Host.Store.SubjectSummaries();

        var import = new ButtonEx(T("Import"), Icons.Import, Kind.Secondary).WithTip(T("Import JSON"), "Ctrl+I");
        import.Click += (_, _) => Go(() => new ImportPage());
        var add = new ButtonEx(T("New subject"), Icons.Add, Kind.Primary).WithTip(T("Create a subject"), "Ctrl+N");
        add.Click += (_, _) => CreateSubject();
        int active = _subjects.Count(s => !s.Archived);
        body.Controls.Add(PageHeader(T("Subjects"), N(active, "subject", "subjects"), false, import, add));

        _search = new InputBox(T("Filter subjects…"), glyph: Icons.Search);
        _search.Changed += (_, _) => Fill();
        _sort = new DropDown(200) { Glyph = Icons.Sort };
        _sort.Add(T("Recently studied"), 0).Add(T("Name"), 1).Add(T("Newest first"), 2);
        _sort.SelectedIndex = 0;
        _sort.SelectionChanged += (_, _) => Fill();
        _archived = new CheckBoxEx(T("Show archived"));
        _archived.Width = _archived.GetPreferredSize(Size.Empty).Width;
        _archived.CheckedChanged += (_, _) => Fill();
        var toolbar = new Row { Gap = S(12) };
        toolbar.Controls.Add(_search.Flex());
        toolbar.Controls.Add(_sort);
        if (_subjects.Any(s => s.Archived)) toolbar.Controls.Add(_archived);
        if (_subjects.Count > 0) body.Controls.Add(toolbar);

        body.Controls.Add(_grid);
        body.Controls.Add(_emptyHost);
        Fill();
    }

    public override void OnActivated() { if (_subjects.Count > 0) _search.Focus(); }

    private void Fill()
    {
        _grid.SuspendLayout();
        foreach (Control c in _grid.Controls.Cast<Control>().ToList()) { _grid.Controls.Remove(c); c.Dispose(); }
        foreach (Control c in _emptyHost.Controls.Cast<Control>().ToList()) { _emptyHost.Controls.Remove(c); c.Dispose(); }

        var q = _search.Text.Trim();
        IEnumerable<Subject> list = _subjects.Where(s => _archived.Checked || !s.Archived);
        if (q.Length > 0) list = list.Where(s => s.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || s.Source.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        list = _sort.SelectedIndex switch
        {
            1 => list.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => list.OrderByDescending(s => s.CreatedAt),
            _ => list.OrderByDescending(s => s.LastOpenedAt ?? s.CreatedAt),
        };

        foreach (var s in list)
        {
            var card = new SubjectCard(s, _summaries.GetValueOrDefault(s.Id) ?? new SubjectSummary { SubjectId = s.Id });
            card.Open += (_, _) => Go(() => new SubjectPage(s.Id));
            card.MenuRequested += (_, anchor) => ShowMenu(s, anchor);
            _grid.Controls.Add(card);
        }
        _grid.ResumeLayout(true);

        if (_subjects.Count == 0)
        {
            var newBtn = new ButtonEx(T("Create a subject"), Icons.Add, Kind.Primary, 40).OnClick(CreateSubject);
            var sample = new ButtonEx(T("Try the sample subject"), Icons.Lightbulb, Kind.Secondary, 40).OnClick(HomePage.LoadSample);
            _emptyHost.Controls.Add(new EmptyState(Icons.Library, T("No subjects yet"),
                T("Create a subject for each course or textbook — or import a JSON file and a subject is created for you."), newBtn, sample));
        }
        else if (_grid.Controls.Count == 0)
        {
            _emptyHost.Controls.Add(new EmptyState(Icons.Search, T("No matching subjects"), T("Try another search word.")));
        }
        LayoutProps.Relayout(_grid);
    }

    private static void ShowMenu(Subject s, Control anchor)
    {
        Menus.Show(anchor,
            Menus.Item(T("Open"), Icons.ChevronRight, () => Go(() => new SubjectPage(s.Id))),
            Menus.Item(T("Study due items"), Icons.Play, () => StudyLauncher.QuickStudy(StudyLauncher.Scope(s.Id))),
            Menus.Item(T("Take a test"), Icons.CheckCircle, () => StudyLauncher.Test(StudyLauncher.Scope(s.Id))),
            Menus.Separator(),
            Menus.Item(T("Edit"), Icons.Edit, () => EditSubject(s)),
            Menus.Item(s.Archived ? T("Unarchive") : T("Archive"), Icons.Archive, () =>
            {
                Host.Store.SetSubjectArchived(s.Id, !s.Archived);
                Toast.Show(s.Archived ? T("Subject restored from the archive.") : T("Subject archived. Turn on \"Show archived\" to see it."), Tone.Info);
                Host.Main?.ReloadPage();
            }),
            Menus.Item(T("Export to JSON…"), Icons.Export, () => ExportSubject(s)),
            Menus.Separator(),
            Menus.Item(T("Delete"), Icons.Delete, () => DeleteSubject(s), danger: true));
    }

    public static void CreateSubject()
    {
        var subject = SubjectDialog.Show();
        if (subject == null) return;
        Host.Store.CreateSubject(subject);
        Toast.Show(F("Subject \"{0}\" created.", subject.Name));
        Host.Main?.Navigate(() => new SubjectPage(subject.Id));
    }

    public static void EditSubject(Subject s)
    {
        var copy = Host.Store.GetSubject(s.Id) ?? s;
        if (SubjectDialog.Show(copy) == null) return;
        Host.Store.UpdateSubject(copy);
        Host.Main?.ReloadPage();
    }

    public static void DeleteSubject(Subject s, bool goToList = false)
    {
        if (!Ask.Confirm(T("Delete subject?"), F("\"{0}\" and everything in it will be moved to the recycle bin. You can restore it from there.", s.Name), T("Delete"), danger: true))
            return;
        Host.Store.DeleteSubject(s.Id);
        Toast.Show(F("\"{0}\" moved to the recycle bin.", s.Name), Tone.Info, T("Undo"), () =>
        {
            Host.Store.RestoreSubject(s.Id);
            Host.Main?.ReloadPage();
        });
        if (goToList) Host.Main?.Navigate(() => new SubjectsPage(), false);
        else Host.Main?.ReloadPage();
    }

    public static void ExportSubject(Subject s, long? onlyDeckId = null, long? onlyChapterId = null)
    {
        var chapters = Host.Store.GetChapters(s.Id).ToDictionary(c => c.Id);
        var decks = Host.Store.GetDecks(s.Id)
            .Where(d => onlyDeckId == null || d.Id == onlyDeckId)
            .Where(d => onlyChapterId == null || d.ChapterId == onlyChapterId)
            .Select(d => new ExportDeck
            {
                Deck = d,
                Chapter = d.ChapterId is long cid && chapters.TryGetValue(cid, out var c) ? c : null,
                Items = Host.Store.GetItems(d.Id),
            }).ToList();
        var name = onlyDeckId != null ? decks.FirstOrDefault()?.Deck.Title ?? s.Name : s.Name;
        using var dlg = new SaveFileDialog
        {
            Title = T("Export to JSON"),
            Filter = T("JSON files") + " (*.json)|*.json",
            FileName = SafeFileName(name) + ".json",
        };
        if (dlg.ShowDialog(Host.Main) != DialogResult.OK) return;
        File.WriteAllText(dlg.FileName, JsonExporter.Export(s, decks, Host.Version), new System.Text.UTF8Encoding(false));
        Toast.Show(F("Exported {0}.", N(decks.Sum(d => d.Items.Count), "item", "items")), Tone.Success, T("Show file"),
            () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{dlg.FileName}\""));
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return clean.Length == 0 ? "export" : clean;
    }
}
