namespace StudyApp.App.Dialogs;

/// <summary>Grid of icons to choose a subject icon.</summary>
public sealed class IconPicker : UiControl, IHeightForWidth
{
    private string _selected;
    private int _hover = -1;
    public Color Tint { get; set; } = Theme.P.Accent;

    public IconPicker(string selected)
    {
        _selected = selected;
        Cursor = Cursors.Hand;
    }

    public string Selected => _selected;
    private int Cell => S(40);
    private int PerRow(int width) => Math.Max(1, width / Cell);

    public int HeightFor(int width) => (Icons.SubjectIcons.Length + PerRow(width) - 1) / PerRow(width) * Cell;

    private Rectangle CellRect(int i)
    {
        int per = PerRow(Width);
        return new Rectangle(i % per * Cell, i / per * Cell, Cell, Cell);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        for (int i = 0; i < Icons.SubjectIcons.Length; i++) if (CellRect(i).Contains(e.Location)) hover = i;
        if (hover != _hover) { _hover = hover; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        for (int i = 0; i < Icons.SubjectIcons.Length; i++)
            if (CellRect(i).Contains(e.Location)) { _selected = Icons.SubjectIcons[i].Key; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        for (int i = 0; i < Icons.SubjectIcons.Length; i++)
        {
            var r = Rectangle.Inflate(CellRect(i), -S(3), -S(3));
            bool sel = Icons.SubjectIcons[i].Key == _selected;
            if (sel) Gfx.FillRound(g, Gfx.Blend(p.Surface, Tint, p.Dark ? 0.3f : 0.16f), r, S(8));
            else if (i == _hover) Gfx.FillRound(g, p.Surface2, r, S(8));
            Gfx.Glyph(g, Icons.SubjectIcons[i].Glyph, Fonts.Icon(12f), sel ? Tint : p.Text2, r);
        }
    }
}

public sealed class SubjectDialog : DialogBase
{
    private readonly InputBox _name;
    private readonly InputBox _source;
    private readonly InputBox _description;
    private readonly ColorSwatches _color;
    private readonly IconPicker _icon;
    private readonly Subject _subject;

    private SubjectDialog(Subject? existing) : base(existing == null ? T("New subject") : T("Edit subject"), 560,
        existing == null ? T("A subject is one course or textbook, e.g. \"Anatomy\". You can split it into chapters.") : null)
    {
        _subject = existing ?? new Subject { Color = Theme.AccentPresets[new Random().Next(Theme.AccentPresets.Length)] };
        _name = new InputBox(T("e.g. Human Anatomy")) { Text = _subject.Name };
        _source = new InputBox(T("e.g. Gray's Anatomy for Students, 4th edition")) { Text = _subject.Source };
        _description = new InputBox(T("Optional notes about this subject"), multiline: true, lines: 2) { Text = _subject.Description };
        _color = new ColorSwatches(Theme.AccentPresets, Gfx.FromHex(_subject.Color, Theme.DefaultAccent));
        _icon = new IconPicker(_subject.Icon) { Tint = _color.Selected };
        _color.SelectedChanged += (_, _) => { _icon.Tint = _color.Selected; _icon.Invalidate(); };

        Content.Controls.Add(Ui.Field(T("Name"), _name));
        Content.Controls.Add(Ui.Field(T("Textbook / source"), _source, T("Shown on items so you know where to re-read.")));
        Content.Controls.Add(Ui.Field(T("Description"), _description));
        Content.Controls.Add(Ui.Field(T("Colour"), _color));
        Content.Controls.Add(Ui.Field(T("Icon"), _icon));

        AddButton(T("Cancel"), Kind.Secondary, DialogResult.Cancel);
        var ok = AddAction(existing == null ? T("Create subject") : T("Save"), Kind.Primary, Save);
        AcceptButton = ok;
        Shown += (_, _) => _name.Focus();
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            _name.Focus();
            Toast.Show(T("Please enter a name."), Tone.Warning);
            return;
        }
        _subject.Name = _name.Text.Trim();
        _subject.Source = _source.Text.Trim();
        _subject.Description = _description.Text.Trim();
        _subject.Color = Gfx.ToHex(_color.Selected);
        _subject.Icon = _icon.Selected;
        DialogResult = DialogResult.OK;
    }

    public static Subject? Show(Subject? existing = null)
    {
        using var d = new SubjectDialog(existing);
        return d.ShowDialog(Form.ActiveForm ?? Host.Main) == DialogResult.OK ? d._subject : null;
    }
}

public sealed class ChapterDialog : DialogBase
{
    private readonly InputBox _number;
    private readonly InputBox _title;
    private readonly InputBox _pages;
    private readonly Chapter _chapter;

