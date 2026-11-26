using StudyApp.App.Dialogs;
using StudyApp.App.Shell;

namespace StudyApp.App.Pages;

/// <summary>Browse, search, edit, flag and delete the items of one deck.</summary>
public sealed class DeckPage : PageBase
{
    private const int PageSize = 120;
    private readonly Deck? _deck;
    private readonly Subject? _subject;
    private readonly InputBox _search;
    private readonly Segmented _filter;
    private readonly Column _list = new() { Gap = S(10) };
    private readonly TextLabel _count;
    private List<Item> _items = new();
    private int _shown;

    public override string NavKey => "subjects";

    public DeckPage(long deckId, int filter = 0)
    {
        var body = UseStandardLayout();
        _deck = Host.Store.GetDeck(deckId);
        _search = new InputBox(T("Search in this deck…"), glyph: Icons.Search);
        _filter = new Segmented(filter, T("All"), T("Flagged"), T("Missed"), T("New"), T("Wrong in source"));
        _count = new TextLabel("", Fonts.Ui(9.5f), Theme.P.Text2, wrap: false);
        if (_deck == null)
        {
            body.Controls.Add(new EmptyState(Icons.Warning, T("Deck not found"), T("It may have been deleted.")));
            return;
        }
        _subject = Host.Store.GetSubject(_deck.SubjectId);
        var chapter = _deck.ChapterId is long cid ? Host.Store.GetChapter(cid) : null;
        var crumbs = string.Join("  ›  ", new[] { _subject?.Name, chapter?.DisplayName }.Where(x => !string.IsNullOrEmpty(x)));

        var deck = _deck;
        var actions = new List<Control>();
        if (deck.Kind == DeckKind.Notes)
            actions.Add(new ButtonEx(T("Read"), Icons.Reading, Kind.Primary).OnClick(() => StudyLauncher.Read(deck.Id)));
        else
        {
            actions.Add(new ButtonEx(T("Study"), Icons.Play, Kind.Primary).WithTip(T("Smart review of this deck")).OnClick(() => StudyLauncher.QuickStudy(StudyLauncher.Scope(deck.SubjectId, null, deck.Id))));
            actions.Add(new ButtonEx(T("Test"), Icons.CheckCircle, Kind.Secondary).OnClick(() => StudyLauncher.Test(StudyLauncher.Scope(deck.SubjectId, null, deck.Id))));
        }
        actions.Add(new ButtonEx(T("Add item"), Icons.Add, Kind.Secondary).WithTip(T("Add an item by hand"), "Ctrl+N").OnClick(AddItem));
        var more = ButtonEx.Icon(Icons.More, T("Deck actions"), null, Kind.Secondary, 36);
        more.Click += (_, _) => Menus.Show(more,
            Menus.Item(T("Study with options…"), Icons.Sliders, () => StudyLauncher.StudyWithOptions(StudyLauncher.Scope(deck.SubjectId, null, deck.Id))),
            Menus.Item(T("Edit deck"), Icons.Edit, () => { if (DeckDialog.Show(deck, false) != null) { Host.Store.UpdateDeck(deck); Host.Main?.ReloadPage(); } }),
            Menus.Item(T("Export to JSON…"), Icons.Export, () => { if (_subject != null) SubjectsPage.ExportSubject(_subject, onlyDeckId: deck.Id); }),
            Menus.Separator(),
            Menus.Item(T("Delete deck"), Icons.Delete, () => DeleteDeck(deck, reloadOnly: false), danger: true));
        actions.Add(more);
        body.Controls.Add(PageHeader(deck.Title, crumbs, true, actions.ToArray()));

        _search.Changed += (_, _) => Refill();
        _filter.SelectionChanged += (_, _) => Refill();
        var toolbar = new Row { Gap = S(12), Wrap = true };
        toolbar.Controls.Add(_search.Also(s => s.Width = S(320)).Flex());
        toolbar.Controls.Add(_filter);
        body.Controls.Add(toolbar);
        body.Controls.Add(_count);
        body.Controls.Add(_list);
        Refill();
    }

