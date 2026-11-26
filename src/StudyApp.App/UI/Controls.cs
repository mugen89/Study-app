namespace StudyApp.App.UI;

public enum Kind { Primary, Secondary, Ghost, Soft, Danger, Success, Link }

/// <summary>Rounded button with optional icon, hover animation, keyboard support and shortcut tooltip.</summary>
public class ButtonEx : UiControl, IButtonControl
{
    public override bool SizesToContent => true;

    private string? _glyph;
    public Kind Style { get; set; }
    public bool IconOnly { get; set; }
    public string? Shortcut { get; set; }
    public float FontPt { get; set; } = 10f;
    public int MinWidth { get; set; }
    public Color? Tint { get; set; }
    public DialogResult DialogResult { get; set; }

    public string? Glyph
    {
        get => _glyph;
        set { _glyph = value; Invalidate(); }
    }

    public ButtonEx(string text = "", string? glyph = null, Kind style = Kind.Secondary, int height = 36)
    {
        Text = text;
        _glyph = glyph;
        Style = style;
        Height = S(height);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        Font = Fonts.UiSemibold(FontPt);
        Width = GetPreferredSize(Size.Empty).Width;
    }

    public ButtonEx WithTip(string tip, string? shortcut = null)
    {
        Shortcut = shortcut;
        Tips.Set(this, tip, shortcut);
        return this;
    }

    public ButtonEx OnClick(Action action)
    {
        Click += (_, _) => action();
        return this;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (IconOnly) return new Size(Height, Height);
        var font = Fonts.UiSemibold(FontPt);
        int pad = Style == Kind.Link ? S(2) : S(16);
        int w = pad * 2 + Gfx.Measure(Text, font).Width;
        if (!string.IsNullOrEmpty(_glyph)) w += S(16) + (string.IsNullOrEmpty(Text) ? 0 : S(8));
        return new Size(Math.Max(w, MinWidth), Height);
    }

    public void NotifyDefault(bool value) => Invalidate();

    public void PerformClick()
    {
        if (Enabled && Visible) OnClick(EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is Form f) f.DialogResult = DialogResult;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            PerformClick();
        }
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        float h = HoverAnim.Value;
        var back = BackColor;
        var accent = Tint ?? p.Accent;

        Color bg, fg, border = Color.Empty;
        switch (Style)
        {
            case Kind.Primary:
                bg = IsDown ? (Tint.HasValue ? Gfx.Darken(accent, 0.15f) : p.AccentPressed) : Gfx.Blend(accent, Tint.HasValue ? Gfx.Darken(accent, 0.08f) : p.AccentHover, h);
                fg = Tint.HasValue ? Gfx.OnColor(accent) : p.OnAccent;
                break;
            case Kind.Danger:
                bg = Gfx.Blend(p.Danger, Gfx.Darken(p.Danger, 0.1f), IsDown ? 1f : h);
                fg = Color.White;
                break;
            case Kind.Success:
                bg = Gfx.Blend(p.Success, Gfx.Darken(p.Success, 0.1f), IsDown ? 1f : h);
                fg = Color.White;
                break;
            case Kind.Soft:
                bg = Gfx.Blend(p.AccentSoft, p.AccentSoftHover, IsDown ? 1f : h);
                fg = p.AccentText;
                break;
            case Kind.Ghost:
                bg = Gfx.Blend(back, Gfx.Blend(back, p.Text, p.Dark ? 0.10f : 0.06f), IsDown ? 1f : h);
                fg = Gfx.Blend(p.Text2, p.Text, h);
                break;
            case Kind.Link:
                bg = back;
                fg = Gfx.Blend(p.AccentText, p.Accent, h);
                break;
            default:
                bg = Gfx.Blend(p.Surface, Gfx.Blend(p.Surface, p.Text, p.Dark ? 0.08f : 0.04f), IsDown ? 1f : h);
                fg = p.Text;
                border = p.BorderStrong;
                break;
        }
        if (!Enabled)
        {
            bg = Style is Kind.Ghost or Kind.Link ? back : Gfx.Blend(bg, back, 0.45f);
            fg = Gfx.Blend(fg, back, 0.55f);
        }

