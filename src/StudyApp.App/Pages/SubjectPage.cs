using StudyApp.App.Dialogs;
using StudyApp.App.Shell;
using StudyApp.Domain.Prompts;

namespace StudyApp.App.Pages;

/// <summary>One subject: chapters with their decks, plus a statistics tab.</summary>
public sealed class SubjectPage : PageBase
{
    private readonly long _id;
    private readonly Subject? _subject;

    public override string NavKey => "subjects";

    public SubjectPage(long subjectId, int tab = 0)
    {
        _id = subjectId;
        _subject = Host.Store.GetSubject(subjectId);
        var body = UseStandardLayout();
        if (_subject == null)
        {
            body.Controls.Add(new EmptyState(Icons.Warning, T("Subject not found"), T("It may have been deleted."),
                new ButtonEx(T("All subjects"), Icons.Library, Kind.Primary).OnClick(() => Go(() => new SubjectsPage()))));
            return;
        }
        Host.Store.TouchSubject(subjectId);
        if (Host.Settings.LastSubjectId != subjectId)
        {
            Host.Settings.LastSubjectId = subjectId;
            Host.SaveSettings();
        }

        var s = _subject;
        var color = Gfx.FromHex(s.Color, Theme.P.Accent);
        int due = Host.Store.CountDueToday(s.Id);

        var header = new Row { Gap = S(14) };
        if (Host.Main?.CanGoBack == true)
            header.Controls.Add(ButtonEx.Icon(Icons.Back, T("Back"), "Alt+←", Kind.Ghost, 38).Also(b => b.Click += (_, _) => Host.Main?.GoBack()));
        header.Controls.Add(new IconBadge(Icons.ForSubject(s.Icon), 52, color));
        var titleCol = new Column { Gap = S(3) }.Flex();
        titleCol.Controls.Add(new TextLabel(s.Name, Fonts.Heading(20f), Theme.P.Text) { MaxLines = 2 });
        var sub = s.Source.Length > 0 ? s.Source : s.Description;
        if (sub.Length > 0) titleCol.Controls.Add(new TextLabel(sub, Fonts.Ui(10f), Theme.P.Text2) { MaxLines = 2 });
        header.Controls.Add(titleCol);
        var study = new ButtonEx(due > 0 ? F("Study ({0} due)", due) : T("Study"), Icons.Play, Kind.Primary)
            .WithTip(T("Smart review of this subject"));
        study.Click += (_, _) => StudyLauncher.QuickStudy(StudyLauncher.Scope(s.Id));
        var test = new ButtonEx(T("Test"), Icons.CheckCircle, Kind.Secondary).WithTip(T("Take a test on this subject"));
        test.Click += (_, _) => StudyLauncher.Test(StudyLauncher.Scope(s.Id));
        var more = ButtonEx.Icon(Icons.More, T("More actions"), null, Kind.Secondary, 36);
        more.Click += (_, _) => Menus.Show(more,
            Menus.Item(T("Study with options…"), Icons.Sliders, () => StudyLauncher.StudyWithOptions(StudyLauncher.Scope(s.Id))),
            Menus.Separator(),
            Menus.Item(T("Add chapter"), Icons.Add, AddChapter),
            Menus.Item(T("Add deck"), Icons.Layers, () => AddDeck(null)),
            Menus.Item(T("Import into this subject"), Icons.Import, () => Go(() => new ImportPage(subjectId: s.Id))),
            Menus.Item(T("Get an AI prompt"), Icons.Chat, () => Go(() => new PromptsPage(null, PresetFor(s, null)))),
            Menus.Separator(),
            Menus.Item(T("Edit subject"), Icons.Edit, () => SubjectsPage.EditSubject(s)),
            Menus.Item(T("Export to JSON…"), Icons.Export, () => SubjectsPage.ExportSubject(s)),
            Menus.Item(s.Archived ? T("Unarchive") : T("Archive"), Icons.Archive, () => { Host.Store.SetSubjectArchived(s.Id, !s.Archived); Host.Main?.ReloadPage(); }),
            Menus.Item(T("Reset progress…"), Icons.Refresh, ResetProgress),
            Menus.Separator(),
            Menus.Item(T("Delete subject"), Icons.Delete, () => SubjectsPage.DeleteSubject(s, goToList: true), danger: true));
        header.Controls.Add(study);
        header.Controls.Add(test);
        header.Controls.Add(more);
        body.Controls.Add(header);

        var tabs = new Segmented(tab, T("Content"), T("Statistics")).Align(HAlign.Left);
        tabs.SelectionChanged += (_, _) => Host.Main?.Replace(() => new SubjectPage(_id, tabs.SelectedIndex));
        body.Controls.Add(tabs);

        if (tab == 1) BuildStats(body, s);
        else BuildContent(body, s);
    }