    private ChapterDialog(Chapter chapter, bool isNew) : base(isNew ? T("New chapter") : T("Edit chapter"), 480)
    {
        _chapter = chapter;
        _number = new InputBox(T("e.g. 3")) { Text = chapter.Number };
        _title = new InputBox(T("e.g. The Skeletal System")) { Text = chapter.Title };
        _pages = new InputBox(T("e.g. 45-78")) { Text = chapter.Pages };
        var row = new Row { Gap = S(12) };
        _number.Width = S(110);
        row.Controls.Add(Ui.Field(T("Number"), _number).Also(c => c.Width = S(110)));
        row.Controls.Add(Ui.Field(T("Pages"), _pages).Flex());
        Content.Controls.Add(Ui.Field(T("Title"), _title));
        Content.Controls.Add(row);
        AddButton(T("Cancel"), Kind.Secondary, DialogResult.Cancel);
        AcceptButton = AddAction(isNew ? T("Add chapter") : T("Save"), Kind.Primary, Save);
        Shown += (_, _) => _title.Focus();
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_title.Text) && string.IsNullOrWhiteSpace(_number.Text))
        {
            _title.Focus();
            return;
        }
        _chapter.Number = _number.Text.Trim();
        _chapter.Title = string.IsNullOrWhiteSpace(_title.Text) ? F("Chapter {0}", _number.Text.Trim()) : _title.Text.Trim();
        _chapter.Pages = _pages.Text.Trim();
        DialogResult = DialogResult.OK;
    }

    public static Chapter? Show(Chapter chapter, bool isNew)
    {
        using var d = new ChapterDialog(chapter, isNew);
        return d.ShowDialog(Form.ActiveForm ?? Host.Main) == DialogResult.OK ? d._chapter : null;
    }
}

public sealed class DeckDialog : DialogBase
{
    private readonly Deck _deck;
    private readonly InputBox _title;
    private readonly InputBox _description;
    private readonly Segmented _kind;
    private readonly DropDown _chapter;
    private readonly Segmented _difficulty;

    private DeckDialog(Deck deck, bool isNew) : base(isNew ? T("New deck") : T("Edit deck"), 560)
    {
        _deck = deck;
        _title = new InputBox(T("e.g. Chapter 3 – key terms")) { Text = deck.Title };
        _description = new InputBox(T("Optional"), multiline: true, lines: 2) { Text = deck.Description };
        _kind = new Segmented((int)deck.Kind, T("Flashcards"), T("Test"), T("Notes"));
        _chapter = new DropDown(300);
        _chapter.Add(T("No chapter"), 0L);
        foreach (var c in Host.Store.GetChapters(deck.SubjectId)) _chapter.Add(c.DisplayName, c.Id);
        _chapter.SelectedValue = deck.ChapterId ?? 0L;
        if (_chapter.SelectedIndex < 0) _chapter.SelectedIndex = 0;
        _difficulty = new Segmented(deck.Difficulty, T("Not set"), "1", "2", "3", "4", "5");

        Content.Controls.Add(Ui.Field(T("Title"), _title));
        Content.Controls.Add(Ui.Field(T("Type"), _kind.Align(HAlign.Left)));
        Content.Controls.Add(Ui.Field(T("Chapter"), _chapter.Align(HAlign.Left)));
        Content.Controls.Add(Ui.Field(T("Difficulty"), _difficulty.Align(HAlign.Left), T("1 = very easy … 5 = expert")));
        Content.Controls.Add(Ui.Field(T("Description"), _description));
        AddButton(T("Cancel"), Kind.Secondary, DialogResult.Cancel);
        AcceptButton = AddAction(isNew ? T("Create deck") : T("Save"), Kind.Primary, Save);
        Shown += (_, _) => _title.Focus();
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            _title.Focus();
            return;
        }
        _deck.Title = _title.Text.Trim();
        _deck.Description = _description.Text.Trim();
        _deck.Kind = (DeckKind)_kind.SelectedIndex;
        _deck.ChapterId = _chapter.SelectedValue is long id && id > 0 ? id : null;
        _deck.Difficulty = _difficulty.SelectedIndex;
        DialogResult = DialogResult.OK;
    }

    public static Deck? Show(Deck deck, bool isNew)
    {
        using var d = new DeckDialog(deck, isNew);
        return d.ShowDialog(Form.ActiveForm ?? Host.Main) == DialogResult.OK ? d._deck : null;
    }
}