        var rect = new RectangleF(0, 0, Width, Height);
        float radius = IconOnly ? Height / 2f : S(8);
        if (Style != Kind.Link) Gfx.FillRound(g, bg, rect, radius);
        if (border != Color.Empty) Gfx.DrawRound(g, border, rect, radius);
        if (Focused && ShowFocusCues) Gfx.DrawRound(g, p.Accent, new RectangleF(1, 1, Width - 2, Height - 2), radius - 1, 2f);

        var font = Fonts.UiSemibold(FontPt);
        var iconFont = Fonts.Icon(FontPt + 1f);
        bool hasGlyph = !string.IsNullOrEmpty(_glyph);
        if (IconOnly || string.IsNullOrEmpty(Text))
        {
            if (hasGlyph) Gfx.Glyph(g, _glyph!, iconFont, fg, ClientRectangle);
            return;
        }

        int textW = Gfx.Measure(Text, font).Width;
        int iconW = hasGlyph ? S(16) + S(8) : 0;
        int x = (Width - textW - iconW) / 2;
        if (hasGlyph) Gfx.Glyph(g, _glyph!, iconFont, fg, new Rectangle(x, 0, S(16), Height));
        Gfx.Text(g, Text, font, fg, new Rectangle(x + iconW, 0, textW + S(2), Height), Gfx.Left);
        if (Style == Kind.Link && h > 0.5f)
        {
            using var pen = new Pen(fg, Math.Max(1f, Sf(1)));
            int baseline = Height / 2 + font.Height / 2 - S(1);
            g.DrawLine(pen, x + iconW, baseline, x + iconW + textW, baseline);
        }
    }

    public static ButtonEx Icon(string glyph, string tip, string? shortcut = null, Kind style = Kind.Ghost, int size = 34)
    {
        var b = new ButtonEx("", glyph, style, size) { IconOnly = true };
        b.Width = b.Height;
        b.WithTip(tip, shortcut);
        return b;
    }
}

/// <summary>Text that wraps and reports its height (labels, paragraphs, titles).</summary>
public class TextLabel : UiControl, IHeightForWidth
{
    public override bool SizesToContent => true;

    private bool _wrap;
    public int MaxLines { get; set; }
    public bool IsLink { get; set; }

    public TextLabel(string text, Font? font = null, Color? color = null, bool wrap = true)
    {
        Text = text;
        Font = font ?? Fonts.Ui(10f);
        ForeColor = color ?? Theme.P.Text;
        _wrap = wrap;
        Height = Font.Height;
    }

    public bool Wrap
    {
        get => _wrap;
        set { _wrap = value; Invalidate(); }
    }

    public int HeightFor(int width)
    {
        if (!_wrap) return Font.Height + S(2);
        int h = Gfx.WrappedHeight(Text, Font, width);
        if (MaxLines > 0) h = Math.Min(h, Font.Height * MaxLines);
        return h;
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Gfx.Measure(Text, Font).Width + S(2), Font.Height + S(2));

    protected override void OnMouseEnter(EventArgs e)
    {
        if (IsLink) Cursor = Cursors.Hand;
        base.OnMouseEnter(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var color = IsLink ? Gfx.Blend(Theme.P.AccentText, Theme.P.Accent, HoverAnim.Value) : ForeColor;
        if (!Enabled) color = Gfx.Blend(color, BackColor, 0.5f);
        var flags = _wrap ? Gfx.Wrap | (MaxLines > 0 ? TextFormatFlags.EndEllipsis : 0) : Gfx.Left | TextFormatFlags.EndEllipsis;
        Gfx.Text(e.Graphics, Text, Font, color, ClientRectangle, flags);
    }
}

public static class Ui
{
    public static TextLabel Title(string text, float pt = 20f) => new(text, Fonts.Heading(pt), Theme.P.Text);
    public static TextLabel Heading(string text, float pt = 13f) => new(text, Fonts.Heading(pt), Theme.P.Text);
    public static TextLabel Body(string text, float pt = 10f) => new(text, Fonts.Ui(pt), Theme.P.Text);
    public static TextLabel Muted(string text, float pt = 9.5f) => new(text, Fonts.Ui(pt), Theme.P.Text2);
    public static TextLabel Faint(string text, float pt = 9f) => new(text, Fonts.Ui(pt), Theme.P.Text3);
    public static TextLabel Strong(string text, float pt = 10f) => new(text, Fonts.UiSemibold(pt), Theme.P.Text);