    public static PromptOptions PresetFor(Subject s, Chapter? c)
    {
        var o = Host.Settings.PromptOptions.Clone();
        o.Subject = s.Name;
        o.Source = s.Source;
        o.ChapterNumber = c?.Number ?? "";
        o.ChapterTitle = c?.Title ?? "";
        o.Pages = c?.Pages ?? "";
        return o;
    }

    // ───────────────────────────────────────────────────────────── content tab

    private void BuildContent(Column body, Subject s)
    {
        var chapters = Host.Store.GetChapters(s.Id);
        var decks = Host.Store.GetDecks(s.Id);
        var summaries = Host.Store.DeckSummaries(s.Id);

        if (chapters.Count == 0 && decks.Count == 0)
        {
            body.Controls.Add(new EmptyState(Icons.Layers, T("This subject is empty"),
                T("Ask your AI chat for flashcards or a test (AI prompts), then import the reply. You can also add chapters first to organise everything."),
                new ButtonEx(T("Get an AI prompt"), Icons.Chat, Kind.Primary, 40).OnClick(() => Go(() => new PromptsPage(null, PresetFor(s, null)))),
                new ButtonEx(T("Import JSON"), Icons.Import, Kind.Secondary, 40).OnClick(() => Go(() => new ImportPage(subjectId: s.Id))),
                new ButtonEx(T("Add chapter"), Icons.Add, Kind.Secondary, 40).OnClick(AddChapter)));
            return;
        }

        // Summary strip
        var sum = Host.Store.SubjectSummaries().GetValueOrDefault(s.Id) ?? new SubjectSummary();
        var strip = new CardGrid { MinCellWidth = S(150), Gap = S(12), MaxColumns = sum.Flagged > 0 ? 5 : 4 };
        strip.Controls.Add(new MiniStat(T("Items"), sum.Items.ToString("N0", L.Culture), Theme.P.Text));
        strip.Controls.Add(new MiniStat(T("Due today"), sum.DueToday.ToString("N0", L.Culture), Theme.P.Accent));
        strip.Controls.Add(new MiniStat(T("New"), sum.New.ToString("N0", L.Culture), Theme.P.Info));
        strip.Controls.Add(new MiniStat(T("Mastered"), $"{sum.MasteredShare * 100:0}%", Theme.P.Success));
        if (sum.Flagged > 0) strip.Controls.Add(new MiniStat(T("Flagged"), sum.Flagged.ToString(), Theme.P.Warning));
        body.Controls.Add(strip);

        foreach (var chapter in chapters)
            body.Controls.Add(ChapterCard(s, chapter, decks.Where(d => d.ChapterId == chapter.Id).ToList(), summaries, chapters));

        var loose = decks.Where(d => d.ChapterId == null || chapters.All(c => c.Id != d.ChapterId)).ToList();
        if (loose.Count > 0) body.Controls.Add(ChapterCard(s, null, loose, summaries, chapters));

        var bottom = new Row { Gap = S(10), Wrap = true };
        bottom.Controls.Add(new ButtonEx(T("Add chapter"), Icons.Add, Kind.Secondary).OnClick(AddChapter));
        bottom.Controls.Add(new ButtonEx(T("Add deck"), Icons.Layers, Kind.Ghost).OnClick(() => AddDeck(null)));
        bottom.Controls.Add(new ButtonEx(T("Import into this subject"), Icons.Import, Kind.Ghost).OnClick(() => Go(() => new ImportPage(subjectId: s.Id))));
        body.Controls.Add(bottom);
    }

