namespace StudyApp.App.UI;

/// <summary>Clickable icon buttons painted inside another control (fast for long lists).</summary>
public sealed class PaintedButtons
{
    private sealed record Btn(string Key, string Glyph, string Tip, Action Action, Func<bool> Active, Color? ActiveColor);

    private readonly Control _owner;
    private readonly List<Btn> _buttons = new();
    private readonly Dictionary<string, Rectangle> _rects = new();
    private string? _hover;

    public PaintedButtons(Control owner) => _owner = owner;

    public void Add(string key, string glyph, string tip, Action action, Func<bool>? active = null, Color? activeColor = null) =>
        _buttons.Add(new Btn(key, glyph, tip, action, active ?? (() => false), activeColor));

    public int Count => _buttons.Count;
    public int Size => S(32);

    /// <summary>Lays the buttons out right-to-left ending at <paramref name="right"/>.</summary>
    public void Arrange(int right, int top)
    {
        _rects.Clear();
        int x = right;
        for (int i = _buttons.Count - 1; i >= 0; i--)
        {
            x -= Size;
            _rects[_buttons[i].Key] = new Rectangle(x, top, Size, Size);
            x -= S(2);
        }
    }

    public int TotalWidth => _buttons.Count * (Size + S(2));

    public bool Contains(Point p) => _rects.Values.Any(r => r.Contains(p));

    public bool MouseMove(Point p)
    {
        var hover = _rects.FirstOrDefault(r => r.Value.Contains(p)).Key;
        if (hover == _hover) return hover != null;
        _hover = hover;
        var tip = _buttons.FirstOrDefault(b => b.Key == hover)?.Tip ?? "";
        Tips.Set(_owner, tip);
        _owner.Cursor = hover != null ? Cursors.Hand : Cursors.Default;
        _owner.Invalidate();
        return hover != null;
    }

    public void MouseLeave()
    {
        _hover = null;
        _owner.Invalidate();
    }

    public bool Click(Point p)
    {
        foreach (var b in _buttons)
            if (_rects.TryGetValue(b.Key, out var r) && r.Contains(p))
            {
                b.Action();
                return true;
            }
        return false;
    }

    public void Paint(Graphics g, bool rowHovered, Color back)
    {
        var p = Theme.P;
        foreach (var b in _buttons)
        {
            if (!_rects.TryGetValue(b.Key, out var r)) continue;
            bool active = b.Active();
            if (!rowHovered && !active) continue;
            if (b.Key == _hover) Gfx.FillRound(g, Gfx.Blend(back, p.Text, p.Dark ? 0.12f : 0.07f), r, S(8));
            var color = active ? (b.ActiveColor ?? p.Accent) : b.Key == _hover ? p.Text : p.Text3;
            var glyph = active && b.Glyph == Icons.Flag ? "" : b.Glyph;
            Gfx.Glyph(g, glyph, Fonts.Icon(10.5f), color, r);
        }
    }
}

/// <summary>One study item in a list: type, question, answer preview, page, difficulty, flags and actions.</summary>
public sealed class ItemRow : UiControl, IHeightForWidth
{
    private readonly Item _item;
    private readonly bool _showContext;
    private readonly PaintedButtons _buttons;
    public event EventHandler? Changed;
    public event EventHandler? Deleted;
    public Action<Item>? OnOpen { get; set; }

    public ItemRow(Item item, bool showContext = false)
    {
        _item = item;
        _showContext = showContext;
        Cursor = Cursors.Hand;
        _buttons = new PaintedButtons(this);
        _buttons.Add("flag", Icons.Flag, T("Flag for later"), ToggleFlag, () => _item.Flagged, Theme.P.Warning);
        _buttons.Add("edit", Icons.Edit, T("Edit"), Edit);
        _buttons.Add("delete", Icons.Delete, T("Delete"), Delete);
    }

    public Item Item => _item;

    private const int MaxPromptLines = 3;
    private int Pad => S(16);
    private int IconW => S(36);
    private int TextLeft => Pad + IconW + S(14);
    private int TextWidth(int width) => Math.Max(S(80), width - TextLeft - _buttons.TotalWidth - Pad - S(8));

    private string PromptText => TextUtil.StripMarkdown(_item.Type == ItemTypes.Note && _item.Prompt.Length == 0 ? _item.Answer : _item.Prompt);

    private string AnswerPreview => _item.Type switch
    {
        ItemTypes.Note => TextUtil.StripMarkdown(_item.Answer),
        _ => T("Answer:") + " " + TextUtil.StripMarkdown(_item.CorrectAnswerText()),
    };