    public static Column Stack(int gap = 12, params Control?[] children)
    {
        var c = new Column { Gap = S(gap) };
        c.AddRange(children);
        return c;
    }

    public static Row Row(int gap = 8, params Control?[] children)
    {
        var r = new Row { Gap = S(gap) };
        r.AddRange(children);
        return r;
    }

    /// <summary>A label above an input ("Name", "Pages"…), optionally with a hint below.</summary>
    public static Column Field(string label, Control input, string? hint = null)
    {
        var c = new Column { Gap = S(6) };
        c.Controls.Add(new TextLabel(label, Fonts.UiSemibold(9.5f), Theme.P.Text2));
        c.Controls.Add(input);
        if (!string.IsNullOrEmpty(hint)) c.Controls.Add(Faint(hint));
        return c;
    }
}

/// <summary>Rounded surface that stacks its children vertically (the main building block of pages).</summary>
public class Card : Column
{
    private Color? _fill;
    public int Radius { get; set; } = S(12);
    public Color? Stroke { get; set; }

    /// <summary>Card colour; children inherit it as their background.</summary>
    public Color? Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            if (value.HasValue) BackColor = value.Value;
        }
    }
    public Color? AccentStripe { get; set; }

    public Card(int padding = 20)
    {
        Padding = Pad(padding);
        BackColor = Theme.P.Surface;
        Gap = S(10);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.P.Bg);
        Gfx.Smooth(g);
        var rect = new RectangleF(0, 0, Width, Height - Sf(1));
        var shadowLine = new RectangleF(0, Sf(1), Width, Height - Sf(1));
        Gfx.FillRound(g, Theme.IsDark ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(14, 20, 30, 60), shadowLine, Radius);
        Gfx.FillRound(g, Fill ?? BackColor, rect, Radius);
        Gfx.DrawRound(g, Stroke ?? Theme.P.Border, rect, Radius);
        if (AccentStripe is Color stripe)
        {
            using var clip = Gfx.Round(rect, Radius);
            g.SetClip(clip);
            using var b = new SolidBrush(stripe);
            g.FillRectangle(b, 0, 0, S(4), Height);
            g.ResetClip();
        }
    }
}

/// <summary>Small rounded label: difficulty, page reference, item type…</summary>
public sealed class Chip : UiControl
{
    public override bool SizesToContent => true;

    public string? Glyph { get; set; }
    public Color Fore { get; set; }
    public Color Back { get; set; }
    public bool Clickable { get; set; }

    public Chip(string text, Color fore, Color back, string? glyph = null)
    {
        Text = text;
        Fore = fore;
        Back = back;
        Glyph = glyph;
        Font = Fonts.UiSemibold(8.5f);
        Height = S(24);
        Width = GetPreferredSize(Size.Empty).Width;
    }

    public static Chip Neutral(string text, string? glyph = null) => new(text, Theme.P.Text2, Theme.P.Surface2, glyph);
    public static Chip Accent(string text, string? glyph = null) => new(text, Theme.P.AccentText, Theme.P.AccentSoft, glyph);
    public static Chip Status(string text, Color color, string? glyph = null) => new(text, color, Gfx.Blend(Theme.P.Surface, color, Theme.IsDark ? 0.22f : 0.13f), glyph);

    public static Chip PageRef(string page) => new(L.Page(page), Theme.P.AccentText, Theme.P.AccentSoft, Icons.Book);