    private Control ChapterCard(Subject s, Chapter? chapter, List<Deck> decks, Dictionary<long, DeckSummary> summaries, List<Chapter> allChapters)
    {
        var card = new Card(20) { Gap = S(12) };
        var head = new Row { Gap = S(10) };
        var titleCol = new Column { Gap = S(4) }.Flex();
        titleCol.Controls.Add(new TextLabel(chapter?.DisplayName ?? T("Other decks"), Fonts.Heading(13f), Theme.P.Text) { MaxLines = 2 });
        int items = decks.Sum(d => summaries.GetValueOrDefault(d.Id)?.Studyable ?? 0);
        int due = decks.Sum(d => summaries.GetValueOrDefault(d.Id)?.DueToday ?? 0);
        var meta = new List<string>();
        if (chapter != null && chapter.Pages.Length > 0) meta.Add(L.Page(chapter.Pages));
        meta.Add(N(decks.Count, "deck", "decks"));
        if (items > 0) meta.Add(N(items, "item", "items"));
        if (due > 0) meta.Add(F("{0} due", due));
        titleCol.Controls.Add(new TextLabel(string.Join("  ·  ", meta), Fonts.Ui(9f), Theme.P.Text2, wrap: false));
        head.Controls.Add(titleCol);

        if (chapter != null)
        {
            if (items > 0)
            {
                head.Controls.Add(new ButtonEx(T("Study"), Icons.Play, Kind.Soft, 34).WithTip(T("Smart review of this chapter"))
                    .OnClick(() => StudyLauncher.QuickStudy(StudyLauncher.Scope(s.Id, chapter.Id))));
                head.Controls.Add(new ButtonEx(T("Test"), Icons.CheckCircle, Kind.Ghost, 34).WithTip(T("Take a test on this chapter"))
                    .OnClick(() => StudyLauncher.Test(StudyLauncher.Scope(s.Id, chapter.Id))));
            }
            var more = ButtonEx.Icon(Icons.More, T("Chapter actions"));
            int index = allChapters.FindIndex(c => c.Id == chapter.Id);
            more.Click += (_, _) => Menus.Show(more,
                Menus.Item(T("Get an AI prompt for this chapter"), Icons.Chat, () => Go(() => new PromptsPage(null, PresetFor(s, chapter)))),
                Menus.Item(T("Import into this chapter"), Icons.Import, () => Go(() => new ImportPage(subjectId: s.Id, chapterId: chapter.Id))),
                Menus.Item(T("Add deck"), Icons.Layers, () => AddDeck(chapter.Id)),
                Menus.Item(T("Study with options…"), Icons.Sliders, () => StudyLauncher.StudyWithOptions(StudyLauncher.Scope(s.Id, chapter.Id))),
                Menus.Separator(),
                Menus.Item(T("Edit chapter"), Icons.Edit, () =>
                {
                    if (ChapterDialog.Show(chapter, false) != null) { Host.Store.UpdateChapter(chapter); Host.Main?.ReloadPage(); }
                }),
                Menus.Item(T("Move up"), Icons.ChevronUp, () => { Host.Store.MoveChapter(chapter.Id, -1); Host.Main?.ReloadPage(); }),
                Menus.Item(T("Move down"), Icons.ChevronDown, () => { Host.Store.MoveChapter(chapter.Id, 1); Host.Main?.ReloadPage(); }),
                Menus.Item(T("Export to JSON…"), Icons.Export, () => SubjectsPage.ExportSubject(s, onlyChapterId: chapter.Id)),
                Menus.Separator(),
                Menus.Item(T("Delete chapter"), Icons.Delete, () => DeleteChapter(chapter), danger: true));
            head.Controls.Add(more);
            _ = index;
        }
        card.Controls.Add(head);

        if (decks.Count == 0)
        {
            var hint = new Row { Gap = S(8), Wrap = true };
            hint.Controls.Add(new TextLabel(T("No decks yet."), Fonts.Ui(9.5f), Theme.P.Text3, wrap: false));
            if (chapter != null)
            {
                hint.Controls.Add(new ButtonEx(T("Get an AI prompt"), Icons.Chat, Kind.Link, 26).OnClick(() => Go(() => new PromptsPage(null, PresetFor(s, chapter)))));
                hint.Controls.Add(new ButtonEx(T("Import JSON"), Icons.Import, Kind.Link, 26).OnClick(() => Go(() => new ImportPage(subjectId: s.Id, chapterId: chapter.Id))));
            }
            card.Controls.Add(hint);
            return card;
        }

        bool first = true;
        foreach (var d in decks)
        {
            if (!first) card.Controls.Add(new Divider());
            first = false;
            card.Controls.Add(new DeckRow(s, d, summaries.GetValueOrDefault(d.Id) ?? new DeckSummary { DeckId = d.Id }));
        }
        return card;
    }

    private void AddChapter()
    {
        var chapter = ChapterDialog.Show(new Chapter { SubjectId = _id, Number = (Host.Store.GetChapters(_id).Count + 1).ToString() }, true);
        if (chapter == null) return;
        Host.Store.CreateChapter(chapter);
        Host.Main?.ReloadPage();
    }

