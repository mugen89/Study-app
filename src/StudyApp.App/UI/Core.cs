using System.Runtime.CompilerServices;

namespace StudyApp.App.UI;

/// <summary>One shared 60 fps timer drives every animation in the app.</summary>
public static class Animator
{
    private static readonly System.Windows.Forms.Timer Timer = new() { Interval = 15 };
    private static readonly List<Func<float, bool>> Active = new();
    private static long _last;

    static Animator()
    {
        Timer.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            float dt = Math.Clamp(now - _last, 1, 100);
            _last = now;
            foreach (var step in Active.ToArray())
            {
                bool done;
                try { done = step(dt); }
                catch { done = true; }
                if (done) Active.Remove(step);
            }
            if (Active.Count == 0) Timer.Stop();
        };
    }

    /// <summary>Runs <paramref name="step"/> every frame (argument: elapsed ms) until it returns true.</summary>
    public static void Run(Func<float, bool> step)
    {
        Active.Add(step);
        if (!Timer.Enabled)
        {
            _last = Environment.TickCount64;
            Timer.Start();
        }
    }

    public static void Stop(Func<float, bool> step) => Active.Remove(step);

    /// <summary>Animates 0→1 over <paramref name="durationMs"/> with ease-out; calls back with the eased value.</summary>
    public static void Tween(Control owner, float durationMs, Action<float> onStep, Action? onDone = null)
    {
        if (!Theme.Animations || durationMs <= 0)
        {
            onStep(1f);
            onDone?.Invoke();
            return;
        }
        float t = 0;
        Run(dt =>
        {
            if (owner.IsDisposed) return true;
            t = Math.Min(1f, t + dt / durationMs);
            onStep(EaseOut(t));
            if (t >= 1f)
            {
                onDone?.Invoke();
                return true;
            }
            return false;
        });
    }

    public static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);
}

/// <summary>A value that glides towards a target (hover highlights, toggles…).</summary>
public sealed class Anim
{
    private readonly Control _owner;
    private readonly float _speed;
    private float _target;
    private bool _running;

    public float Value { get; private set; }

    public Anim(Control owner, float durationMs = 140f)
    {
        _owner = owner;
        _speed = 1f / durationMs;
    }

    public void To(float target)
    {
        _target = target;
        if (!Theme.Animations || !_owner.IsHandleCreated)
        {
            Value = target;
            _owner.Invalidate();
            return;
        }
        if (_running) return;
        _running = true;
        Animator.Run(dt =>
        {
            if (_owner.IsDisposed) { _running = false; return true; }
            float step = dt * _speed;
            Value = Value < _target ? Math.Min(_target, Value + step) : Math.Max(_target, Value - step);
            _owner.Invalidate();
            bool done = Math.Abs(Value - _target) < 0.001f;
            if (done) _running = false;
            return done;
        });
    }

    public void Set(float value)
    {
        _target = Value = value;
        _owner.Invalidate();
    }
}

/// <summary>Base class of all custom-drawn controls: double buffered, hover/press tracking.</summary>
public class UiControl : Control
{
    protected readonly Anim HoverAnim;
    protected bool IsHover { get; private set; }
    protected bool IsDown { get; private set; }

    public UiControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        HoverAnim = new Anim(this);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        IsHover = true;
        HoverAnim.To(1f);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        IsHover = false;
        IsDown = false;
        HoverAnim.To(0f);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            IsDown = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        IsDown = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackColor);

    /// <summary>True when <see cref="Control.GetPreferredSize"/> measures the content (text width etc.).</summary>
    public virtual bool SizesToContent => false;

    /// <summary>The colour behind this control (first opaque ancestor background).</summary>
    protected Color ParentBack()
    {
        for (var p = Parent; p != null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return Theme.P.Bg;
    }
}

// ───────────────────────────────────────────────────────────── layout

/// <summary>Controls whose height depends on the width they get (wrapping text, stacks…).</summary>
public interface IHeightForWidth
{
    int HeightFor(int width);
}

public enum HAlign { Stretch, Left, Center, Right }

/// <summary>Attached layout properties.</summary>
public static class LayoutProps
{
    private sealed class Info
    {
        public HAlign Align = HAlign.Stretch;
        public float Flex;
        public int? InitialWidth;
        public bool Fixed;
    }

    private static readonly ConditionalWeakTable<Control, Info> Table = new();