    public static Chip DifficultyChip(int d)
    {
        var color = d switch
        {
            1 or 2 => Theme.P.Success,
            3 => Theme.P.Info,
            4 => Theme.P.Warning,
            _ => Theme.P.Danger,
        };
        return Status(Difficulty.Label(d), color);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int w = S(10) * 2 + Gfx.Measure(Text, Font).Width;
        if (!string.IsNullOrEmpty(Glyph)) w += S(12) + S(5);
        return new Size(w, Height);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        if (Clickable) Cursor = Cursors.Hand;
        base.OnMouseEnter(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var back = Clickable ? Gfx.Blend(Back, Gfx.Blend(Back, Fore, 0.15f), HoverAnim.Value) : Back;
        Gfx.FillRound(g, back, new RectangleF(0, 0, Width, Height), Height / 2f);
        int x = S(10);
        if (!string.IsNullOrEmpty(Glyph))
        {
            Gfx.Glyph(g, Glyph!, Fonts.Icon(8.5f), Fore, new Rectangle(x, 0, S(12), Height));
            x += S(12) + S(5);
        }
        Gfx.Text(g, Text, Font, Fore, new Rectangle(x, 0, Width - x - S(6), Height), Gfx.Left);
    }
}

/// <summary>Thin rounded progress bar; supports stacked segments.</summary>
public sealed class ProgressBarEx : UiControl
{
    private float _value;
    public Color Bar { get; set; } = Theme.P.Accent;
    public Color Track { get; set; } = Theme.P.Surface3;
    public List<(float Value, Color Color)>? Segments { get; set; }

    public ProgressBarEx(float value = 0, int height = 6)
    {
        _value = value;
        Height = S(height);
    }

    public float Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0f, 1f); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var r = new RectangleF(0, 0, Width, Height);
        Gfx.FillRound(g, Track, r, Height / 2f);
        using var clip = Gfx.Round(r, Height / 2f);
        g.SetClip(clip);
        if (Segments is { Count: > 0 })
        {
            float x = 0;
            foreach (var (v, c) in Segments)
            {
                float w = Width * Math.Clamp(v, 0, 1);
                using var b = new SolidBrush(c);
                g.FillRectangle(b, x, 0, w, Height);
                x += w;
            }
        }
        else if (_value > 0)
        {
            Gfx.FillRound(g, Bar, new RectangleF(0, 0, Math.Max(Height, Width * _value), Height), Height / 2f);
        }
        g.ResetClip();
    }
}

/// <summary>Circular progress with a value in the middle (scores, daily goal).</summary>
public sealed class Ring : UiControl
{
    private float _value;
    public string Center { get; set; } = "";
    public string Caption { get; set; } = "";
    public Color Color { get; set; } = Theme.P.Accent;
    public float Thickness { get; set; } = 10f;
    public float CenterPt { get; set; } = 20f;

    public Ring(int size = 140)
    {
        Size = new Size(S(size), S(size));
    }

    public float Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0f, 1f); Invalidate(); }
    }

    public void AnimateTo(float value)
    {
        float target = Math.Clamp(value, 0f, 1f);
        Animator.Tween(this, 700, t => Value = target * t);
    }

    public override Size GetPreferredSize(Size proposedSize) => Size;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        float th = Sf(Thickness);
        var r = new RectangleF(th / 2 + 1, th / 2 + 1, Width - th - 2, Height - th - 2);
        using (var track = new Pen(Theme.P.Surface3, th)) g.DrawEllipse(track, r);
        if (_value > 0)
        {
            using var pen = new Pen(Color, th) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            g.DrawArc(pen, r, -90, Math.Max(1f, 360f * _value));
        }
        var big = Fonts.Heading(CenterPt);
        int capH = string.IsNullOrEmpty(Caption) ? 0 : Fonts.Ui(9f).Height;
        int top = (Height - big.Height - capH) / 2;
        Gfx.Text(g, Center, big, Theme.P.Text, new Rectangle(0, top, Width, big.Height), Gfx.Center);
        if (capH > 0) Gfx.Text(g, Caption, Fonts.Ui(9f), Theme.P.Text2, new Rectangle(0, top + big.Height, Width, capH), Gfx.Center);
    }
}

/// <summary>Dashboard number tile: icon, big value, label and a hint line.</summary>
public sealed class StatTile : UiControl, IHeightForWidth
{
    public string Glyph { get; set; }
    public string Value { get; set; }
    public string Label { get; set; }
    public string Hint { get; set; }
    public Color Tint { get; set; }

    public StatTile(string glyph, string value, string label, string hint = "", Color? tint = null)
    {
        Glyph = glyph;
        Value = value;
        Label = label;
        Hint = hint;
        Tint = tint ?? Theme.P.Accent;
        Height = S(112);
    }

