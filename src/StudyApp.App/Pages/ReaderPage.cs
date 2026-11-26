using StudyApp.App.Shell;
using StudyApp.Domain.Settings;

namespace StudyApp.App.Pages;

/// <summary>Comfortable reader for study notes: contents list, read tracking and quick reading settings.</summary>
public sealed class ReaderPage : PageBase
{
    private readonly Deck? _deck;
    private readonly List<Item> _sections = new();
    private readonly List<Control> _sectionViews = new();
    private readonly ScrollHost? _scroll;
    private readonly TocList? _toc;
    private readonly ProgressBarEx _progress = new(0, 6);
    private readonly TextLabel _progressText = new("", Fonts.Ui(9f), Theme.P.Text2, wrap: false);
    private readonly int _startSection;

    public override string NavKey => "subjects";
    public override bool FocusMode => true;

    public ReaderPage(long deckId, int startSection = 0)
    {
        _deck = Host.Store.GetDeck(deckId);
        _startSection = startSection;
        if (_deck == null)
        {
            var body = UseStandardLayout();
            body.Controls.Add(new EmptyState(Icons.Warning, T("Notes not found"), T("They may have been deleted.")));
            return;
        }
        _sections = Host.Store.GetItems(deckId).Where(i => i.Type == ItemTypes.Note || !i.IsStudyable || _deck.Kind == DeckKind.Notes).ToList();

        // Top bar
        var top = new SessionBar(true, 72) { MaxContentWidth = S(1400) };
        var row = new Row { Gap = S(10) };
        var back = ButtonEx.Icon(Icons.Back, T("Back"), "Esc", Kind.Ghost, 38);
        back.Click += (_, _) => Host.Main?.GoBack();
        row.Controls.Add(back);
        var titleCol = new Column { Gap = S(2) };
        titleCol.Controls.Add(new TextLabel(_deck.Title, Fonts.UiSemibold(10.5f), Theme.P.Text, wrap: false));
        titleCol.Controls.Add(_progressText);
        titleCol.FixedWidth(S(320));
        row.Controls.Add(titleCol);
        row.Controls.Add(_progress.Flex());
        row.Controls.Add(ButtonEx.Icon(Icons.ZoomOut, T("Smaller text"), "Ctrl −").Also(b => b.Click += (_, _) => Zoom(-1)));
        row.Controls.Add(ButtonEx.Icon(Icons.ZoomIn, T("Bigger text"), "Ctrl +").Also(b => b.Click += (_, _) => Zoom(1)));
        var settings = ButtonEx.Icon(Icons.Font, T("Reading settings"));
        settings.Click += (_, _) => ShowReadingMenu(settings);
        row.Controls.Add(settings);
        row.Controls.Add(new ButtonEx(T("Mark all as read"), Icons.CheckCircle, Kind.Soft, 36).OnClick(() =>
        {
            foreach (var s in _sections) { Host.Store.MarkNotesRead(s.Id, true); s.LastResult = Result.GotIt; }
            UpdateProgress();
            foreach (var v in _sectionViews) v.Invalidate(true);
            _toc?.Invalidate();
        }));
        top.SetContent(row);

        // Content
        var content = new Column { Gap = S(10), Padding = Pad(40, 32, 40, 80), BackColor = Theme.P.Bg };
        if (_sections.Count == 0)
        {
            content.Controls.Add(new EmptyState(Icons.Reading, T("No notes yet"), T("Use the \"Study notes\" prompt to have the AI write a summary, then import it.")));
        }
        for (int i = 0; i < _sections.Count; i++)
        {
            var view = new SectionView(_sections[i], i, () => { UpdateProgress(); _toc?.Invalidate(); });
            _sectionViews.Add(view);
            content.Controls.Add(view);
        }
        _scroll = new ScrollHost(content) { Dock = DockStyle.Fill, MaxContentWidth = S(Math.Clamp(Host.Settings.ContentWidth, 520, 1400)) + S(80), BackColor = Theme.P.Bg };

        if (_sections.Count > 1)
        {
            _toc = new TocList(_sections, i => _scroll.ScrollTo(_sectionViews[i].Top - S(16)));
            var tocCol = new Column { Gap = S(8), Padding = Pad(18, 24, 12, 24), BackColor = Theme.P.Bg };
            tocCol.Controls.Add(new TextLabel(T("Contents"), Fonts.UiSemibold(10f), Theme.P.Text2));
            tocCol.Controls.Add(_toc);
            var tocScroll = new ScrollHost(tocCol) { Dock = DockStyle.Left, Width = S(280), BackColor = Theme.P.Bg };
            Controls.Add(_scroll);
            Controls.Add(tocScroll);
            _scroll.Scrolled += (_, _) => TrackPosition();
        }
        else Controls.Add(_scroll);
        Controls.Add(top);
        UpdateProgress();
    }