    public override bool HandleKey(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.N)) { AddItem(); return true; }
        return false;
    }

    private void AddItem()
    {
        if (_deck == null) return;
        if (ItemEditorDialog.Create(_deck) != null)
        {
            Toast.Show(T("Item added."));
            Host.Main?.ReloadPage();
        }
    }

    private void Refill()
    {
        if (_deck == null) return;
        var all = Host.Store.QueryItems(new ItemQuery { DeckIds = { _deck.Id }, WrongInSource = null, IncludeSuspended = true, OrderBy = "i.sort_order, i.id" });
        var q = _search.Text.Trim();
        IEnumerable<Item> list = all;
        if (q.Length > 0)
            list = list.Where(i => i.Prompt.Contains(q, StringComparison.CurrentCultureIgnoreCase) || i.Answer.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                   || i.Explanation.Contains(q, StringComparison.CurrentCultureIgnoreCase) || i.Page.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                   || i.Tags.Any(t => t.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                                   || (i.Data.Options?.Any(o => o.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)) ?? false));
        list = _filter.SelectedIndex switch
        {
            1 => list.Where(i => i.Flagged),
            2 => list.Where(i => i.LastResult is Result.Missed or Result.Partial),
            3 => list.Where(i => i.SeenCount == 0 && i.Type != ItemTypes.Note),
            4 => list.Where(i => i.WrongInSource),
            _ => list,
        };
        _items = list.ToList();
        _count.Text = _items.Count == all.Count ? N(all.Count, "item", "items") : F("{0} of {1}", _items.Count, N(all.Count, "item", "items"));
        _count.Invalidate();

        _list.SuspendLayout();
        foreach (Control c in _list.Controls.Cast<Control>().ToList()) { _list.Controls.Remove(c); c.Dispose(); }
        _shown = 0;
        ShowMore();
        if (_items.Count == 0)
        {
            if (all.Count == 0)
                _list.Controls.Add(new EmptyState(Icons.Layers, T("This deck is empty"), T("Import a JSON file into it, or add items by hand."),
                    new ButtonEx(T("Add item"), Icons.Add, Kind.Primary).OnClick(AddItem),
                    new ButtonEx(T("Import JSON"), Icons.Import, Kind.Secondary).OnClick(() => Go(() => new ImportPage(subjectId: _deck.SubjectId, chapterId: _deck.ChapterId)))));
            else
                _list.Controls.Add(new EmptyState(Icons.Filter, T("Nothing matches"), T("Try another search or filter.")));
        }
        _list.ResumeLayout(true);
        LayoutProps.Relayout(_list);
    }

    private void ShowMore()
    {
        foreach (Control c in _list.Controls.Cast<Control>().Where(c => c is ButtonEx).ToList()) { _list.Controls.Remove(c); c.Dispose(); }
        foreach (var item in _items.Skip(_shown).Take(PageSize))
        {
            var row = new ItemRow(item);
            row.Deleted += (_, _) =>
            {
                _list.Controls.Remove(row);
                row.Dispose();
                LayoutProps.Relayout(_list);
            };
            _list.Controls.Add(row);
        }
        _shown = Math.Min(_items.Count, _shown + PageSize);
        if (_shown < _items.Count)
        {
            var moreBtn = new ButtonEx(F("Show {0} more", Math.Min(PageSize, _items.Count - _shown)), Icons.ChevronDown, Kind.Secondary).Align(HAlign.Center);
            moreBtn.Click += (_, _) => { _list.SuspendLayout(); ShowMore(); _list.ResumeLayout(true); LayoutProps.Relayout(_list); };
            _list.Controls.Add(moreBtn);
        }
    }

    public static void DeleteDeck(Deck deck, bool reloadOnly)
    {
        if (!Ask.Confirm(T("Delete deck?"), F("\"{0}\" will be moved to the recycle bin.", deck.Title), T("Delete"), danger: true)) return;
        Host.Store.DeleteDeck(deck.Id);
        Toast.Show(T("Deck moved to the recycle bin."), Tone.Info, T("Undo"), () => { Host.Store.RestoreDeck(deck.Id); Host.Main?.ReloadPage(); });
        if (reloadOnly) Host.Main?.ReloadPage();
        else Host.Main?.GoBack();
    }
}
