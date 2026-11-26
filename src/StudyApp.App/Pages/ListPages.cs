using StudyApp.App.Dialogs;
using StudyApp.App.Shell;
using StudyApp.Domain.Prompts;

namespace StudyApp.App.Pages;

/// <summary>Everything that needs attention: due today, recently missed, flagged, wrong in source.</summary>
public sealed class ReviewPage : PageBase
{
    public override string NavKey => "review";

    public ReviewPage(int tab = 0)
    {
        var body = UseStandardLayout();
        var due = Host.Store.QueryItems(new ItemQuery { DueBefore = StudyDay.EndOfTodayMs(), Types = ItemTypes.Studyable.ToList(), OrderBy = "i.srs_due" });
        var missed = Host.Store.QueryItems(new ItemQuery { OnlyMissed = true, Types = ItemTypes.Studyable.ToList(), OrderBy = "i.last_seen_at DESC" });
        var flagged = Host.Store.QueryItems(new ItemQuery { OnlyFlagged = true, WrongInSource = null, IncludeSuspended = true, OrderBy = "i.updated_at DESC" });
        var wrong = Host.Store.QueryItems(new ItemQuery { WrongInSource = true, IncludeSuspended = true, OrderBy = "i.updated_at DESC" });

        body.Controls.Add(PageHeader(T("Review"), T("Items that need your attention, across all subjects.")));
        var tabs = new Segmented(tab,
            $"{T("Due today")} ({due.Count})", $"{T("Missed")} ({missed.Count})", $"{T("Flagged")} ({flagged.Count})", $"{T("Wrong in source")} ({wrong.Count})")
            .Align(HAlign.Left);
        tabs.SelectionChanged += (_, _) => Host.Main?.Replace(() => new ReviewPage(tabs.SelectedIndex));
        body.Controls.Add(tabs);

        List<Item> list;
        switch (tab)
        {
            case 1:
                list = missed;
                if (list.Count > 0)
                    body.Controls.Add(new SectionHeader(T("Recently missed"), T("You got these wrong or only partly right the last time."),
                        new ButtonEx(T("Get harder questions"), Icons.Chat, Kind.Ghost).OnClick(() => Go(() => new PromptsPage(PromptDefaults.Weak))),
                        new ButtonEx(T("Practise these"), Icons.Play, Kind.Primary).OnClick(() => StudyLauncher.ReviewItems(missed.Take(60).Select(i => i.Id), T("Missed items")))));
                break;
            case 2:
                list = flagged;
                if (list.Count > 0)
                    body.Controls.Add(new SectionHeader(T("Flagged"), T("Items you flagged to look at again."),
                        new ButtonEx(T("Remove all flags"), Icons.Flag, Kind.Ghost).OnClick(() =>
                        {
                            if (!Ask.Confirm(T("Remove all flags?"), F("{0} will no longer be flagged.", N(flagged.Count, "item", "items")), T("Remove flags"))) return;
                            foreach (var i in flagged) Host.Store.SetFlagged(i.Id, false);
                            Host.Main?.ReloadPage();
                        }),
                        new ButtonEx(T("Study these"), Icons.Play, Kind.Primary).OnClick(() => StudyLauncher.ReviewItems(flagged.Where(i => !i.WrongInSource).Select(i => i.Id), T("Flagged items")))));
                break;
            case 3:
                list = wrong;
                if (list.Count > 0)
                    body.Controls.Add(new Banner(Tone.Warning, T("Items marked as wrong in the source"),
                        T("The AI made a mistake in these. Click an item to correct it and switch off \"Wrong in source\" — or delete it. Until then it is left out of study sessions.")));
                break;
            default:
                list = due;
                if (list.Count > 0)
                    body.Controls.Add(new SectionHeader(T("Due today"), T("Spaced repetition scheduled these for today."),
                        new ButtonEx(F("Study all ({0})", due.Count), Icons.Play, Kind.Primary).OnClick(() => StudyLauncher.QuickStudy(new()))));
                break;
        }

        if (list.Count == 0)
        {
            var (glyph, title, text) = tab switch
            {
                1 => (Icons.CheckCircle, T("No recent mistakes"), T("Items you get wrong show up here so you can practise them again.")),
                2 => (Icons.Flag, T("Nothing flagged"), T("Press F while studying to flag an item you want to look at again.")),
                3 => (Icons.Bug, T("No items marked as wrong"), T("If the AI got something wrong, press W while studying (or use the editor) to mark it.")),
                _ => (Icons.CheckCircle, T("Nothing due today"), T("You are all caught up. New and practice sessions are available from each subject.")),
            };
            body.Controls.Add(new EmptyState(glyph, title, text));
            return;
        }

        var listCol = new Column { Gap = S(10) };
        foreach (var item in list.Take(300))
        {
            var row = new ItemRow(item, showContext: true);
            row.Deleted += (_, _) => Host.Main?.ReloadPage();
            row.Changed += (_, _) => { if (tab is 2 or 3) Host.Main?.ReloadPage(); };
            listCol.Controls.Add(row);
        }
        body.Controls.Add(listCol);
        if (list.Count > 300) body.Controls.Add(Ui.Faint(F("Showing the first 300 of {0}.", list.Count)));
    }
}