    public override void OnActivated()
    {
        if (_startSection > 0 && _startSection < _sectionViews.Count)
            BeginInvoke(() => _scroll?.ScrollTo(_sectionViews[_startSection].Top - S(16), animate: false));
    }

    private void TrackPosition()
    {
        if (_scroll == null || _toc == null) return;
        int current = 0;
        for (int i = 0; i < _sectionViews.Count; i++)
            if (_sectionViews[i].Top - S(80) <= _scroll.Offset) current = i;
        _toc.Current = current;
    }

    private int CurrentSection()
    {
        if (_scroll == null) return 0;
        int current = 0;
        for (int i = 0; i < _sectionViews.Count; i++)
            if (_sectionViews[i].Top - S(80) <= _scroll.Offset) current = i;
        return current;
    }

    private void UpdateProgress()
    {
        int read = _sections.Count(s => s.LastResult == Result.GotIt);
        _progress.Value = _sections.Count == 0 ? 0 : (float)read / _sections.Count;
        _progressText.Text = F("{0} of {1} read", read, N(_sections.Count, "section", "sections"));
        _progressText.Invalidate();
    }

    private void Zoom(int delta)
    {
        Host.Settings.NotesFontSize = Math.Clamp(Host.Settings.NotesFontSize + delta, 10, 30);
        Host.SaveSettings();
        Reopen();
    }

    private void Reopen()
    {
        if (_deck == null) return;
        int section = CurrentSection();
        long id = _deck.Id;
        Host.Main?.Replace(() => new ReaderPage(id, section));
    }

    private void ShowReadingMenu(Control anchor)
    {
        var s = Host.Settings;
        ToolStripMenuItem Check(string text, bool on, Action a)
        {
            var item = Menus.Item(text, null, a);
            item.Checked = on;
            return item;
        }
        void SetTheme(ThemeMode mode)
        {
            s.Theme = mode;
            Host.SaveSettings();
            Theme.Apply(s);
            Host.Main?.Rebuild();
        }
        Menus.Show(anchor,
            Check(T("Light"), Theme.Mode == ThemeMode.Light, () => SetTheme(ThemeMode.Light)),
            Check(T("Sepia"), Theme.Mode == ThemeMode.Sepia, () => SetTheme(ThemeMode.Sepia)),
            Check(T("Dark"), Theme.Mode == ThemeMode.Dark, () => SetTheme(ThemeMode.Dark)),
            Menus.Separator(),
            Check(T("Line spacing: compact"), s.LineHeight < 1.4f, () => { s.LineHeight = 1.3f; Host.SaveSettings(); Reopen(); }),
            Check(T("Line spacing: normal"), s.LineHeight is >= 1.4f and < 1.7f, () => { s.LineHeight = 1.5f; Host.SaveSettings(); Reopen(); }),
            Check(T("Line spacing: relaxed"), s.LineHeight >= 1.7f, () => { s.LineHeight = 1.85f; Host.SaveSettings(); Reopen(); }),
            Menus.Separator(),
            Check(T("Width: narrow"), s.ContentWidth < 700, () => { s.ContentWidth = 640; Host.SaveSettings(); Reopen(); }),
            Check(T("Width: normal"), s.ContentWidth is >= 700 and < 950, () => { s.ContentWidth = 820; Host.SaveSettings(); Reopen(); }),
            Check(T("Width: wide"), s.ContentWidth >= 950, () => { s.ContentWidth = 1080; Host.SaveSettings(); Reopen(); }),
            Menus.Separator(),
            Menus.Item(T("More reading settings…"), Icons.Settings, () => Go(() => new SettingsPage(1))));
    }

    public override bool HandleKey(Keys keyData)
    {
        if (_scroll == null) return false;
        switch (keyData)
        {
            case Keys.Escape: Host.Main?.GoBack(); return true;
            case Keys.Down: _scroll.ScrollBy(S(60)); return true;
            case Keys.Up: _scroll.ScrollBy(-S(60)); return true;
            case Keys.PageDown:
            case Keys.Space: _scroll.ScrollBy(_scroll.Height - S(80)); return true;
            case Keys.PageUp:
            case Keys.Shift | Keys.Space: _scroll.ScrollBy(-(_scroll.Height - S(80))); return true;
            case Keys.Home: _scroll.ScrollTo(0); return true;
            case Keys.End: _scroll.ScrollTo(_scroll.MaxOffset); return true;
            case Keys.Control | Keys.Oemplus:
            case Keys.Control | Keys.Add: Zoom(1); return true;
            case Keys.Control | Keys.OemMinus:
            case Keys.Control | Keys.Subtract: Zoom(-1); return true;
        }
        return false;
    }

