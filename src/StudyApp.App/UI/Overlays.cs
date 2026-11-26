namespace StudyApp.App.UI;

/// <summary>Themed tooltips. Shortcuts are shown next to the text, e.g. "Flip card  (Space)".</summary>
public static class Tips
{
    private static ToolTip? _tip;

    private static ToolTip Tip
    {
        get
        {
            if (_tip != null) return _tip;
            _tip = new ToolTip { OwnerDraw = true, InitialDelay = 450, ReshowDelay = 100, AutoPopDelay = 12000, UseAnimation = true, UseFading = true };
            _tip.Popup += (_, e) =>
            {
                var text = _tip.GetToolTip(e.AssociatedControl);
                var size = TextRenderer.MeasureText(text, Fonts.Ui(9f), new Size(S(360), int.MaxValue), Gfx.Wrap);
                e.ToolTipSize = new Size(size.Width + S(20), size.Height + S(12));
            };
            _tip.Draw += (_, e) =>
            {
                var p = Theme.P;
                var back = p.Dark ? p.Surface3 : Color.FromArgb(32, 35, 42);
                var fore = p.Dark ? p.Text : Color.White;
                using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var pen = new Pen(p.Dark ? p.BorderStrong : back)) e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, Fonts.Ui(9f), new Rectangle(S(10), S(6), e.Bounds.Width - S(20), e.Bounds.Height - S(12)), fore, Gfx.Wrap);
            };
            return _tip;
        }
    }

    public static void Set(Control c, string text, string? shortcut = null)
    {
        var full = string.IsNullOrEmpty(shortcut) ? text : $"{text}   ({shortcut})";
        Tip.SetToolTip(c, full);
    }

    public static void Reset()
    {
        _tip?.Dispose();
        _tip = null;
    }
}

/// <summary>Context menus and dropdowns with the app theme.</summary>
public static class Menus
{
    public static ContextMenuStrip Create()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new ThemedRenderer(),
            ShowImageMargin = true,
            ShowCheckMargin = false,
            Font = Fonts.Ui(10f),
            Padding = new Padding(S(4), S(6), S(4), S(6)),
            BackColor = Theme.P.Surface,
            ForeColor = Theme.P.Text,
            DropShadowEnabled = true,
        };
        menu.HandleCreated += (_, _) => Native.RoundCorners(menu.Handle);
        menu.Closed += (_, _) => BeginDispose(menu);
        return menu;
    }

    private static void BeginDispose(ContextMenuStrip menu)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (!menu.Visible) menu.Dispose();
        };
        timer.Start();
    }

    public static ToolStripMenuItem Item(string text, string? glyph, Action onClick, string? shortcut = null, bool danger = false)
    {
        var item = new ToolStripMenuItem(text)
        {
            Tag = new MenuInfo(glyph, danger),
            ShortcutKeyDisplayString = shortcut,
            AutoSize = true,
            Padding = new Padding(S(6), S(7), S(28), S(7)),
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    public static ToolStripSeparator Separator() => new() { Margin = new Padding(0, S(3), 0, S(3)) };

    public static void Show(Control anchor, params ToolStripItem[] items)
    {
        var menu = Create();
        menu.Items.AddRange(items);
        int width = menu.PreferredSize.Width;
        foreach (ToolStripItem i in menu.Items) i.Width = Math.Max(i.Width, width);
        var pt = new Point(anchor.Width - width, anchor.Height + S(4));
        menu.Show(anchor, pt.X < 0 ? new Point(0, pt.Y) : pt);
    }

    public sealed record MenuInfo(string? Glyph, bool Danger);

    private sealed class ThemedRenderer : ToolStripProfessionalRenderer
    {
        public ThemedRenderer() : base(new Colors()) => RoundedEdges = false;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var b = new SolidBrush(Theme.P.Surface);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Theme.P.BorderStrong);
            e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Gfx.Smooth(e.Graphics);
            var r = new RectangleF(S(2), S(1), e.Item.Width - S(4), e.Item.Height - S(2));
            var danger = (e.Item.Tag as MenuInfo)?.Danger == true;
            Gfx.FillRound(e.Graphics, danger ? Theme.P.DangerSoft : Gfx.Blend(Theme.P.Surface, Theme.P.Text, Theme.IsDark ? 0.09f : 0.055f), r, S(6));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var info = e.Item.Tag as MenuInfo;
            var color = !e.Item.Enabled ? Theme.P.Text3 : info?.Danger == true ? Theme.P.Danger : Theme.P.Text;
            // The shortcut text is drawn together with the main text below.
            if (e.Item is ToolStripMenuItem mi && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString) && e.Text == mi.ShortcutKeyDisplayString && e.Text != mi.Text)
                return;
            var rect = new Rectangle(S(38), 0, e.Item.Width - S(46), e.Item.Height);
            TextRenderer.DrawText(e.Graphics, e.Text, Fonts.Ui(10f), rect, color, Gfx.Left);
            if (info?.Glyph is string glyph)
                Gfx.Glyph(e.Graphics, glyph, Fonts.Icon(10f), info.Danger ? Theme.P.Danger : Theme.P.Text2, new Rectangle(S(10), 0, S(20), e.Item.Height));
            if (e.Item is ToolStripMenuItem { Checked: true })
                Gfx.Glyph(e.Graphics, Icons.Check, Fonts.Icon(10f), Theme.P.Accent, new Rectangle(S(10), 0, S(20), e.Item.Height));
            if (e.Item is ToolStripMenuItem { ShortcutKeyDisplayString: { Length: > 0 } sc })
                TextRenderer.DrawText(e.Graphics, sc, Fonts.Ui(9f), new Rectangle(0, 0, e.Item.Width - S(12), e.Item.Height), Theme.P.Text3, Gfx.Left | TextFormatFlags.Right);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Theme.P.Border);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, S(8), y, e.Item.Width - S(8), y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.P.Text2;
            base.OnRenderArrow(e);
        }

        private sealed class Colors : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Theme.P.Surface2;
            public override Color MenuBorder => Theme.P.BorderStrong;
            public override Color ToolStripDropDownBackground => Theme.P.Surface;
            public override Color ImageMarginGradientBegin => Theme.P.Surface;
            public override Color ImageMarginGradientMiddle => Theme.P.Surface;
            public override Color ImageMarginGradientEnd => Theme.P.Surface;
        }
    }
}