    public static T Align<T>(this T c, HAlign align) where T : Control { Table.GetOrCreateValue(c).Align = align; return c; }
    public static T Flex<T>(this T c, float flex = 1f) where T : Control { Table.GetOrCreateValue(c).Flex = flex; return c; }
    public static HAlign GetAlign(Control c) => Table.TryGetValue(c, out var i) ? i.Align : HAlign.Stretch;
    public static float GetFlex(Control c) => Table.TryGetValue(c, out var i) ? i.Flex : 0f;

    public static int HeightOf(Control? c, int width) => c == null ? 0 : c is IHeightForWidth h ? h.HeightFor(Math.Max(1, width)) : c.Height;

    /// <summary>
    /// Width a control wants in a row / aligned column. Content-sized controls (buttons, labels, chips…)
    /// measure themselves; fixed-size ones keep the width they were created with, so a temporary narrow
    /// layout pass can never shrink them for good.
    /// </summary>
    public static int PreferredWidth(Control c)
    {
        var info = Table.GetOrCreateValue(c);
        if (info.Fixed && info.InitialWidth.HasValue) return info.InitialWidth.Value;
        if (c is LayoutPanel || c is UiControl { SizesToContent: true }) return c.GetPreferredSize(Size.Empty).Width;
        info.InitialWidth ??= c.Width;
        return info.InitialWidth.Value;
    }

    /// <summary>Changes the width a fixed-size control asks for.</summary>
    public static void SetPreferredWidth(Control c, int width)
    {
        var info = Table.GetOrCreateValue(c);
        info.InitialWidth = width;
        info.Fixed = true;
        c.Width = width;
    }

    /// <summary>Gives any control (even a stack) a fixed width inside rows.</summary>
    public static T FixedWidth<T>(this T c, int width) where T : Control
    {
        SetPreferredWidth(c, width);
        return c;
    }

    /// <summary>Re-runs layout from the nearest scroll container or page so size changes propagate.</summary>
    public static void Relayout(Control c)
    {
        Control? target = null;
        for (var p = c.Parent; p != null; p = p.Parent)
        {
            if (p is ScrollHost || p is Form) { target = p; break; }
            target = p;
        }
        target?.PerformLayout();
    }
}

/// <summary>Base for the stack/row/grid panels.</summary>
public abstract class LayoutPanel : Panel, IHeightForWidth
{
    protected LayoutPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public abstract int HeightFor(int width);

    protected IEnumerable<Control> VisibleChildren()
    {
        foreach (Control c in Controls)
            if (c.Visible) yield return c;
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        e.Control!.VisibleChanged += ChildVisibleChanged;
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        e.Control!.VisibleChanged -= ChildVisibleChanged;
        base.OnControlRemoved(e);
    }

    private void ChildVisibleChanged(object? sender, EventArgs e) => LayoutProps.Relayout(this);

    /// <summary>Sets bounds and makes sure nested panels re-layout even when their size did not change.</summary>
    protected static void Place(Control c, int x, int y, int w, int h)
    {
        bool same = c.Width == w && c.Height == h;
        c.SetBounds(x, y, w, h);
        if (same && (c is LayoutPanel || c is ScrollHost)) c.PerformLayout();
    }

    public void AddRange(params Control?[] controls)
    {
        SuspendLayout();
        foreach (var c in controls)
            if (c != null) Controls.Add(c);
        ResumeLayout(true);
    }
}

/// <summary>Vertical stack: children get the full width (unless aligned) and their natural height.</summary>
public class Column : LayoutPanel
{
    public int Gap { get; set; } = Dpi.S(12);

    public override int HeightFor(int width)
    {
        int inner = width - Padding.Horizontal;
        int h = Padding.Vertical;
        bool first = true;
        foreach (var c in VisibleChildren())
        {
            if (!first) h += Gap;
            first = false;
            h += c.Margin.Vertical + ChildHeight(c, inner);
        }
        return h;
    }

