using StudyApp.App.Dialogs;
using StudyApp.App.Pages;
using StudyApp.App.Updates;
using StudyApp.Domain.Settings;

namespace StudyApp.App.Shell;

/// <summary>Left navigation: custom drawn for smooth hover, badges and a collapsible mode.</summary>
public sealed class Sidebar : UiControl
{
    private sealed record Entry(string Key, string Glyph, string Text, string? Shortcut, Func<PageBase> Create);

    private readonly List<Entry> _top;
    private readonly List<Entry> _bottom;
    private readonly Dictionary<string, Anim> _hoverAnims = new();
    private string _selected = "";
    private string? _hover;
    private int _dueCount;
    private int _trashCount;
    private bool _collapsed;

    public Sidebar()
    {
        _collapsed = Host.Settings.SidebarCollapsed;
        BackColor = Theme.P.Sidebar;
        Width = _collapsed ? S(72) : S(240);
        _top = new()
        {
            new("home", Icons.Home, T("Home"), "Ctrl+1", () => new HomePage()),
            new("subjects", Icons.Library, T("Subjects"), "Ctrl+2", () => new SubjectsPage()),
            new("review", Icons.Review, T("Review"), "Ctrl+3", () => new ReviewPage()),
            new("search", Icons.Search, T("Search"), "Ctrl+K", () => new SearchPage()),
            new("import", Icons.Import, T("Import"), "Ctrl+I", () => new ImportPage()),
            new("prompts", Icons.Chat, T("AI prompts"), "Ctrl+P", () => new PromptsPage()),
        };
        _bottom = new()
        {
            new("trash", Icons.Delete, T("Recycle bin"), null, () => new TrashPage()),
            new("settings", Icons.Settings, T("Settings"), "Ctrl+,", () => new SettingsPage()),
        };
        RefreshCounts();
    }

    public void SetSelected(string key)
    {
        _selected = key;
        RefreshCounts();
        Invalidate();
    }

    public void RefreshCounts()
    {
        try
        {
            _dueCount = Host.Store.CountDueToday();
            _trashCount = Host.Store.TrashCount();
        }
        catch
        {
            _dueCount = _trashCount = 0;
        }
        Invalidate();
    }

    private Anim HoverFor(string key)
    {
        if (!_hoverAnims.TryGetValue(key, out var a)) _hoverAnims[key] = a = new Anim(this, 120);
        return a;
    }

    // ── geometry ─────────────────────────────────────────────────────

    private int ItemH => S(42);
    private Rectangle CollapseRect => _collapsed ? new Rectangle((Width - S(32)) / 2, S(64), S(32), S(28)) : new Rectangle(Width - S(44), S(22), S(30), S(28));

    private IEnumerable<(Entry Entry, Rectangle Rect)> Items()
    {
        int y = _collapsed ? S(104) : S(84);
        foreach (var e in _top)
        {
            yield return (e, new Rectangle(S(12), y, Width - S(24), ItemH));
            y += ItemH + S(4);
        }
        int bottom = Height - FooterHeight - S(8);
        for (int i = _bottom.Count - 1; i >= 0; i--)
        {
            bottom -= ItemH;
            yield return (_bottom[i], new Rectangle(S(12), bottom, Width - S(24), ItemH));
            bottom -= S(4);
        }
    }

    private int FooterHeight => _collapsed ? S(132) : S(104);

    private Rectangle LangRect => _collapsed
        ? new Rectangle((Width - S(44)) / 2, Height - FooterHeight + S(16), S(44), S(30))
        : new Rectangle(S(16), Height - FooterHeight + S(16), S(96), S(32));

    private Rectangle ThemeRect => _collapsed
        ? new Rectangle((Width - S(36)) / 2, Height - FooterHeight + S(52), S(36), S(32))
        : new Rectangle(Width - S(16) - S(36), Height - FooterHeight + S(16), S(36), S(32));

    private Rectangle UpdateRect => _collapsed
        ? new Rectangle((Width - S(36)) / 2, Height - S(40), S(36), S(28))
        : new Rectangle(S(16), Height - S(46), Width - S(32), S(30));