    public int HeightFor(int width)
    {
        int tw = TextWidth(width);
        var pf = Fonts.UiSemibold(10.5f);
        int h = Math.Min(Gfx.WrappedHeight(PromptText, pf, tw), pf.Height * MaxPromptLines);
        h += S(4) + Fonts.Ui(9.5f).Height;
        h += S(10) + S(24);
        if (_showContext) h += S(4) + Fonts.Ui(8.5f).Height;
        return Math.Max(S(76), h + Pad * 2 - S(4));
    }

    private void ToggleFlag()
    {
        _item.Flagged = !_item.Flagged;
        Host.Store.SetFlagged(_item.Id, _item.Flagged);
        Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Edit()
    {
        if (Dialogs.ItemEditorDialog.Edit(_item))
        {
            LayoutProps.Relayout(this);
            Invalidate();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Delete()
    {
        Host.Store.DeleteItem(_item.Id);
        Deleted?.Invoke(this, EventArgs.Empty);
        var id = _item.Id;
        Toast.Show(T("Item moved to the recycle bin."), Tone.Info, T("Undo"), () =>
        {
            Host.Store.RestoreItem(id);
            Host.Main?.ReloadPage();
        });
        Host.Main?.RefreshSidebar();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_buttons.MouseMove(e.Location)) Cursor = Cursors.Hand;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _buttons.MouseLeave();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        if (_buttons.Click(e.Location)) return;
        if (OnOpen != null) OnOpen(_item);
        else Edit();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.P.Bg);
        Gfx.Smooth(g);
        var p = Theme.P;
        var rect = new RectangleF(0, 0, Width, Height);
        var back = Gfx.Blend(p.Surface, Gfx.Blend(p.Surface, p.Text, p.Dark ? 0.05f : 0.025f), HoverAnim.Value);
        Gfx.FillRound(g, back, rect, S(10));
        Gfx.DrawRound(g, _item.WrongInSource ? Gfx.Blend(p.Border, p.Danger, 0.6f) : p.Border, rect, S(10));

        // Type icon
        var (glyph, tint) = TypeVisual(_item.Type);
        var circle = new RectangleF(Pad, Pad - S(2), IconW, IconW);
        Gfx.Circle(g, Gfx.Blend(p.Surface, tint, p.Dark ? 0.22f : 0.12f), circle);
        Gfx.Glyph(g, glyph, Fonts.Icon(11f), tint, Rectangle.Round(circle));

        int tw = TextWidth(Width);
        int y = Pad - S(2);
        var pf = Fonts.UiSemibold(10.5f);
        int ph = Math.Min(Gfx.WrappedHeight(PromptText, pf, tw), pf.Height * MaxPromptLines);
        Gfx.Text(g, PromptText, pf, p.Text, new Rectangle(TextLeft, y, tw, ph), Gfx.Wrap | TextFormatFlags.EndEllipsis);
        y += ph + S(4);
        var af = Fonts.Ui(9.5f);
        Gfx.Text(g, AnswerPreview.Replace('\n', ' '), af, p.Text2, new Rectangle(TextLeft, y, tw, af.Height), Gfx.Left | TextFormatFlags.EndEllipsis);
        y += af.Height + S(10);

        // Meta chips
        int x = TextLeft;
        void ChipAt(string text, Color fore, Color fill, string? chipGlyph = null)
        {
            var font = Fonts.UiSemibold(8.5f);
            int w = Gfx.Measure(text, font).Width + S(20) + (chipGlyph != null ? S(16) : 0);
            if (x + w > TextLeft + tw) return;
            var r = new RectangleF(x, y, w, S(22));
            Gfx.FillRound(g, fill, r, S(11));
            int tx = x + S(10);
            if (chipGlyph != null)
            {
                Gfx.Glyph(g, chipGlyph, Fonts.Icon(8f), fore, new Rectangle(tx - S(2), y, S(14), S(22)));
                tx += S(16);
            }
            Gfx.Text(g, text, font, fore, new Rectangle(tx, y, w - (tx - x), S(22)), Gfx.Left);
            x += w + S(6);
        }

        ChipAt(ItemTypes.DisplayName(_item.Type), p.Text2, p.Surface2);
        if (!string.IsNullOrWhiteSpace(_item.Page)) ChipAt(L.Page(_item.Page), p.AccentText, p.AccentSoft, Icons.Book);
        if (_item.Difficulty > 0)
        {
            var dc = _item.Difficulty <= 2 ? p.Success : _item.Difficulty == 3 ? p.Info : _item.Difficulty == 4 ? p.Warning : p.Danger;
            ChipAt(Difficulty.Label(_item.Difficulty), dc, Gfx.Blend(p.Surface, dc, p.Dark ? 0.2f : 0.12f));
        }
        if (_item.WrongInSource) ChipAt(T("Wrong in source"), p.Danger, p.DangerSoft, Icons.Bug);
        if (_item.SeenCount > 0)
        {
            var acc = _item.Accuracy;
            var c = acc >= 0.8 ? p.Success : acc >= 0.5 ? p.Warning : p.Danger;
            ChipAt(F("{0}/{1} correct", _item.CorrectCount, _item.SeenCount), c, Gfx.Blend(p.Surface, c, p.Dark ? 0.2f : 0.12f));
        }
        else if (_item.Type != ItemTypes.Note) ChipAt(T("New"), p.Info, p.InfoSoft);
        if (_item.PersonalNote.Length > 0) ChipAt(T("Note"), p.Text2, p.Surface2, Icons.Note);
        foreach (var tag in _item.Tags.Take(2)) ChipAt("#" + tag, p.Text3, p.Surface2);

        if (_showContext)
        {
            y += S(26);
            var cf = Fonts.Ui(8.5f);
            Gfx.Text(g, _item.SubjectName + "  ›  " + (_item.ChapterTitle.Length > 0 ? _item.ChapterTitle + "  ›  " : "") + _item.DeckTitle, cf, p.Text3,
                new Rectangle(TextLeft, y, tw, cf.Height), Gfx.Left | TextFormatFlags.EndEllipsis);
        }

        _buttons.Arrange(Width - Pad + S(4), Pad - S(6));
        _buttons.Paint(g, IsHover, back);
    }

    public static (string Glyph, Color Tint) TypeVisual(string type) => type switch
    {
        ItemTypes.Flashcard => (Icons.Layers, Theme.P.Accent),
        ItemTypes.Single => (Icons.CheckCircle, Theme.P.Info),
        ItemTypes.Multiple => (Icons.List, Theme.P.Info),
        ItemTypes.TrueFalse => (Icons.Question, Theme.P.Warning),
        ItemTypes.Short => (Icons.Edit, Theme.P.Success),
        ItemTypes.Note => (Icons.Reading, Theme.P.Text2),
        _ => (Icons.Document, Theme.P.Text2),
    };
}

/// <summary>Card for a subject on the Subjects and Home pages.</summary>
public sealed class SubjectCard : UiControl, IHeightForWidth
{
    private readonly Subject _subject;
    private readonly SubjectSummary _summary;
    private readonly PaintedButtons _buttons;
    public event EventHandler? Open;
    public event EventHandler<Control>? MenuRequested;