/// <summary>Global search across subjects, decks and every item.</summary>
public sealed class SearchPage : PageBase
{
    private readonly InputBox _box;
    private readonly Column _results = new() { Gap = S(10) };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 250 };

    public override string NavKey => "search";

    public SearchPage(string query = "")
    {
        var body = UseStandardLayout();
        body.Controls.Add(PageHeader(T("Search"), T("Find any flashcard, question or note in all your subjects.")));
        _box = new InputBox(T("Type to search…  (questions, answers, explanations, tags, pages)"), glyph: Icons.Search, fontPt: 12f) { Text = query };
        _box.Height = S(48);
        _box.Changed += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Run(); };
        body.Controls.Add(_box);
        body.Controls.Add(_results);
        Run();
    }

    public override void OnActivated() => FocusSearch();

    public override void OnDeactivated()
    {
        _debounce.Stop();
        _debounce.Dispose();
    }

    public void FocusSearch()
    {
        _box.Focus();
        _box.Box.SelectAll();
    }

    private void Run()
    {
        _results.SuspendLayout();
        foreach (Control c in _results.Controls.Cast<Control>().ToList()) { _results.Controls.Remove(c); c.Dispose(); }
        var q = _box.Text.Trim();
        if (q.Length < 2)
        {
            _results.Controls.Add(new EmptyState(Icons.Search, T("Search everything"), T("Type at least two letters. Tip: press Ctrl+K anywhere to come here.")));
        }
        else
        {
            var hits = Host.Store.Search(q);
            var subjects = hits.Where(h => h.Kind == TrashKind.Subject).ToList();
            var decks = hits.Where(h => h.Kind == TrashKind.Deck).ToList();
            var items = hits.Where(h => h.Kind == TrashKind.Item).ToList();
            if (hits.Count == 0)
                _results.Controls.Add(new EmptyState(Icons.Search, T("No results"), F("Nothing matches \"{0}\".", q)));
            else
                _results.Controls.Add(new TextLabel(F("{0} found", N(hits.Count, "result", "results")), Fonts.Ui(9.5f), Theme.P.Text2));

            if (subjects.Count + decks.Count > 0)
            {
                var card = new Card(16) { Gap = S(6) };
                foreach (var h in subjects.Concat(decks))
                {
                    var row = new Row { Gap = S(10) };
                    row.Controls.Add(new IconBadge(h.Kind == TrashKind.Subject ? Icons.Library : Icons.Layers, 34, Theme.P.Accent));
                    var col = new Column { Gap = S(2) }.Flex();
                    var link = new TextLabel(h.Title, Fonts.UiSemibold(10.5f), Theme.P.Text, wrap: false) { IsLink = true };
                    var hit = h;
                    link.Click += (_, _) =>
                    {
                        if (hit.Kind == TrashKind.Subject) Go(() => new SubjectPage(hit.Id));
                        else Go(() => new DeckPage(hit.Id));
                    };
                    col.Controls.Add(link);
                    if (h.Context.Length > 0) col.Controls.Add(new TextLabel(h.Context, Fonts.Ui(9f), Theme.P.Text2, wrap: false));
                    row.Controls.Add(col);
                    card.Controls.Add(row);
                }
                _results.Controls.Add(card);
            }
            foreach (var h in items.Take(200))
            {
                var row = new ItemRow(h.Item!, showContext: true);
                row.Deleted += (_, _) => Run();
                _results.Controls.Add(row);
            }
        }
        _results.ResumeLayout(true);
        LayoutProps.Relayout(_results);
    }
}