/// <summary>Personal note attached to an item ("remember: the mnemonic is…").</summary>
public static class NoteDialog
{
    public static string? Show(Item item)
    {
        using var d = new DialogBase(T("Personal note"), 520, T("Only you see this note. It is shown with the item while you study."));
        var input = new InputBox(T("Write a reminder, a mnemonic, or what you got wrong…"), multiline: true, lines: 6) { Text = item.PersonalNote };
        d.Content.Controls.Add(input);
        d.AddButton(T("Cancel"), Kind.Secondary, DialogResult.Cancel);
        var ok = d.AddButton(T("Save note"), Kind.Primary, DialogResult.OK);
        d.AcceptButton = ok;
        d.Shown += (_, _) => input.Focus();
        return d.ShowDialog(Form.ActiveForm ?? Host.Main) == DialogResult.OK ? input.Text.Trim() : null;
    }
}

public static class ShortcutsDialog
{
    public static void ShowDialogFor(IWin32Window? owner)
    {
        using var d = new DialogBase(T("Keyboard shortcuts"), 620);
        void Group(string title, params (string Keys, string What)[] rows)
        {
            var card = new Card(16) { Gap = S(6) };
            card.Controls.Add(new TextLabel(title, Fonts.UiSemibold(10.5f), Theme.P.Text));
            foreach (var (keys, what) in rows)
            {
                var row = new Row { Gap = S(12) };
                row.Controls.Add(new TextLabel(what, Fonts.Ui(9.5f), Theme.P.Text2).Flex());
                row.Controls.Add(Chip.Neutral(keys));
                card.Controls.Add(row);
            }
            d.Content.Controls.Add(card);
        }

        Group(T("Everywhere"),
            ("Ctrl+1 / 2 / 3", T("Home / Subjects / Review")),
            ("Ctrl+K", T("Search all items")),
            ("Ctrl+I", T("Import JSON")),
            ("Ctrl+P", T("AI prompt templates")),
            ("Ctrl+N", T("New subject")),
            ("Ctrl+,", T("Settings")),
            ("Ctrl+T", T("Switch light / dark theme")),
            ("Alt+←", T("Back")),
            ("F1", T("This list")));
        Group(T("Flashcards"),
            (T("Space"), T("Show answer")),
            ("1 2 3 4", T("Again / Hard / Good / Easy")),
            ("H", T("Show hint")),
            ("F", T("Flag for later")),
            ("N", T("Personal note")),
            ("E", T("Edit item")),
            ("W", T("Mark as wrong in source")),
            ("Ctrl+Z", T("Undo last answer")),
            ("Esc", T("End session")));
        Group(T("Tests"),
            ("1–9 / A–H", T("Choose an answer")),
            ("T / F", T("True / False")),
            ("Enter", T("Check answer / next question")),
            ("← →", T("Previous / next question")),
            ("Ctrl+Enter", T("Finish the test")));
        Group(T("Reading"),
            ("Ctrl + / Ctrl −", T("Bigger / smaller text")),
            ("↑ ↓ PgUp PgDn", T("Scroll")));
        var ok = d.AddButton(T("Close"), Kind.Primary, DialogResult.OK);
        d.AcceptButton = ok;
        d.ShowDialog(owner ?? Host.Main);
    }
}