/// <summary>Small notifications in the bottom-right corner, with an optional action (e.g. "Undo").</summary>
public sealed class Toast : UiControl
{
    private readonly Tone _tone;
    private readonly string? _actionText;
    private readonly Action? _action;
    private readonly System.Windows.Forms.Timer _timer;
    private Rectangle _actionRect;
    private Rectangle _closeRect;

    private static readonly List<Toast> Open = new();

    private Toast(string text, Tone tone, string? actionText, Action? action, int seconds)
    {
        Text = text;
        _tone = tone;
        _actionText = actionText;
        _action = action;
        Font = Fonts.Ui(10f);
        _timer = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
        _timer.Tick += (_, _) => Dismiss();
        var size = Measure();
        Size = size;
    }

    public static void Show(string text, Tone tone = Tone.Success, string? actionText = null, Action? action = null, int seconds = 4)
    {
        var form = Host.Main;
        if (form == null || form.IsDisposed) return;
        var toast = new Toast(text, tone, actionText, action, actionText != null ? Math.Max(seconds, 7) : seconds);
        form.Controls.Add(toast);
        toast.BringToFront();
        Open.Add(toast);
        Arrange();
        toast._timer.Start();
    }

    private Size Measure()
    {
        int textW = Math.Min(S(380), Gfx.Measure(Text, Font).Width);
        int actionW = _actionText != null ? Gfx.Measure(_actionText, Fonts.UiSemibold(10f)).Width + S(24) : 0;
        int h = Math.Max(S(48), Gfx.WrappedHeight(Text, Font, textW) + S(28));
        return new Size(S(16) + S(26) + textW + actionW + S(40), h);
    }

    private static void Arrange()
    {
        var form = Host.Main;
        if (form == null) return;
        int y = form.ClientSize.Height - S(24);
        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var t = Open[i];
            y -= t.Height;
            t.Location = new Point(form.ClientSize.Width - t.Width - S(24), y);
            y -= S(10);
        }
    }

    public static void Reposition() => Arrange();

    private void Dismiss()
    {
        _timer.Stop();
        _timer.Dispose();
        Open.Remove(this);
        Parent?.Controls.Remove(this);
        Dispose();
        Arrange();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = _actionRect.Contains(e.Location) || _closeRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_actionRect.Contains(e.Location))
        {
            _action?.Invoke();
            Dismiss();
        }
        else if (_closeRect.Contains(e.Location)) Dismiss();
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Parent?.BackColor ?? Theme.P.Bg);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Smooth(g);
        var p = Theme.P;
        var back = p.Dark ? p.Surface3 : Color.FromArgb(34, 37, 45);
        var fore = p.Dark ? p.Text : Color.White;
        var rect = new RectangleF(0, 0, Width, Height);
        Gfx.FillRound(g, back, rect, S(10));
        var color = _tone switch
        {
            Tone.Danger => p.Danger,
            Tone.Warning => p.Warning,
            Tone.Info => p.Info,
            Tone.Accent => p.Accent,
            _ => p.Success,
        };
        var glyph = _tone switch
        {
            Tone.Danger => Icons.ErrorCircle,
            Tone.Warning => Icons.Warning,
            Tone.Info or Tone.Accent => Icons.Info,
            _ => Icons.CheckCircle,
        };
        Gfx.Glyph(g, glyph, Fonts.Icon(12f), Gfx.Lighten(color, p.Dark ? 0.1f : 0.25f), new Rectangle(S(14), 0, S(22), Height));
        int x = S(16) + S(26);
        int actionW = _actionText != null ? Gfx.Measure(_actionText, Fonts.UiSemibold(10f)).Width + S(24) : 0;
        Gfx.Text(g, Text, Font, fore, new Rectangle(x, S(14), Width - x - actionW - S(40), Height - S(28)), Gfx.Wrap);
        var mouse = PointToClient(Cursor.Position);
        if (_actionText != null)
        {
            _actionRect = new Rectangle(Width - actionW - S(36), S(8), actionW, Height - S(16));
            if (_actionRect.Contains(mouse)) Gfx.FillRound(g, Color.FromArgb(40, 255, 255, 255), _actionRect, S(6));
            Gfx.Text(g, _actionText, Fonts.UiSemibold(10f), Gfx.Lighten(p.Accent, 0.35f), _actionRect, Gfx.Center);
        }
        _closeRect = new Rectangle(Width - S(34), 0, S(28), Height);
        Gfx.Glyph(g, Icons.Close, Fonts.Icon(8f), _closeRect.Contains(mouse) ? fore : Gfx.Blend(fore, back, 0.4f), _closeRect);
    }
}