    // ── input ────────────────────────────────────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string? hover = null;
        foreach (var (entry, rect) in Items()) if (rect.Contains(e.Location)) hover = entry.Key;
        if (CollapseRect.Contains(e.Location)) hover = "__collapse";
        if (LangRect.Contains(e.Location)) hover = "__lang";
        if (ThemeRect.Contains(e.Location)) hover = "__theme";
        if (Host.AvailableUpdate != null && UpdateRect.Contains(e.Location)) hover = "__update";
        if (hover != _hover)
        {
            if (_hover != null) HoverFor(_hover).To(0);
            _hover = hover;
            if (_hover != null) HoverFor(_hover).To(1);
            Cursor = hover != null ? Cursors.Hand : Cursors.Default;
            UpdateTooltip();
        }
    }

    private void UpdateTooltip()
    {
        string tip = _hover switch
        {
            "__collapse" => _collapsed ? T("Expand sidebar") : T("Collapse sidebar"),
            "__lang" => T("Language: English / Български"),
            "__theme" => T("Switch light / dark theme") + "   (Ctrl+T)",
            "__update" => T("A new version is available"),
            null => "",
            _ => _top.Concat(_bottom).Where(e => e.Key == _hover).Select(e => e.Text + (e.Shortcut != null ? $"   ({e.Shortcut})" : "")).FirstOrDefault() ?? "",
        };
        if (!_collapsed && _hover is not ("__collapse" or "__lang" or "__theme")) tip = "";
        Tips.Set(this, tip);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hover != null) HoverFor(_hover).To(0);
        _hover = null;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (CollapseRect.Contains(e.Location))
        {
            _collapsed = !_collapsed;
            Host.Settings.SidebarCollapsed = _collapsed;
            Host.SaveSettings();
            Width = _collapsed ? S(72) : S(240);
            Invalidate();
            return;
        }
        if (LangRect.Contains(e.Location))
        {
            Host.Settings.Language = L.IsBg ? L.English : L.BulgarianCode;
            Host.SaveSettings();
            L.SetLanguage(Host.Settings.Language);
            return;
        }
        if (ThemeRect.Contains(e.Location))
        {
            ToggleTheme();
            return;
        }
        if (Host.AvailableUpdate != null && UpdateRect.Contains(e.Location))
        {
            UpdateDialog.ShowFor(Host.AvailableUpdate);
            return;
        }
        foreach (var (entry, rect) in Items())
            if (rect.Contains(e.Location)) Host.Main?.Navigate(entry.Create);
    }

    public static void ToggleTheme()
    {
        Host.Settings.Theme = Theme.IsDark ? (Theme.P.Sepia ? ThemeMode.Light : ThemeMode.Light) : ThemeMode.Dark;
        Host.SaveSettings();
        Theme.Apply(Host.Settings);
        Host.Main?.Rebuild();
    }

    // ── painting ─────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.P.Sidebar);
        Gfx.Smooth(g);
        var p = Theme.P;

        // Logo
        var logo = new RectangleF(_collapsed ? (Width - S(34)) / 2f : S(20), S(20), S(34), S(34));
        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(logo, p.Accent, Gfx.Blend(p.Accent, Color.FromArgb(139, 92, 246), 0.6f), 45f))
        using (var path = Gfx.Round(logo, S(9)))
            g.FillPath(brush, path);
        Gfx.Glyph(g, Icons.Reading, Fonts.Icon(13f), Color.White, Rectangle.Round(logo));
        if (!_collapsed)
            Gfx.Text(g, "Study App", Fonts.Heading(13.5f), p.Text, new Rectangle(S(64), S(20), Width - S(110), S(34)), Gfx.Left);

        // Collapse button
        var cr = CollapseRect;
        float ch = _hover == "__collapse" ? HoverFor("__collapse").Value : 0;
        if (ch > 0) Gfx.FillRound(g, Gfx.Blend(p.Sidebar, p.Text, 0.07f * ch), cr, S(6));
        Gfx.Glyph(g, _collapsed ? "" : "", Fonts.Icon(7.5f), p.Text3, cr);

        foreach (var (entry, rect) in Items()) DrawItem(g, entry, rect);

        DrawFooter(g);
    }

    private void DrawItem(Graphics g, Entry entry, Rectangle rect)
    {
        var p = Theme.P;
        bool selected = entry.Key == _selected;
        float h = HoverFor(entry.Key).Value;
        if (selected)
        {
            Gfx.FillRound(g, p.AccentSoft, rect, S(9));
            Gfx.FillRound(g, p.Accent, new RectangleF(rect.X, rect.Y + S(11), S(3), rect.Height - S(22)), S(2));
        }
        else if (h > 0)
        {
            Gfx.FillRound(g, Gfx.Blend(p.Sidebar, p.Text, (p.Dark ? 0.09f : 0.06f) * h), rect, S(9));
        }

        var fore = selected ? p.AccentText : Gfx.Blend(p.Text2, p.Text, h);
        var iconRect = _collapsed ? rect : new Rectangle(rect.X + S(14), rect.Y, S(22), rect.Height);
        Gfx.Glyph(g, entry.Glyph, Fonts.Icon(12f), fore, iconRect);
        if (!_collapsed)
            Gfx.Text(g, entry.Text, selected ? Fonts.UiSemibold(10.5f) : Fonts.Ui(10.5f), fore, new Rectangle(rect.X + S(48), rect.Y, rect.Width - S(96), rect.Height), Gfx.Left);

        int badge = entry.Key == "review" ? _dueCount : entry.Key == "trash" ? _trashCount : 0;
        if (badge > 0)
        {
            var text = badge > 999 ? "999+" : badge.ToString();
            var font = Fonts.UiSemibold(8f);
            int bw = Math.Max(S(22), Gfx.Measure(text, font).Width + S(12));
            var br = _collapsed
                ? new Rectangle(rect.Right - bw + S(2), rect.Y + S(3), bw, S(18))
                : new Rectangle(rect.Right - bw - S(10), rect.Y + (rect.Height - S(20)) / 2, bw, S(20));
            bool strong = entry.Key == "review";
            Gfx.FillRound(g, strong ? p.Accent : p.Surface3, br, br.Height / 2f);
            Gfx.Text(g, text, font, strong ? p.OnAccent : p.Text2, br, Gfx.Center);
        }
    }

    private void DrawFooter(Graphics g)
    {
        var p = Theme.P;
        using (var pen = new Pen(p.Border)) g.DrawLine(pen, S(16), Height - FooterHeight, Width - S(16), Height - FooterHeight);

        // Language switch
        var lr = LangRect;
        if (_collapsed)
        {
            float lh = _hover == "__lang" ? HoverFor("__lang").Value : 0;
            Gfx.FillRound(g, Gfx.Blend(p.Sidebar, p.Text, 0.06f + 0.04f * lh), lr, S(8));
            Gfx.Text(g, L.IsBg ? "БГ" : "EN", Fonts.UiSemibold(9f), p.Text, lr, Gfx.Center);
        }
        else
        {
            Gfx.FillRound(g, Gfx.Blend(p.Sidebar, p.Text, 0.06f), lr, S(9));
            var half = new Rectangle(lr.X + S(3), lr.Y + S(3), (lr.Width - S(6)) / 2, lr.Height - S(6));
            var other = new Rectangle(half.Right, half.Y, half.Width, half.Height);
            var sel = L.IsBg ? other : half;
            Gfx.FillRound(g, p.Dark ? p.Surface3 : p.Surface, sel, S(7));
            Gfx.Text(g, "EN", Fonts.UiSemibold(9f), L.IsBg ? p.Text3 : p.Text, half, Gfx.Center);
            Gfx.Text(g, "БГ", Fonts.UiSemibold(9f), L.IsBg ? p.Text : p.Text3, other, Gfx.Center);
        }

        // Theme toggle
        var tr = ThemeRect;
        float th = _hover == "__theme" ? HoverFor("__theme").Value : 0;
        Gfx.FillRound(g, Gfx.Blend(p.Sidebar, p.Text, 0.06f + 0.05f * th), tr, S(9));
        Gfx.Glyph(g, Theme.IsDark ? Icons.Sun : Icons.Moon, Fonts.Icon(11f), p.Text, tr);

        // Update pill or version
        if (Host.AvailableUpdate != null)
        {
            var ur = UpdateRect;
            float uh = _hover == "__update" ? HoverFor("__update").Value : 0;
            Gfx.FillRound(g, Gfx.Blend(p.Accent, p.AccentHover, uh), ur, ur.Height / 2f);
            if (_collapsed) Gfx.Glyph(g, Icons.Update, Fonts.Icon(10f), p.OnAccent, ur);
            else
            {
                Gfx.Glyph(g, Icons.Update, Fonts.Icon(9.5f), p.OnAccent, new Rectangle(ur.X + S(10), ur.Y, S(18), ur.Height));
                Gfx.Text(g, F("Update to v{0}", Host.AvailableUpdate.Version), Fonts.UiSemibold(9f), p.OnAccent,
                    new Rectangle(ur.X + S(32), ur.Y, ur.Width - S(40), ur.Height), Gfx.Left);
            }
        }
        else if (!_collapsed)
        {
            Gfx.Text(g, "v" + Host.Version, Fonts.Ui(8.5f), p.Text3, new Rectangle(S(18), Height - S(40), Width - S(36), S(24)), Gfx.Left);
        }
    }
}