    public SubjectCard(Subject subject, SubjectSummary summary)
    {
        _subject = subject;
        _summary = summary;
        Cursor = Cursors.Hand;
        _buttons = new PaintedButtons(this);
        _buttons.Add("more", Icons.More, T("More actions"), () => MenuRequested?.Invoke(this, this));
    }

    public Subject Subject => _subject;

    public int HeightFor(int width) => S(196);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_buttons.MouseMove(e.Location)) Cursor = Cursors.Hand;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _buttons.MouseLeave();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Right) { MenuRequested?.Invoke(this, this); return; }
        if (_buttons.Click(e.Location)) return;
        Open?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.P.Bg);
        Gfx.Smooth(g);
        var p = Theme.P;
        var color = Gfx.FromHex(_subject.Color, p.Accent);
        float h = HoverAnim.Value;
        var rect = new RectangleF(0, S(2) - S(2) * h, Width, Height - S(4));
        Gfx.FillRound(g, Theme.IsDark ? Color.FromArgb((int)(40 + 30 * h), 0, 0, 0) : Color.FromArgb((int)(14 + 18 * h), 20, 30, 60),
            new RectangleF(rect.X, rect.Y + S(2) + S(2) * h, rect.Width, rect.Height), S(14));
        Gfx.FillRound(g, p.Surface, rect, S(14));
        Gfx.DrawRound(g, Gfx.Blend(p.Border, color, 0.45f * h), rect, S(14), 1f + h * 0.4f);

        // Colour band
        using (var clip = Gfx.Round(rect, S(14)))
        {
            g.SetClip(clip);
            using var band = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(rect.X, rect.Y, rect.Width, S(6)), color, Gfx.Blend(color, Color.White, 0.25f), 0f);
            g.FillRectangle(band, rect.X, rect.Y, rect.Width, S(6));
            g.ResetClip();
        }

        int pad = S(18);
        float top = rect.Y + S(20);
        var badge = new RectangleF(pad, top, S(44), S(44));
        Gfx.FillRound(g, Gfx.Blend(p.Surface, color, p.Dark ? 0.28f : 0.15f), badge, S(12));
        Gfx.Glyph(g, Icons.ForSubject(_subject.Icon), Fonts.Icon(15f), color, Rectangle.Round(badge));

        int textX = pad + S(56);
        int textW = Width - textX - pad - S(30);
        Gfx.Text(g, _subject.Name, Fonts.Heading(12.5f), p.Text, new Rectangle(textX, (int)top - S(1), textW, Fonts.Heading(12.5f).Height + S(2)), Gfx.Left);
        var sub = _subject.Source.Length > 0 ? _subject.Source : _subject.Description;
        if (sub.Length > 0) Gfx.Text(g, sub, Fonts.Ui(9f), p.Text2, new Rectangle(textX, (int)top + S(24), textW, Fonts.Ui(9f).Height + S(2)), Gfx.Left);

        _buttons.Arrange(Width - S(10), (int)top - S(6));
        _buttons.Paint(g, IsHover, p.Surface);

        if (_subject.Archived)
        {
            var ar = new Rectangle(Width - S(110), (int)top + S(24), S(92), S(20));
            Gfx.Text(g, T("Archived"), Fonts.UiSemibold(8f), p.Text3, ar, Gfx.Left | TextFormatFlags.Right);
        }

        // Stats line
        float y = top + S(64);
        var stats = new List<string>();
        if (_summary.Chapters > 0) stats.Add(N(_summary.Chapters, "chapter", "chapters"));
        stats.Add(N(_summary.Items, "item", "items"));
        if (_summary.Notes > 0) stats.Add(N(_summary.Notes, "note", "notes"));
        Gfx.Text(g, string.Join("  ·  ", stats), Fonts.Ui(9f), p.Text2, new Rectangle(pad, (int)y, Width - pad * 2, S(18)), Gfx.Left);

        // Progress
        y += S(28);
        var bar = new RectangleF(pad, y, Width - pad * 2, S(8));
        Gfx.FillRound(g, p.Surface3, bar, S(4));
        if (_summary.Items > 0)
        {
            float mastered = (float)_summary.Mastered / _summary.Items;
            float learning = (float)_summary.Learning / _summary.Items;
            using var clip = Gfx.Round(bar, S(4));
            g.SetClip(clip);
            using (var b1 = new SolidBrush(p.Success)) g.FillRectangle(b1, bar.X, bar.Y, bar.Width * mastered, bar.Height);
            using (var b2 = new SolidBrush(Gfx.Blend(p.Surface, color, 0.65f))) g.FillRectangle(b2, bar.X + bar.Width * mastered, bar.Y, bar.Width * learning, bar.Height);
            g.ResetClip();
        }
        y += S(16);
        var masteredText = _summary.Items == 0 ? T("Empty – import a file to start") : F("{0:0}% mastered", _summary.MasteredShare * 100);
        Gfx.Text(g, masteredText, Fonts.Ui(8.5f), p.Text3, new Rectangle(pad, (int)y, Width / 2, S(18)), Gfx.Left);

        if (_summary.DueToday > 0)
        {
            var text = F("{0} due", _summary.DueToday);
            var font = Fonts.UiSemibold(8.5f);
            int w = Gfx.Measure(text, font).Width + S(18);
            var r = new RectangleF(Width - pad - w, y - S(2), w, S(22));
            Gfx.FillRound(g, p.AccentSoft, r, S(11));
            Gfx.Text(g, text, font, p.AccentText, Rectangle.Round(r), Gfx.Center);
        }
        else if (_summary.Items > 0 && _summary.New == 0)
        {
            Gfx.Text(g, T("All caught up"), Fonts.UiSemibold(8.5f), p.Success, new Rectangle(Width / 2, (int)y, Width / 2 - pad, S(18)), Gfx.Left | TextFormatFlags.Right);
        }
    }
}