    public int HeightFor(int width) => S(112);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.P.Bg);
        Gfx.Smooth(g);
        var p = Theme.P;
        var rect = new RectangleF(0, 0, Width, Height - Sf(1));
        Gfx.FillRound(g, Theme.IsDark ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(14, 20, 30, 60), new RectangleF(0, Sf(1), Width, Height - Sf(1)), S(12));
        Gfx.FillRound(g, p.Surface, rect, S(12));
        Gfx.DrawRound(g, p.Border, rect, S(12));

        int pad = S(18);
        var circle = new RectangleF(pad, pad, S(36), S(36));
        Gfx.Circle(g, Gfx.Blend(p.Surface, Tint, p.Dark ? 0.25f : 0.14f), circle);
        Gfx.Glyph(g, Glyph, Fonts.Icon(12f), Tint, Rectangle.Round(circle));

        var valueFont = Fonts.Heading(20f);
        int x = pad + S(36) + S(14);
        Gfx.Text(g, Value, valueFont, p.Text, new Rectangle(x, pad - S(4), Width - x - pad, valueFont.Height), Gfx.Left);
        Gfx.Text(g, Label, Fonts.UiSemibold(9.5f), p.Text2, new Rectangle(x, pad - S(4) + valueFont.Height, Width - x - pad, Fonts.Ui(9.5f).Height + S(2)), Gfx.Left);
        if (!string.IsNullOrEmpty(Hint))
            Gfx.Text(g, Hint, Fonts.Ui(8.5f), p.Text3, new Rectangle(pad, Height - pad - Fonts.Ui(8.5f).Height + S(2), Width - pad * 2, Fonts.Ui(8.5f).Height + S(2)), Gfx.Left);
    }
}

/// <summary>Friendly empty state: big icon, title, explanation and actions.</summary>
public sealed class EmptyState : Column
{
    public EmptyState(string glyph, string title, string text, params ButtonEx[] actions)
    {
        Gap = S(10);
        Padding = Pad(24, 32, 24, 32);
        Controls.Add(new IconBadge(glyph, 64, Theme.P.Accent).Align(HAlign.Center));
        var t = Ui.Heading(title, 14f);
        Controls.Add(new CenteredText(title, Fonts.Heading(14f), Theme.P.Text));
        Controls.Add(new CenteredText(text, Fonts.Ui(10f), Theme.P.Text2));
        if (actions.Length > 0)
        {
            var row = new Row { Gap = S(10), Margin = new Padding(0, S(8), 0, 0) };
            foreach (var a in actions) row.Controls.Add(a);
            row.Align(HAlign.Center);
            Controls.Add(row);
        }
        t.Dispose();
    }
}

/// <summary>Wrapped, horizontally centred text.</summary>
public sealed class CenteredText : UiControl, IHeightForWidth
{
    public CenteredText(string text, Font font, Color color)
    {
        Text = text;
        Font = font;
        ForeColor = color;
    }

    public int HeightFor(int width) => Gfx.WrappedHeight(Text, Font, Math.Min(width, S(560)));

