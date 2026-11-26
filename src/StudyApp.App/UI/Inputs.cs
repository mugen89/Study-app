namespace StudyApp.App.UI;

/// <summary>Rounded text input (wraps a borderless TextBox) with placeholder, icon and focus ring.</summary>
public sealed class InputBox : UiControl, IHeightForWidth
{
    private readonly string? _glyph;
    public TextBox Box { get; }
    public event EventHandler? Changed;
    public event EventHandler? Submitted;

    public InputBox(string placeholder = "", bool multiline = false, int lines = 4, string? glyph = null, float fontPt = 10.5f, bool mono = false)
    {
        _glyph = glyph;
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Multiline = multiline,
            Font = mono ? Fonts.Code(fontPt - 0.5f) : Fonts.Ui(fontPt),
            PlaceholderText = placeholder,
            BackColor = InputBack,
            ForeColor = Theme.P.Text,
            WordWrap = true,
            AcceptsReturn = multiline,
            AcceptsTab = false,
            ScrollBars = multiline && lines >= 8 ? ScrollBars.Vertical : ScrollBars.None,
        };
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Box.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Box.KeyDown += (_, e) =>
        {
            if (!multiline && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Submitted?.Invoke(this, EventArgs.Empty);
            }
            if (e.Control && e.KeyCode == Keys.A)
            {
                Box.SelectAll();
                e.SuppressKeyPress = true;
            }
        };
        Box.HandleCreated += (_, _) => Native.ThemeScrollbars(Box);
        Height = multiline ? Box.Font.Height * lines + S(20) : S(40);
        Cursor = Cursors.IBeam;
    }

    public static Color InputBack => Theme.IsDark ? Theme.P.Surface2 : Theme.P.Surface;

    /// <summary>Text with "\n" line breaks (the Windows text box needs "\r\n" internally).</summary>
    public override string Text
    {
        get => Box.Multiline ? Box.Text.Replace("\r\n", "\n") : Box.Text;
#pragma warning disable CS8765
        set => Box.Text = Box.Multiline ? (value ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") : (value ?? "");
#pragma warning restore CS8765
    }

    public string Placeholder
    {
        get => Box.PlaceholderText;
        set => Box.PlaceholderText = value;
    }

    public bool ReadOnly
    {
        get => Box.ReadOnly;
        set => Box.ReadOnly = value;
    }

    public int HeightFor(int width) => Height;

    /// <summary>Focuses the text box with the caret at the end (no accidental select-all).</summary>
    public new void Focus()
    {
        Box.Focus();
        Box.SelectionStart = Box.TextLength;
        Box.SelectionLength = 0;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Box.Focus();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int left = S(12) + (_glyph != null ? S(24) : 0);
        if (Box.Multiline)
            Box.SetBounds(left, S(10), Width - left - S(6), Height - S(18));
        else
            Box.SetBounds(left, (Height - Box.Height) / 2, Width - left - S(12), Box.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.Smooth(g);
        var p = Theme.P;
        var rect = new RectangleF(0, 0, Width, Height);
        Gfx.FillRound(g, InputBack, rect, S(8));
        bool focused = Box.Focused;
        var border = focused ? p.Accent : Gfx.Blend(p.Border, p.BorderStrong, HoverAnim.Value);
        Gfx.DrawRound(g, border, rect, S(8), focused ? Sf(1.6f) : 1f);
        if (_glyph != null)
            Gfx.Glyph(g, _glyph, Fonts.Icon(11f), p.Text3, new Rectangle(S(12), Box.Multiline ? S(8) : 0, S(18), Box.Multiline ? S(22) : Height));
    }
}

/// <summary>Themed replacement for a ComboBox: opens a menu with the options.</summary>
public sealed class DropDown : UiControl
{
    private int _selected = -1;
    public List<(string Text, object? Value)> Items { get; } = new();
    public event EventHandler? SelectionChanged;
    public string Placeholder { get; set; } = "";
    public string? Glyph { get; set; }

    public DropDown(int width = 220)
    {
        Height = S(40);
        Width = S(width);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    public DropDown Add(string text, object? value)
    {
        Items.Add((text, value));
        return this;
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var v = Items.Count == 0 ? -1 : Math.Clamp(value, -1, Items.Count - 1);
            if (v == _selected) return;
            _selected = v;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedValue
    {
        get => _selected >= 0 && _selected < Items.Count ? Items[_selected].Value : null;
        set => SelectedIndex = Items.FindIndex(i => Equals(i.Value, value));
    }

    public string SelectedText => _selected >= 0 && _selected < Items.Count ? Items[_selected].Text : "";

    public override Size GetPreferredSize(Size proposedSize) => Size;

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Open();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Space:
            case Keys.Enter:
            case Keys.F4:
                Open();
                e.Handled = true;
                break;
            case Keys.Down:
                SelectedIndex = Math.Min(Items.Count - 1, _selected + 1);
                e.Handled = true;
                break;
            case Keys.Up:
                SelectedIndex = Math.Max(0, _selected - 1);
                e.Handled = true;
                break;
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    private void Open()
    {
        var menu = Menus.Create();
        menu.MaximumSize = new Size(Math.Max(Width, S(220)), S(440));
        for (int i = 0; i < Items.Count; i++)
        {
            int index = i;
            var item = Menus.Item(Items[i].Text, null, () => SelectedIndex = index);
            item.Checked = i == _selected;
            menu.Items.Add(item);
        }
        menu.MinimumSize = new Size(Width, 0);
        menu.Show(this, new Point(0, Height + S(4)));
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        var rect = new RectangleF(0, 0, Width, Height);
        Gfx.FillRound(g, Gfx.Blend(InputBox.InputBack, Gfx.Blend(InputBox.InputBack, p.Text, 0.04f), HoverAnim.Value), rect, S(8));
        Gfx.DrawRound(g, Focused ? p.Accent : Gfx.Blend(p.Border, p.BorderStrong, HoverAnim.Value), rect, S(8), Focused ? Sf(1.6f) : 1f);
        int x = S(12);
        if (Glyph != null)
        {
            Gfx.Glyph(g, Glyph, Fonts.Icon(11f), p.Text2, new Rectangle(x, 0, S(18), Height));
            x += S(26);
        }
        var text = _selected >= 0 ? SelectedText : Placeholder;
        Gfx.Text(g, text, Fonts.Ui(10f), _selected >= 0 ? (Enabled ? p.Text : p.Text3) : p.Text3, new Rectangle(x, 0, Width - x - S(36), Height), Gfx.Left | TextFormatFlags.EndEllipsis);
        Gfx.Glyph(g, Icons.ChevronDown, Fonts.Icon(9f), p.Text2, new Rectangle(Width - S(32), 0, S(24), Height));
    }
}

/// <summary>Checkbox with label and optional description.</summary>
public sealed class CheckBoxEx : UiControl, IHeightForWidth
{
    public override bool SizesToContent => true;

    private bool _checked;
    private readonly string _description;
    public event EventHandler? CheckedChanged;

    public CheckBoxEx(string text, bool isChecked = false, string description = "")
    {
        Text = text;
        _checked = isChecked;
        _description = description;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        Height = S(28);
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int TextLeft => S(30);

    public int HeightFor(int width)
    {
        int h = Math.Max(S(22), Gfx.WrappedHeight(Text, Fonts.Ui(10f), width - TextLeft));
        if (_description.Length > 0) h += S(2) + Gfx.WrappedHeight(_description, Fonts.Ui(9f), width - TextLeft);
        return h + S(4);
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(TextLeft + Gfx.Measure(Text, Fonts.Ui(10f)).Width + S(4), HeightFor(S(400)));

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        var box = new RectangleF(S(1), S(2), S(20), S(20));
        if (_checked)
        {
            Gfx.FillRound(g, Enabled ? p.Accent : p.BorderStrong, box, S(5));
            Gfx.Glyph(g, Icons.Check, Fonts.Icon(9f), p.OnAccent, Rectangle.Round(box));
        }
        else
        {
            Gfx.FillRound(g, InputBox.InputBack, box, S(5));
            Gfx.DrawRound(g, Gfx.Blend(p.BorderStrong, p.Accent, HoverAnim.Value * 0.7f), box, S(5), Sf(1.4f));
        }
        if (Focused && ShowFocusCues) Gfx.DrawRound(g, p.Accent, RectangleF.Inflate(box, S(2), S(2)), S(7), 1.5f);

        var font = Fonts.Ui(10f);
        int textH = Gfx.WrappedHeight(Text, font, Width - TextLeft);
        Gfx.Text(g, Text, font, Enabled ? p.Text : p.Text3, new Rectangle(TextLeft, S(2), Width - TextLeft, textH), Gfx.Wrap);
        if (_description.Length > 0)
            Gfx.Text(g, _description, Fonts.Ui(9f), p.Text2, new Rectangle(TextLeft, S(4) + textH, Width - TextLeft, Height - textH), Gfx.Wrap);
    }
}

/// <summary>On/off switch with a title and description (settings rows).</summary>
public sealed class Toggle : UiControl, IHeightForWidth
{
    private bool _on;
    private readonly string _description;
    private readonly Anim _knob;
    public event EventHandler? Toggled;

    public Toggle(string text, bool on, string description = "")
    {
        Text = text;
        _on = on;
        _description = description;
        _knob = new Anim(this, 160);
        _knob.Set(on ? 1 : 0);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    public bool On
    {
        get => _on;
        set
        {
            if (_on == value) return;
            _on = value;
            _knob.To(value ? 1 : 0);
            Toggled?.Invoke(this, EventArgs.Empty);
        }
    }

    private int SwitchW => S(44);

    public int HeightFor(int width)
    {
        int textW = width - SwitchW - S(16);
        int h = Gfx.WrappedHeight(Text, Fonts.UiSemibold(10f), textW);
        if (_description.Length > 0) h += S(3) + Gfx.WrappedHeight(_description, Fonts.Ui(9f), textW);
        return Math.Max(S(26), h) + S(4);
    }

    protected override void OnClick(EventArgs e)
    {
        On = !On;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) { On = !On; e.Handled = true; }
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        int textW = Width - SwitchW - S(16);
        var title = Fonts.UiSemibold(10f);
        int th = Gfx.WrappedHeight(Text, title, textW);
        Gfx.Text(g, Text, title, p.Text, new Rectangle(0, S(1), textW, th), Gfx.Wrap);
        if (_description.Length > 0)
            Gfx.Text(g, _description, Fonts.Ui(9f), p.Text2, new Rectangle(0, th + S(4), textW, Height - th), Gfx.Wrap);

        float t = _knob.Value;
        var track = new RectangleF(Width - SwitchW, S(2), SwitchW, S(24));
        var off = Gfx.Blend(p.Surface3, p.BorderStrong, HoverAnim.Value * 0.5f);
        Gfx.FillRound(g, Gfx.Blend(off, p.Accent, t), track, track.Height / 2);
        float d = track.Height - S(6);
        var knob = new RectangleF(track.X + S(3) + (track.Width - d - S(6)) * t, track.Y + S(3), d, d);
        Gfx.Circle(g, t > 0.5f ? p.OnAccent : (p.Dark ? p.Text2 : Color.White), knob);
        if (Focused && ShowFocusCues) Gfx.DrawRound(g, p.Accent, RectangleF.Inflate(track, S(2), S(2)), track.Height / 2 + S(2), 1.5f);
    }
}

/// <summary>Row of mutually exclusive options ("Light | Dark | Sepia").</summary>
public sealed class Segmented : UiControl
{
    public override bool SizesToContent => true;

    private readonly string[] _options;
    private int _selected;
    private int _hover = -1;
    public event EventHandler? SelectionChanged;

    public Segmented(int selected, params string[] options)
    {
        _options = options;
        _selected = selected;
        Height = S(38);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        Width = GetPreferredSize(Size.Empty).Width;
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var v = Math.Clamp(value, 0, _options.Length - 1);
            if (v == _selected) return;
            _selected = v;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int SegmentWidth(int i) => Gfx.Measure(_options[i], Fonts.UiSemibold(9.5f)).Width + S(28);

    public override Size GetPreferredSize(Size proposedSize) =>
        new(_options.Select((_, i) => SegmentWidth(i)).Sum() + S(8), Height);

    private Rectangle SegmentRect(int index)
    {
        int total = _options.Select((_, i) => SegmentWidth(i)).Sum();
        float scale = (float)(Width - S(8)) / Math.Max(1, total);
        float x = S(4);
        for (int i = 0; i < index; i++) x += SegmentWidth(i) * scale;
        return new Rectangle((int)x, S(4), (int)(SegmentWidth(index) * scale), Height - S(8));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        for (int i = 0; i < _options.Length; i++) if (SegmentRect(i).Contains(e.Location)) hover = i;
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
        for (int i = 0; i < _options.Length; i++) if (SegmentRect(i).Contains(e.Location)) SelectedIndex = i;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) { SelectedIndex--; e.Handled = true; }
        if (e.KeyCode == Keys.Right) { SelectedIndex++; e.Handled = true; }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        var rect = new RectangleF(0, 0, Width, Height);
        Gfx.FillRound(g, p.Dark ? p.Surface2 : Gfx.Blend(p.Surface2, p.Surface3, 0.6f), rect, S(10));
        for (int i = 0; i < _options.Length; i++)
        {
            var r = SegmentRect(i);
            bool sel = i == _selected;
            if (sel)
            {
                Gfx.FillRound(g, p.Dark ? p.Surface3 : p.Surface, r, S(7));
                if (!p.Dark) Gfx.DrawRound(g, p.Border, r, S(7));
            }
            else if (i == _hover)
            {
                Gfx.FillRound(g, Gfx.Blend(p.Surface2, p.Text, 0.05f), r, S(7));
            }
            Gfx.Text(g, _options[i], Fonts.UiSemibold(9.5f), sel ? p.Text : p.Text2, r, Gfx.Center);
        }
        if (Focused && ShowFocusCues) Gfx.DrawRound(g, p.Accent, rect, S(10), 1.5f);
    }
}

/// <summary>Vertical list of radio "cards" with title and description.</summary>
public sealed class ChoiceList : UiControl, IHeightForWidth
{
    private readonly List<(string Title, string Description)> _choices = new();
    private int _selected;
    private int _hover = -1;
    public event EventHandler? SelectionChanged;

    public ChoiceList(int selected, params (string Title, string Description)[] choices)
    {
        _choices.AddRange(choices);
        _selected = selected;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var v = Math.Clamp(value, 0, _choices.Count - 1);
            if (v == _selected) return;
            _selected = v;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int ItemHeight(int i, int width)
    {
        int textW = width - S(52);
        int h = Gfx.WrappedHeight(_choices[i].Title, Fonts.UiSemibold(10f), textW);
        if (_choices[i].Description.Length > 0) h += S(2) + Gfx.WrappedHeight(_choices[i].Description, Fonts.Ui(9f), textW);
        return h + S(22);
    }

    public int HeightFor(int width)
    {
        int h = 0;
        for (int i = 0; i < _choices.Count; i++) h += ItemHeight(i, width) + (i > 0 ? S(8) : 0);
        return h;
    }

    private Rectangle ItemRect(int index)
    {
        int y = 0;
        for (int i = 0; i < index; i++) y += ItemHeight(i, Width) + S(8);
        return new Rectangle(0, y, Width, ItemHeight(index, Width));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        for (int i = 0; i < _choices.Count; i++) if (ItemRect(i).Contains(e.Location)) hover = i;
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
        for (int i = 0; i < _choices.Count; i++) if (ItemRect(i).Contains(e.Location)) SelectedIndex = i;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Up) { SelectedIndex--; e.Handled = true; }
        if (e.KeyCode == Keys.Down) { SelectedIndex++; e.Handled = true; }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        for (int i = 0; i < _choices.Count; i++)
        {
            var r = ItemRect(i);
            bool sel = i == _selected;
            var fill = sel ? p.AccentSoft : i == _hover ? Gfx.Blend(InputBox.InputBack, p.Text, 0.03f) : InputBox.InputBack;
            Gfx.FillRound(g, fill, r, S(10));
            Gfx.DrawRound(g, sel ? p.Accent : p.Border, r, S(10), sel ? Sf(1.5f) : 1f);

            var dot = new RectangleF(r.X + S(16), r.Y + S(13), S(18), S(18));
            Gfx.Circle(g, InputBox.InputBack, dot);
            using (var pen = new Pen(sel ? p.Accent : p.BorderStrong, Sf(1.5f))) g.DrawEllipse(pen, dot);
            if (sel) Gfx.Circle(g, p.Accent, RectangleF.Inflate(dot, -S(4), -S(4)));

            int textW = r.Width - S(52);
            int th = Gfx.WrappedHeight(_choices[i].Title, Fonts.UiSemibold(10f), textW);
            Gfx.Text(g, _choices[i].Title, Fonts.UiSemibold(10f), p.Text, new Rectangle(r.X + S(44), r.Y + S(11), textW, th), Gfx.Wrap);
            if (_choices[i].Description.Length > 0)
                Gfx.Text(g, _choices[i].Description, Fonts.Ui(9f), p.Text2, new Rectangle(r.X + S(44), r.Y + S(13) + th, textW, r.Height - th), Gfx.Wrap);
        }
    }
}

/// <summary>Horizontal slider with value label.</summary>
public sealed class Slider : UiControl
{
    private double _value;
    private bool _drag;
    public double Min { get; set; }
    public double Max { get; set; } = 100;
    public double Step { get; set; } = 1;
    public Func<double, string> Format { get; set; } = v => v.ToString("0");
    public event EventHandler? ValueChanged;

    public Slider(double min, double max, double value, double step = 1)
    {
        Min = min;
        Max = max;
        Step = step;
        _value = Math.Clamp(value, min, max);
        Height = S(36);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    public double Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(Math.Round(value / Step) * Step, Min, Max);
            if (Math.Abs(v - _value) < 1e-9) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int LabelWidth => S(64);
    private RectangleF Track => new(S(10), Height / 2f - S(2), Width - LabelWidth - S(20), S(4));

    private void SetFromX(int x)
    {
        var t = Track;
        double f = Math.Clamp((x - t.X) / t.Width, 0, 1);
        Value = Min + f * (Max - Min);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _drag = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Left or Keys.Down) { Value -= Step; e.Handled = true; }
        if (e.KeyCode is Keys.Right or Keys.Up) { Value += Step; e.Handled = true; }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        var t = Track;
        Gfx.FillRound(g, p.Surface3, t, t.Height / 2);
        float f = (float)((_value - Min) / Math.Max(1e-9, Max - Min));
        Gfx.FillRound(g, p.Accent, new RectangleF(t.X, t.Y, Math.Max(t.Height, t.Width * f), t.Height), t.Height / 2);
        float d = S(18) + S(2) * HoverAnim.Value;
        var knob = new RectangleF(t.X + t.Width * f - d / 2, Height / 2f - d / 2, d, d);
        Gfx.Circle(g, p.Accent, knob);
        Gfx.Circle(g, p.Dark ? p.Surface : Color.White, RectangleF.Inflate(knob, -S(4), -S(4)));
        if (Focused && ShowFocusCues) Gfx.DrawRound(g, p.Accent, RectangleF.Inflate(knob, S(3), S(3)), d, 1.5f);
        Gfx.Text(g, Format(_value), Fonts.UiSemibold(9.5f), p.Text, new Rectangle(Width - LabelWidth, 0, LabelWidth, Height), Gfx.Center);
    }
}

/// <summary>Number picker: − value +. Mouse wheel and arrow keys work too.</summary>
public sealed class Stepper : UiControl, IWheelTarget
{
    private int _value;
    private int _hoverPart;
    public int Min { get; set; }
    public int Max { get; set; } = 100;
    public int Step { get; set; } = 1;
    public Func<int, string> Format { get; set; } = v => v.ToString();
    public event EventHandler? ValueChanged;

    public Stepper(int min, int max, int value, int step = 1, int width = 150)
    {
        Min = min;
        Max = max;
        Step = step;
        _value = Math.Clamp(value, min, max);
        Size = new Size(S(width), S(40));
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, Min, Max);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Size GetPreferredSize(Size proposedSize) => Size;

    private Rectangle MinusRect => new(0, 0, Height, Height);
    private Rectangle PlusRect => new(Width - Height, 0, Height, Height);

    public bool HandleWheel(int delta)
    {
        if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) return false;
        Value += Math.Sign(delta) * Step;
        return true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int part = MinusRect.Contains(e.Location) ? -1 : PlusRect.Contains(e.Location) ? 1 : 0;
        Cursor = part != 0 ? Cursors.Hand : Cursors.Default;
        if (part != _hoverPart) { _hoverPart = part; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverPart = 0;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int mult = ModifierKeys.HasFlag(Keys.Shift) ? 10 : 1;
        if (MinusRect.Contains(e.Location)) Value -= Step * mult;
        if (PlusRect.Contains(e.Location)) Value += Step * mult;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Left or Keys.Down) { Value -= Step; e.Handled = true; }
        if (e.KeyCode is Keys.Right or Keys.Up) { Value += Step; e.Handled = true; }
        if (e.KeyCode == Keys.PageUp) { Value += Step * 10; e.Handled = true; }
        if (e.KeyCode == Keys.PageDown) { Value -= Step * 10; e.Handled = true; }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        var rect = new RectangleF(0, 0, Width, Height);
        Gfx.FillRound(g, InputBox.InputBack, rect, S(8));
        Gfx.DrawRound(g, Focused ? p.Accent : p.Border, rect, S(8), Focused ? Sf(1.6f) : 1f);
        foreach (var (r, glyph, part, enabled) in new[] { (MinusRect, "", -1, _value > Min), (PlusRect, Icons.Add, 1, _value < Max) })
        {
            if (_hoverPart == part && enabled) Gfx.FillRound(g, Gfx.Blend(InputBox.InputBack, p.Text, 0.07f), RectangleF.Inflate(r, -S(5), -S(5)), S(6));
            Gfx.Glyph(g, glyph, Fonts.Icon(9.5f), enabled ? p.Text : p.Text3, r);
        }
        Gfx.Text(g, Format(_value), Fonts.UiSemibold(10f), p.Text, new Rectangle(Height, 0, Width - Height * 2, Height), Gfx.Center);
    }
}

/// <summary>Colour picker made of round swatches plus a "custom colour" button.</summary>
public sealed class ColorSwatches : UiControl, IHeightForWidth
{
    private readonly List<Color> _colors;
    private Color _selected;
    private int _hover = -1;
    public event EventHandler? SelectedChanged;

    public ColorSwatches(IEnumerable<string> hexColors, Color selected)
    {
        _colors = hexColors.Select(h => Gfx.FromHex(h, Theme.DefaultAccent)).ToList();
        _selected = selected;
        Cursor = Cursors.Hand;
        Height = S(40);
    }

    public Color Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int Cell => S(38);
    private int PerRow(int width) => Math.Max(1, width / Cell);

    public int HeightFor(int width)
    {
        int count = _colors.Count + 1;
        int rows = (count + PerRow(width) - 1) / PerRow(width);
        return rows * Cell;
    }

    private Rectangle CellRect(int i)
    {
        int per = PerRow(Width);
        return new Rectangle(i % per * Cell, i / per * Cell, Cell, Cell);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        for (int i = 0; i <= _colors.Count; i++) if (CellRect(i).Contains(e.Location)) hover = i;
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
        for (int i = 0; i < _colors.Count; i++)
            if (CellRect(i).Contains(e.Location)) { Selected = _colors[i]; return; }
        if (CellRect(_colors.Count).Contains(e.Location))
        {
            using var dlg = new ColorDialog { Color = _selected, FullOpen = true, AnyColor = true };
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK) Selected = dlg.Color;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        bool customSelected = !_colors.Any(c => c.ToArgb() == _selected.ToArgb());
        for (int i = 0; i <= _colors.Count; i++)
        {
            var r = CellRect(i);
            var circle = new RectangleF(r.X + S(5), r.Y + S(5), r.Width - S(10), r.Height - S(10));
            if (i == _colors.Count)
            {
                if (customSelected) Gfx.Circle(g, _selected, circle);
                else using (var pen = new Pen(p.BorderStrong, Sf(1.5f)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawEllipse(pen, circle);
                Gfx.Glyph(g, Icons.Add, Fonts.Icon(9f), customSelected ? Gfx.OnColor(_selected) : p.Text2, Rectangle.Round(circle));
                if (customSelected) using (var pen = new Pen(p.Text, Sf(2))) g.DrawEllipse(pen, RectangleF.Inflate(circle, S(3), S(3)));
                Tips.Set(this, T("Custom colour"));
                continue;
            }
            var c = _colors[i];
            float grow = i == _hover ? S(1) : 0;
            Gfx.Circle(g, c, RectangleF.Inflate(circle, grow, grow));
            if (c.ToArgb() == _selected.ToArgb())
            {
                using var pen = new Pen(p.Text, Sf(2));
                g.DrawEllipse(pen, RectangleF.Inflate(circle, S(3), S(3)));
                Gfx.Glyph(g, Icons.Check, Fonts.Icon(9f), Gfx.OnColor(c), Rectangle.Round(circle));
            }
        }
    }
}