    private void AddDeck(long? chapterId)
    {
        var deck = DeckDialog.Show(new Deck { SubjectId = _id, ChapterId = chapterId, Source = _subject?.Source ?? "" }, true);
        if (deck == null) return;
        Host.Store.CreateDeck(deck);
        Go(() => new DeckPage(deck.Id));
    }

    private static void DeleteChapter(Chapter chapter)
    {
        if (!Ask.Confirm(T("Delete chapter?"), F("\"{0}\" and its decks will be moved to the recycle bin.", chapter.DisplayName), T("Delete"), danger: true)) return;
        Host.Store.DeleteChapter(chapter.Id);
        Toast.Show(T("Chapter moved to the recycle bin."), Tone.Info, T("Undo"), () => { Host.Store.RestoreChapter(chapter.Id); Host.Main?.ReloadPage(); });
        Host.Main?.ReloadPage();
    }

    private void ResetProgress()
    {
        if (!Ask.Confirm(T("Reset progress?"), T("All answers, statistics and the review schedule of this subject will be cleared. Your flashcards and questions stay."), T("Reset"), danger: true))
            return;
        Host.Store.ResetProgress(_id);
        Toast.Show(T("Progress reset."), Tone.Info);
        Host.Main?.ReloadPage();
    }

    // ───────────────────────────────────────────────────────────── statistics tab

    private void BuildStats(Column body, Subject s)
    {
        var sum = Host.Store.SubjectSummaries().GetValueOrDefault(s.Id) ?? new SubjectSummary();
        var tiles = new CardGrid { MinCellWidth = S(210), Gap = S(16), MaxColumns = 4 };
        tiles.Controls.Add(new StatTile(Icons.Chart, sum.Answers == 0 ? "–" : $"{sum.Accuracy * 100:0}%", T("Accuracy"), N(sum.Answers, "answer", "answers"), Theme.P.Info));
        tiles.Controls.Add(new StatTile(Icons.CheckCircle, sum.Mastered.ToString("N0", L.Culture), T("Mastered"), T("Interval of 3 weeks or more"), Theme.P.Success));
        tiles.Controls.Add(new StatTile(Icons.Refresh, sum.Learning.ToString("N0", L.Culture), T("Learning"), T("Seen, not yet mastered"), Theme.P.Warning));
        tiles.Controls.Add(new StatTile(Icons.Add, sum.New.ToString("N0", L.Culture), T("New"), T("Never studied"), Theme.P.Accent));
        body.Controls.Add(tiles);

        var progress = new Card(20) { Gap = S(10) };
        progress.Controls.Add(new SectionHeader(T("Progress"), T("How much of this subject you know well")));
        if (sum.Items > 0)
        {
            progress.Controls.Add(new ProgressBarEx(0, 14)
            {
                Segments = new()
                {
                    ((float)sum.Mastered / sum.Items, Theme.P.Success),
                    ((float)sum.Learning / sum.Items, Theme.P.Warning),
                    ((float)sum.New / sum.Items, Gfx.Blend(Theme.P.Surface3, Theme.P.Accent, 0.25f)),
                },
            });
            var legend = new Row { Gap = S(16), Wrap = true };
            legend.Controls.Add(Chip.Status(F("{0} mastered", sum.Mastered), Theme.P.Success));
            legend.Controls.Add(Chip.Status(F("{0} learning", sum.Learning), Theme.P.Warning));
            legend.Controls.Add(Chip.Status(F("{0} new", sum.New), Theme.P.Accent));
            progress.Controls.Add(legend);
        }
        else progress.Controls.Add(Ui.Muted(T("No items yet.")));

        var chart = new Card(20) { Gap = S(10) };
        chart.Controls.Add(new SectionHeader(T("Accuracy over time"), T("Share of correct answers per week")));
        chart.Controls.Add(new LineChart(Host.Store.AccuracyByWeek(s.Id, 12)
            .Select(w => (w.WeekStart.ToDateTime(TimeOnly.MinValue).ToString("d MMM", L.Culture), w.Accuracy, w.Answers))));
        body.Controls.Add(new TwoColumns(chart, progress, 0.6f));

        var chapters = new Card(20) { Gap = S(10) };
        chapters.Controls.Add(new SectionHeader(T("Weakest chapters")));
        chapters.Controls.Add(new BarList(Host.Store.WeakestChapters(s.Id).Take(6).Select(w => (w.Label, w.Accuracy, F("{0:0}% · {1}", w.Accuracy * 100, N(w.Answers, "answer", "answers"))))));
        var tags = new Card(20) { Gap = S(10) };
        tags.Controls.Add(new SectionHeader(T("Weakest topics")));
        tags.Controls.Add(new BarList(Host.Store.WeakestTags(s.Id).Take(6).Select(w => ("#" + w.Label, w.Accuracy, F("{0:0}% · {1}", w.Accuracy * 100, N(w.Answers, "answer", "answers"))))));
        body.Controls.Add(new TwoColumns(chapters, tags, 0.5f));

        var weak = Host.Store.WeakItems(s.Id, 8);
        if (weak.Count > 0)
        {
            var card = new Card(20) { Gap = S(10) };
            var practise = new ButtonEx(T("Practise these"), Icons.Play, Kind.Soft, 34).OnClick(() => StudyLauncher.ReviewItems(Host.Store.WeakItems(s.Id, 30).Select(i => i.Id), T("Weak spots")));
            var prompt = new ButtonEx(T("Get harder questions"), Icons.Chat, Kind.Ghost, 34).OnClick(() => Go(() => new PromptsPage(PromptDefaults.Weak, PresetFor(s, null))));
            card.Controls.Add(new SectionHeader(T("Most missed items"), null, prompt, practise));
            foreach (var item in weak) card.Controls.Add(new ItemRow(item));
            body.Controls.Add(card);
        }

        var sessions = Host.Store.RecentSessions(s.Id, 8);
        var history = new Card(20) { Gap = S(8) };
        history.Controls.Add(new SectionHeader(T("Recent sessions")));
        if (sessions.Count == 0) history.Controls.Add(Ui.Muted(T("No sessions yet.")));
        foreach (var r in sessions)
        {
            var row = new Row { Gap = S(12) };
            row.Controls.Add(Chip.Neutral(r.Mode == SessionMode.Test ? T("Test") : T("Flashcards"), r.Mode == SessionMode.Test ? Icons.CheckCircle : Icons.Layers));
            row.Controls.Add(new TextLabel(r.Title.Length > 0 ? r.Title : s.Name, Fonts.Ui(9.5f), Theme.P.Text, wrap: false).Flex());
            row.Controls.Add(new TextLabel(L.DateTimeShort(Time.LocalFromMs(r.StartedAt)), Fonts.Ui(9f), Theme.P.Text3, wrap: false));
            var c = r.Score >= 0.8 ? Theme.P.Success : r.Score >= 0.6 ? Theme.P.Warning : Theme.P.Danger;
            row.Controls.Add(Chip.Status($"{r.Score * 100:0}% · {r.ItemCount}", c));
            history.Controls.Add(row);
        }
        body.Controls.Add(history);
    }