    protected override void OnPaint(PaintEventArgs e)
    {
        int w = Math.Min(Width, S(560));
        Gfx.Text(e.Graphics, Text, Font, ForeColor, new Rectangle((Width - w) / 2, 0, w, Height), Gfx.Wrap | TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>Icon inside a soft circle.</summary>
public sealed class IconBadge : UiControl
{
    private readonly string _glyph;
    private readonly Color _color;

    public IconBadge(string glyph, int size, Color color)
    {
        _glyph = glyph;
        _color = color;
        Size = new Size(S(size), S(size));
    }

    public override Size GetPreferredSize(Size proposedSize) => Size;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        Gfx.Circle(g, Gfx.Blend(BackColor, _color, Theme.IsDark ? 0.24f : 0.13f), new RectangleF(0, 0, Width - 1, Height - 1));
        Gfx.Glyph(g, _glyph, Fonts.Get(Fonts.Icons, Width / Dpi.Scale * 0.30f), _color, ClientRectangle);
    }
}

public enum Tone { Info, Success, Warning, Danger, Accent, Neutral }

/// <summary>Coloured message box inside a page (tips, warnings, import results).</summary>
public sealed class Banner : LayoutPanel
{
    private readonly Tone _tone;
    private readonly string _glyph;
    private readonly Column _text;
    private readonly Row? _actions;

    public Banner(Tone tone, string title, string? text = null, params Control[] actions)
    {
        _tone = tone;
        _glyph = tone switch
        {
            Tone.Success => Icons.CheckCircle,
            Tone.Warning => Icons.Warning,
            Tone.Danger => Icons.ErrorCircle,
            Tone.Accent => Icons.Lightbulb,
            _ => Icons.Info,
        };
        Padding = Pad(16, 14, 16, 14);
        BackColor = Fill;
        _text = new Column { Gap = S(4) };
        if (!string.IsNullOrEmpty(title)) _text.Controls.Add(new TextLabel(title, Fonts.UiSemibold(10f), Theme.P.Text));
        if (!string.IsNullOrEmpty(text)) _text.Controls.Add(new TextLabel(text, Fonts.Ui(9.5f), Theme.P.Text2));
        Controls.Add(_text);
        if (actions.Length > 0)
        {
            _actions = new Row { Gap = S(8) };
            foreach (var a in actions) _actions.Controls.Add(a);
            Controls.Add(_actions);
        }
    }

    private Color ToneColor => _tone switch
    {
        Tone.Success => Theme.P.Success,
        Tone.Warning => Theme.P.Warning,
        Tone.Danger => Theme.P.Danger,
        Tone.Accent => Theme.P.Accent,
        Tone.Neutral => Theme.P.Text2,
        _ => Theme.P.Info,
    };

    private Color Fill => Gfx.Blend(Theme.P.Surface, ToneColor, Theme.IsDark ? 0.16f : 0.08f);

    private int IconSpace => S(30);

    public override int HeightFor(int width)
    {
        if (_text == null) return Padding.Vertical;
        int textW = width - Padding.Horizontal - IconSpace - ActionsWidth();
        int h = LayoutProps.HeightOf(_text, Math.Max(S(80), textW));
        if (_actions != null) h = Math.Max(h, _actions.HeightFor(ActionsWidth()));
        return h + Padding.Vertical;
    }

    private int ActionsWidth() => _actions == null ? 0 : _actions.GetPreferredSize(Size.Empty).Width + S(12);

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_text == null) return;
        int aw = ActionsWidth();
        int textW = Math.Max(S(80), Width - Padding.Horizontal - IconSpace - aw);
        int th = LayoutProps.HeightOf(_text, textW);
        Place(_text, Padding.Left + IconSpace, Padding.Top, textW, th);
        if (_actions != null)
        {
            int ah = _actions.HeightFor(aw);
            Place(_actions, Width - Padding.Right - aw + S(12), (Height - ah) / 2, aw - S(12), ah);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.P.Bg);
        Gfx.Smooth(g);
        var rect = new RectangleF(0, 0, Width, Height);
        Gfx.FillRound(g, Fill, rect, S(10));
        Gfx.DrawRound(g, Gfx.Blend(Theme.P.Surface, ToneColor, Theme.IsDark ? 0.35f : 0.25f), rect, S(10));
        Gfx.Glyph(g, _glyph, Fonts.Icon(12f), ToneColor, new Rectangle(Padding.Left - S(2), Padding.Top - S(1), S(22), S(22)));
    }
}

/// <summary>Horizontal 1px line.</summary>
public sealed class Divider : Control
{
    public Divider()
    {
        Height = Math.Max(1, S(1));
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.P.Border);
}

/// <summary>A section heading with optional subtitle and actions on the right.</summary>
public sealed class SectionHeader : Row
{
    public SectionHeader(string title, string? subtitle = null, params Control[] actions)
    {
        Gap = S(8);
        var col = new Column { Gap = S(2) }.Flex();
        col.Controls.Add(new TextLabel(title, Fonts.Heading(12.5f), Theme.P.Text, wrap: false));
        if (!string.IsNullOrEmpty(subtitle)) col.Controls.Add(new TextLabel(subtitle, Fonts.Ui(9.5f), Theme.P.Text2));
        Controls.Add(col);
        foreach (var a in actions) Controls.Add(a);
    }
}

public static class ControlExtensions
{
    /// <summary>Configure a control inline: <c>new Row().Also(r => r.Wrap = true)</c>.</summary>
    public static T Also<T>(this T control, Action<T> configure) where T : Control
    {
        configure(control);
        return control;
    }
}