/// <summary>Two columns side by side on wide screens, stacked on narrow ones.</summary>
public sealed class TwoColumns : LayoutPanel
{
    private readonly Control _left;
    private readonly Control _right;
    private readonly float _leftShare;
    public int Gap { get; set; } = S(18);
    public int StackBelow { get; set; } = S(820);

    public TwoColumns(Control left, Control right, float leftShare = 0.6f)
    {
        _left = left;
        _right = right;
        _leftShare = leftShare;
        Controls.Add(left);
        Controls.Add(right);
    }

    private bool Stacked(int width) => width < StackBelow;

    private (int Left, int Right) Widths(int width)
    {
        int l = (int)((width - Gap) * _leftShare);
        return (l, width - Gap - l);
    }

    public override int HeightFor(int width)
    {
        if (Stacked(width))
            return LayoutProps.HeightOf(_left, width) + Gap + LayoutProps.HeightOf(_right, width);
        var (l, r) = Widths(width);
        return Math.Max(LayoutProps.HeightOf(_left, l), LayoutProps.HeightOf(_right, r));
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (Stacked(Width))
        {
            int lh = LayoutProps.HeightOf(_left, Width);
            Place(_left, 0, 0, Width, lh);
            Place(_right, 0, lh + Gap, Width, LayoutProps.HeightOf(_right, Width));
            return;
        }
        var (l, r) = Widths(Width);
        Place(_left, 0, 0, l, LayoutProps.HeightOf(_left, l));
        Place(_right, l + Gap, 0, r, LayoutProps.HeightOf(_right, r));
    }
}