    private static int ChildHeight(Control c, int inner)
    {
        var align = LayoutProps.GetAlign(c);
        int w = inner - c.Margin.Horizontal;
        if (align != HAlign.Stretch) w = Math.Min(w, LayoutProps.PreferredWidth(c));
        return LayoutProps.HeightOf(c, w);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int inner = ClientSize.Width - Padding.Horizontal;
        int y = Padding.Top;
        bool first = true;
        foreach (var c in VisibleChildren())
        {
            if (!first) y += Gap;
            first = false;
            var align = LayoutProps.GetAlign(c);
            int avail = inner - c.Margin.Horizontal;
            int w = align == HAlign.Stretch ? avail : Math.Min(avail, LayoutProps.PreferredWidth(c));
            int x = Padding.Left + c.Margin.Left + align switch
            {
                HAlign.Center => (avail - w) / 2,
                HAlign.Right => avail - w,
                _ => 0,
            };
            int h = LayoutProps.HeightOf(c, w);
            Place(c, x, y + c.Margin.Top, Math.Max(1, w), Math.Max(0, h));
            y += c.Margin.Vertical + h;
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int w = 0;
        foreach (var c in VisibleChildren()) w = Math.Max(w, LayoutProps.PreferredWidth(c) + c.Margin.Horizontal);
        w += Padding.Horizontal;
        return new Size(w, HeightFor(w));
    }
}

public enum VAlign { Top, Center, Bottom }

/// <summary>Horizontal row: children keep their preferred width; flex children share the rest.</summary>
public class Row : LayoutPanel
{
    public int Gap { get; set; } = Dpi.S(8);
    public VAlign Align { get; set; } = VAlign.Center;
    public bool Wrap { get; set; }

    private List<(Control C, int W)> Measure(int inner)
    {
        var list = VisibleChildren().ToList();
        var widths = new List<(Control, int)>();
        int fixedTotal = 0;
        float flexTotal = 0;
        foreach (var c in list)
        {
            float flex = LayoutProps.GetFlex(c);
            if (flex > 0) flexTotal += flex;
            else fixedTotal += LayoutProps.PreferredWidth(c) + c.Margin.Horizontal;
        }
        int gaps = Math.Max(0, list.Count - 1) * Gap;
        int remaining = Math.Max(0, inner - fixedTotal - gaps);
        foreach (var c in list)
        {
            float flex = LayoutProps.GetFlex(c);
            int w = flex > 0 ? (int)(remaining * flex / flexTotal) - c.Margin.Horizontal : LayoutProps.PreferredWidth(c);
            widths.Add((c, Math.Max(0, w)));
        }
        return widths;
    }

    private List<List<(Control C, int W)>> Lines(int inner)
    {
        var items = Measure(inner);
        var lines = new List<List<(Control, int)>>();
        if (!Wrap)
        {
            lines.Add(items);
            return lines;
        }
        var line = new List<(Control, int)>();
        int x = 0;
        foreach (var it in items)
        {
            int w = Math.Min(it.W, inner);
            if (line.Count > 0 && x + Gap + w + it.C.Margin.Horizontal > inner)
            {
                lines.Add(line);
                line = new List<(Control, int)>();
                x = 0;
            }
            x += (line.Count > 0 ? Gap : 0) + w + it.C.Margin.Horizontal;
            line.Add((it.C, w));
        }
        if (line.Count > 0) lines.Add(line);
        return lines;
    }

    public override int HeightFor(int width)
    {
        int inner = width - Padding.Horizontal;
        int h = 0;
        var lines = Lines(inner);
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) h += Gap;
            h += lines[i].Count == 0 ? 0 : lines[i].Max(t => LayoutProps.HeightOf(t.C, t.W) + t.C.Margin.Vertical);
        }
        return h + Padding.Vertical;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int inner = ClientSize.Width - Padding.Horizontal;
        int y = Padding.Top;
        foreach (var line in Lines(inner))
        {
            int lineH = line.Count == 0 ? 0 : line.Max(t => LayoutProps.HeightOf(t.C, t.W) + t.C.Margin.Vertical);
            int x = Padding.Left;
            foreach (var (c, w) in line)
            {
                int h = LayoutProps.HeightOf(c, w);
                int top = Align switch
                {
                    VAlign.Top => y + c.Margin.Top,
                    VAlign.Bottom => y + lineH - h - c.Margin.Bottom,
                    _ => y + (lineH - h) / 2,
                };
                Place(c, x + c.Margin.Left, top, w, h);
                x += c.Margin.Horizontal + w + Gap;
            }
            y += lineH + Gap;
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var list = VisibleChildren().ToList();
        int w = list.Sum(c => LayoutProps.PreferredWidth(c) + c.Margin.Horizontal) + Math.Max(0, list.Count - 1) * Gap + Padding.Horizontal;
        int h = list.Count == 0 ? 0 : list.Max(c => LayoutProps.HeightOf(c, LayoutProps.PreferredWidth(c)) + c.Margin.Vertical);
        return new Size(w, h + Padding.Vertical);
    }
}