/// <summary>Deleted subjects, chapters, decks and items — restore or delete for good.</summary>
public sealed class TrashPage : PageBase
{
    public override string NavKey => "trash";

    public TrashPage()
    {
        var body = UseStandardLayout();
        var entries = Host.Store.GetTrash();
        var empty = new ButtonEx(T("Empty recycle bin"), Icons.Delete, Kind.Danger) { Enabled = entries.Count > 0 };
        empty.Click += (_, _) =>
        {
            if (!Ask.Confirm(T("Empty the recycle bin?"), T("Everything in the recycle bin will be deleted permanently. This cannot be undone."), T("Delete permanently"), danger: true)) return;
            Host.Store.EmptyTrash();
            Host.Main?.ReloadPage();
        };
        body.Controls.Add(PageHeader(T("Recycle bin"), F("Deleted things are kept for {0}, then removed automatically.", N(Host.Settings.RecycleBinDays, "day", "days")), false, empty));

        if (entries.Count == 0)
        {
            body.Controls.Add(new EmptyState(Icons.Delete, T("The recycle bin is empty"), T("When you delete a subject, chapter, deck or item it waits here, so you can restore it.")));
            return;
        }

        var card = new Card(16) { Gap = S(4) };
        bool first = true;
        foreach (var e in entries)
        {
            if (!first) card.Controls.Add(new Divider());
            first = false;
            var row = new Row { Gap = S(12) };
            var glyph = e.Kind switch
            {
                TrashKind.Subject => Icons.Library,
                TrashKind.Chapter => Icons.Book,
                TrashKind.Deck => Icons.Layers,
                _ => Icons.Document,
            };
            row.Controls.Add(new IconBadge(glyph, 36, Theme.P.Text2));
            var col = new Column { Gap = S(2) }.Flex();
            col.Controls.Add(new TextLabel(e.Title, Fonts.UiSemibold(10f), Theme.P.Text) { MaxLines = 2 });
            var kind = e.Kind switch
            {
                TrashKind.Subject => T("Subject"),
                TrashKind.Chapter => T("Chapter"),
                TrashKind.Deck => T("Deck"),
                _ => T("Item"),
            };
            var meta = kind + (e.Context.Length > 0 ? "  ·  " + e.Context : "") + (e.Children > 0 ? "  ·  " + N(e.Children, "item", "items") : "") + "  ·  " + F("deleted {0}", L.Ago(Time.LocalFromMs(e.DeletedAt)));
            col.Controls.Add(new TextLabel(meta, Fonts.Ui(9f), Theme.P.Text2, wrap: false));
            row.Controls.Add(col);
            var entry = e;
            row.Controls.Add(new ButtonEx(T("Restore"), Icons.Undo, Kind.Soft, 34).OnClick(() =>
            {
                Host.Store.Restore(entry.Kind, entry.Id);
                Toast.Show(F("\"{0}\" restored.", TextUtil.Truncate(entry.Title, 60)));
                Host.Main?.ReloadPage();
            }));
            row.Controls.Add(ButtonEx.Icon(Icons.Delete, T("Delete permanently")).Also(b => b.Click += (_, _) =>
            {
                if (!Ask.Confirm(T("Delete permanently?"), F("\"{0}\" will be gone for good.", TextUtil.Truncate(entry.Title, 80)), T("Delete"), danger: true)) return;
                Host.Store.DeleteForever(entry.Kind, entry.Id);
                Host.Main?.ReloadPage();
            }));
            card.Controls.Add(row);
        }
        body.Controls.Add(card);
    }
}