    /// <summary>Small number + label tile.</summary>
    private sealed class MiniStat : UiControl, IHeightForWidth
    {
        private readonly string _label;
        private readonly string _value;
        private readonly Color _color;

        public MiniStat(string label, string value, Color color)
        {
            _label = label;
            _value = value;
            _color = color;
        }

        public int HeightFor(int width) => S(72);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.P.Bg);
            Gfx.Smooth(g);
            var r = new RectangleF(0, 0, Width, Height);
            Gfx.FillRound(g, Theme.P.Surface, r, S(12));
            Gfx.DrawRound(g, Theme.P.Border, r, S(12));
            Gfx.Text(g, _value, Fonts.Heading(17f), _color, new Rectangle(S(16), S(10), Width - S(32), S(32)), Gfx.Left);
            Gfx.Text(g, _label, Fonts.Ui(9f), Theme.P.Text2, new Rectangle(S(16), S(42), Width - S(32), S(20)), Gfx.Left);
        }
    }
}

/// <summary>A deck inside a chapter card: icon, title, progress and the main actions.</summary>
public sealed class DeckRow : Row
{
    public DeckRow(Subject subject, Deck deck, DeckSummary sum)
    {
        Gap = S(12);
        var (glyph, tint) = deck.Kind switch
        {
            DeckKind.Test => (Icons.CheckCircle, Theme.P.Info),
            DeckKind.Notes => (Icons.Reading, Theme.P.Success),
            _ => (Icons.Layers, Theme.P.Accent),
        };
        Controls.Add(new IconBadge(glyph, 40, tint));

        var col = new Column { Gap = S(5) }.Flex();
        var title = new TextLabel(deck.Title, Fonts.UiSemibold(10.5f), Theme.P.Text, wrap: false) { IsLink = true };
        title.Click += (_, _) =>
        {
            if (deck.Kind == DeckKind.Notes) StudyLauncher.Read(deck.Id);
            else Host.Main?.Navigate(() => new DeckPage(deck.Id));
        };
        Tips.Set(title, deck.Kind == DeckKind.Notes ? T("Open the notes") : T("Browse and edit the items"));
        col.Controls.Add(title);

        var meta = new List<string>();
        if (sum.Cards > 0) meta.Add(N(sum.Cards, "card", "cards"));
        if (sum.Questions > 0) meta.Add(N(sum.Questions, "question", "questions"));
        if (sum.Notes > 0) meta.Add(N(sum.Notes, "section", "sections"));
        if (sum.Items == 0) meta.Add(T("empty"));
        if (sum.DueToday > 0) meta.Add(F("{0} due", sum.DueToday));
        if (sum.Flagged > 0) meta.Add(F("{0} flagged", sum.Flagged));
        if (deck.Difficulty > 0) meta.Add(Difficulty.Label(deck.Difficulty));
        col.Controls.Add(new TextLabel(string.Join("  ·  ", meta), Fonts.Ui(9f), Theme.P.Text2, wrap: false));

        if (deck.Kind == DeckKind.Notes && sum.Notes > 0)
            col.Controls.Add(new ProgressBarEx((float)sum.NotesRead / sum.Notes, 5) { Bar = Theme.P.Success });
        else if (sum.Studyable > 0)
            col.Controls.Add(new ProgressBarEx(0, 5)
            {
                Segments = new() { ((float)sum.Mastered / sum.Studyable, Theme.P.Success), ((float)(sum.Studyable - sum.New - sum.Mastered) / sum.Studyable, Gfx.Blend(Theme.P.Surface, tint, 0.55f)) },
            });
        Controls.Add(col);

        if (deck.Kind == DeckKind.Notes)
        {
            Controls.Add(new ButtonEx(T("Read"), Icons.Reading, Kind.Soft, 34).OnClick(() => StudyLauncher.Read(deck.Id)));
        }
        else
        {
            if (sum.Studyable > 0)
                Controls.Add(new ButtonEx(T("Study"), Icons.Play, Kind.Soft, 34).WithTip(T("Smart review of this deck"))
                    .OnClick(() => StudyLauncher.QuickStudy(StudyLauncher.Scope(subject.Id, null, deck.Id))));
            if (sum.Questions > 0 || sum.Cards > 0)
                Controls.Add(new ButtonEx(T("Test"), Icons.CheckCircle, Kind.Ghost, 34).WithTip(T("Take a test on this deck"))
                    .OnClick(() => StudyLauncher.Test(StudyLauncher.Scope(subject.Id, null, deck.Id))));
        }

        var more = ButtonEx.Icon(Icons.More, T("Deck actions"));
        more.Click += (_, _) => Menus.Show(more,
            Menus.Item(T("Browse and edit items"), Icons.List, () => Host.Main?.Navigate(() => new DeckPage(deck.Id))),
            Menus.Item(T("Study with options…"), Icons.Sliders, () => StudyLauncher.StudyWithOptions(StudyLauncher.Scope(subject.Id, null, deck.Id))),
            Menus.Item(T("Add item"), Icons.Add, () => { if (ItemEditorDialog.Create(deck) != null) Host.Main?.ReloadPage(); }),
            Menus.Separator(),
            Menus.Item(T("Edit deck"), Icons.Edit, () => { if (DeckDialog.Show(deck, false) != null) { Host.Store.UpdateDeck(deck); Host.Main?.ReloadPage(); } }),
            Menus.Item(T("Move up"), Icons.ChevronUp, () => { Host.Store.MoveDeck(deck.Id, -1); Host.Main?.ReloadPage(); }),
            Menus.Item(T("Move down"), Icons.ChevronDown, () => { Host.Store.MoveDeck(deck.Id, 1); Host.Main?.ReloadPage(); }),
            Menus.Item(T("Export to JSON…"), Icons.Export, () => SubjectsPage.ExportSubject(subject, onlyDeckId: deck.Id)),
            Menus.Separator(),
            Menus.Item(T("Delete deck"), Icons.Delete, () => DeckPage.DeleteDeck(deck, reloadOnly: true), danger: true));
        Controls.Add(more);
    }
}