/// <summary>Flexible empty space inside a <see cref="Row"/> (pushes following children to the right).</summary>
public sealed class Spacer : Control
{
    public Spacer()
    {
        this.Flex();
        Height = 1;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Parent?.BackColor ?? Theme.P.Bg);
}

/// <summary>Responsive grid of equally sized cells (subject cards, tiles).</summary>
public class CardGrid : LayoutPanel
{
    public int MinCellWidth { get; set; } = Dpi.S(260);
    public int Gap { get; set; } = Dpi.S(16);
    public int MaxColumns { get; set; } = 6;

    private int Columns(int inner) => Math.Clamp((inner + Gap) / (MinCellWidth + Gap), 1, MaxColumns);

    public override int HeightFor(int width)
    {
        int inner = width - Padding.Horizontal;
        var list = VisibleChildren().ToList();
        if (list.Count == 0) return Padding.Vertical;
        int cols = Columns(inner);
        int cellW = (inner - Gap * (cols - 1)) / cols;
        int h = 0;
        for (int i = 0; i < list.Count; i += cols)
        {
            if (i > 0) h += Gap;
            h += list.Skip(i).Take(cols).Max(c => LayoutProps.HeightOf(c, cellW));
        }
        return h + Padding.Vertical;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int inner = ClientSize.Width - Padding.Horizontal;
        var list = VisibleChildren().ToList();
        if (list.Count == 0) return;
        int cols = Columns(inner);
        int cellW = (inner - Gap * (cols - 1)) / cols;
        int y = Padding.Top;
        for (int i = 0; i < list.Count; i += cols)
        {
            var row = list.Skip(i).Take(cols).ToList();
            int rowH = row.Max(c => LayoutProps.HeightOf(c, cellW));
            for (int k = 0; k < row.Count; k++)
                Place(row[k], Padding.Left + k * (cellW + Gap), y, cellW, rowH);
            y += rowH + Gap;
        }
    }
}

// ───────────────────────────────────────────────────────────── scrolling

public interface IWheelTarget
{
    /// <summary>Returns false when it cannot scroll further, so an outer container may scroll instead.</summary>
    bool HandleWheel(int delta);
}

/// <summary>Routes the mouse wheel to the scroll area under the cursor (not the focused control).</summary>
public sealed class WheelRouter : IMessageFilter
{
    private const int WM_MOUSEWHEEL = 0x020A;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;
        var lp = m.LParam.ToInt64();
        var pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
        var hwnd = Native.WindowFromPoint(pt);
        var control = Control.FromChildHandle(hwnd);
        if (control == null) return false;
        if (control is TextBox { Multiline: true, ScrollBars: not ScrollBars.None } || control is RichTextBox) return false;
        if (control is ListBox or ComboBox or DataGridView) return false;

        int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
        for (var c = control; c != null; c = c.Parent)
            if (c is IWheelTarget target && target.HandleWheel(delta)) return true;
        return false;
    }
}

/// <summary>Vertical scroll container with a slim themed scrollbar and smooth wheel scrolling.</summary>
public sealed class ScrollHost : UiControl, IWheelTarget
{
    private readonly ScrollThumb _bar;
    private int _offset;
    private float _target;
    private bool _animating;

    public Control Content { get; }
    /// <summary>Maximum width of the content in pixels (0 = fill). Content is centred.</summary>
    public int MaxContentWidth { get; set; }
    public int ContentHeight { get; private set; }
    public int Offset => _offset;
    public int MaxOffset => Math.Max(0, ContentHeight - ClientSize.Height);
    public event EventHandler? Scrolled;

    public ScrollHost(Control content)
    {
        Content = content;
        Controls.Add(content);
        _bar = new ScrollThumb(this) { Visible = false };
        Controls.Add(_bar);
        _bar.BringToFront();
        TabStop = false;
    }