    /// <summary>One section of the notes: heading, page, text, "mark as read".</summary>
    private sealed class SectionView : Column
    {
        public SectionView(Item item, int index, Action changed)
        {
            Gap = S(14);
            Padding = Pad(0, index == 0 ? 0 : 26, 0, 14);
            var head = new Row { Gap = S(10), Align = VAlign.Center };
            var heading = item.Prompt.Length > 0 ? item.Prompt : F("Section {0}", index + 1);
            head.Controls.Add(new RichTextView("## " + heading, RichStyle.Study(Host.Settings.NotesFontSize, Theme.P.Text)).Flex());
            if (item.Page.Length > 0) head.Controls.Add(Chip.PageRef(item.Page));
            Controls.Add(head);
            Controls.Add(new RichTextView(item.Answer, RichStyle.Study(Host.Settings.NotesFontSize, Theme.P.Text)));
            var read = new ButtonEx(item.LastResult == Result.GotIt ? T("Read ✓") : T("Mark as read"), Icons.Check, item.LastResult == Result.GotIt ? Kind.Soft : Kind.Ghost, 34).Align(HAlign.Left);
            read.Click += (_, _) =>
            {
                bool now = item.LastResult != Result.GotIt;
                Host.Store.MarkNotesRead(item.Id, now);
                item.LastResult = now ? Result.GotIt : Result.None;
                read.Text = now ? T("Read ✓") : T("Mark as read");
                read.Style = now ? Kind.Soft : Kind.Ghost;
                read.Width = read.GetPreferredSize(Size.Empty).Width;
                read.Invalidate();
                changed();
            };
            Controls.Add(read);
            Controls.Add(new Divider());
        }
    }

    /// <summary>Clickable table of contents with read check marks.</summary>
    private sealed class TocList : UiControl, IHeightForWidth
    {
        private readonly List<Item> _items;
        private readonly Action<int> _go;
        private int _hover = -1;
        private int _current;

        public TocList(List<Item> items, Action<int> go)
        {
            _items = items;
            _go = go;
            Cursor = Cursors.Hand;
        }

        public int Current
        {
            get => _current;
            set { if (_current != value) { _current = value; Invalidate(); } }
        }

        private int RowH(int i, int width) => Math.Max(S(34), Gfx.WrappedHeight(Title(i), Fonts.Ui(9.5f), width - S(40)) + S(14));
        private string Title(int i) => _items[i].Prompt.Length > 0 ? TextUtil.StripMarkdown(_items[i].Prompt) : F("Section {0}", i + 1);

        public int HeightFor(int width)
        {
            int h = 0;
            for (int i = 0; i < _items.Count; i++) h += RowH(i, width);
            return h;
        }

        private Rectangle RowRect(int index)
        {
            int y = 0;
            for (int i = 0; i < index; i++) y += RowH(i, Width);
            return new Rectangle(0, y, Width, RowH(index, Width));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < _items.Count; i++) if (RowRect(i).Contains(e.Location)) h = i;
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (int i = 0; i < _items.Count; i++) if (RowRect(i).Contains(e.Location)) _go(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Smooth(g);
            var p = Theme.P;
            for (int i = 0; i < _items.Count; i++)
            {
                var r = RowRect(i);
                if (i == _current) Gfx.FillRound(g, p.AccentSoft, Rectangle.Inflate(r, 0, -S(2)), S(8));
                else if (i == _hover) Gfx.FillRound(g, Gfx.Blend(p.Bg, p.Text, 0.05f), Rectangle.Inflate(r, 0, -S(2)), S(8));
                bool read = _items[i].LastResult == Result.GotIt;
                Gfx.Glyph(g, read ? Icons.CheckCircle : "", Fonts.Icon(9.5f), read ? p.Success : p.Text3, new Rectangle(r.X + S(8), r.Y, S(20), r.Height));
                Gfx.Text(g, Title(i), Fonts.Ui(9.5f), i == _current ? p.AccentText : p.Text, new Rectangle(r.X + S(34), r.Y + S(7), r.Width - S(40), r.Height - S(10)), Gfx.Wrap);
            }
        }
    }
}