    private int BarWidth => Dpi.S(12);

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (ClientSize.Width <= 0) return;
        int avail = ClientSize.Width - BarWidth;
        int w = MaxContentWidth > 0 ? Math.Min(avail, MaxContentWidth) : avail;
        int h = LayoutProps.HeightOf(Content, w);
        ContentHeight = h;
        _offset = Math.Clamp(_offset, 0, MaxOffset);
        _target = Math.Clamp(_target, 0, MaxOffset);
        int x = Math.Max(0, (avail - w) / 2);
        bool same = Content.Width == w && Content.Height == Math.Max(h, 1);
        Content.SetBounds(x, -_offset, w, Math.Max(h, 1));
        if (same) Content.PerformLayout();
        _bar.SetBounds(ClientSize.Width - BarWidth, 0, BarWidth, ClientSize.Height);
        _bar.Visible = MaxOffset > 0;
        _bar.Invalidate();
    }

    public bool HandleWheel(int delta)
    {
        if (MaxOffset <= 0) return false;
        if (delta > 0 && _target <= 0) return false;
        if (delta < 0 && _target >= MaxOffset) return false;
        ScrollTo((int)(_target - delta / 120f * Dpi.S(110)));
        return true;
    }

    public void ScrollTo(int y, bool animate = true)
    {
        _target = Math.Clamp(y, 0, MaxOffset);
        if (!animate || !Theme.Animations)
        {
            SetOffset((int)_target);
            return;
        }
        if (_animating) return;
        _animating = true;
        Animator.Run(dt =>
        {
            if (IsDisposed) return true;
            float diff = _target - _offset;
            if (Math.Abs(diff) < 1.5f)
            {
                SetOffset((int)_target);
                _animating = false;
                return true;
            }
            float k = 1f - (float)Math.Pow(0.78, dt / 16.0);
            SetOffset(_offset + (int)Math.Round(diff * k == 0 ? Math.Sign(diff) : diff * k));
            return false;
        });
    }

    public void ScrollBy(int delta) => ScrollTo((int)_target + delta);

    private void SetOffset(int value)
    {
        value = Math.Clamp(value, 0, MaxOffset);
        if (value == _offset) return;
        _offset = value;
        Content.Top = -_offset;
        _bar.Invalidate();
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Scrolls so that <paramref name="c"/> (a descendant of the content) is visible.</summary>
    public void EnsureVisible(Control c, int margin = 16)
    {
        var pt = Content.PointToClient(c.PointToScreen(Point.Empty));
        int top = pt.Y - Dpi.S(margin);
        int bottom = pt.Y + c.Height + Dpi.S(margin);
        if (top < _offset) ScrollTo(top);
        else if (bottom > _offset + ClientSize.Height) ScrollTo(bottom - ClientSize.Height);
    }

    public void ScrollToTop() => ScrollTo(0, false);

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackColor);

    private sealed class ScrollThumb : UiControl
    {
        private readonly ScrollHost _host;
        private int _dragStartY = -1;
        private int _dragStartOffset;

        public ScrollThumb(ScrollHost host)
        {
            _host = host;
            Cursor = Cursors.Default;
        }

        private Rectangle ThumbRect()
        {
            int viewport = _host.ClientSize.Height;
            int content = Math.Max(1, _host.ContentHeight);
            int h = Math.Max(Dpi.S(36), (int)((double)viewport / content * Height));
            int max = Math.Max(1, _host.MaxOffset);
            int y = (int)((double)_host.Offset / max * (Height - h));
            return new Rectangle(0, y, Width, h);
        }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(_host.BackColor);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Smooth(g);
            var r = ThumbRect();
            float hover = Math.Max(HoverAnim.Value, _dragStartY >= 0 ? 1f : 0f);
            float w = Dpi.Sf(4) + Dpi.Sf(3) * hover;
            var thumb = new RectangleF(Width - w - Dpi.Sf(3), r.Y + Dpi.Sf(2), w, r.Height - Dpi.Sf(4));
            var color = Gfx.Blend(Theme.P.BorderStrong, Theme.P.Text3, hover);
            Gfx.FillRound(g, color, thumb, w / 2);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var r = ThumbRect();
            if (r.Contains(e.Location))
            {
                _dragStartY = e.Y;
                _dragStartOffset = _host.Offset;
                Capture = true;
            }
            else
            {
                _host.ScrollBy(e.Y < r.Y ? -_host.ClientSize.Height + Dpi.S(40) : _host.ClientSize.Height - Dpi.S(40));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragStartY < 0) return;
            var r = ThumbRect();
            double track = Math.Max(1, Height - r.Height);
            int offset = _dragStartOffset + (int)((e.Y - _dragStartY) / track * _host.MaxOffset);
            _host.ScrollTo(offset, animate: false);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragStartY = -1;
            Capture = false;
            Invalidate();
        }
    }
}
