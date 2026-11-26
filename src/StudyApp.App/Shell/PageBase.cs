using System.Runtime.InteropServices;

namespace StudyApp.App.Shell;

/// <summary>
/// Base class for every screen. Pages are rebuilt from <see cref="Factory"/> when navigating back,
/// or when the theme, language or DPI changes — so they never need to handle those changes themselves.
/// </summary>
public abstract class PageBase : Panel
{
    protected PageBase()
    {
        BackColor = Theme.P.Bg;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    /// <summary>Sidebar item to highlight.</summary>
    public abstract string NavKey { get; }

    /// <summary>Recreates this page (set by the navigator).</summary>
    public Func<PageBase>? Factory { get; set; }

    /// <summary>Hides the sidebar for a calm, distraction-free screen (study sessions, tests, reading).</summary>
    public virtual bool FocusMode => false;

    /// <summary>False for transient screens (study sessions) that should not be returned to with "Back".</summary>
    public virtual bool KeepInHistory => true;

    public virtual bool CanLeave() => true;

    /// <summary>Page keyboard shortcuts. Called before the focused control unless the user is typing.</summary>
    public virtual bool HandleKey(Keys keyData) => false;

    public virtual void OnActivated() { }

    public virtual void OnDeactivated() { }

    /// <summary>Called when files are dropped while this page is open. Return true if handled here.</summary>
    public virtual bool HandleDroppedFiles(string[] files) => false;

    protected ScrollHost? Scroller { get; private set; }
    protected Column Body { get; private set; } = null!;

    /// <summary>The standard layout: a scrolling, centred column with comfortable margins.</summary>
    protected Column UseStandardLayout(int maxWidth = 1180, int top = 28)
    {
        Body = new Column { Gap = S(18), Padding = Pad(36, top, 36, 48), BackColor = Theme.P.Bg };
        Scroller = new ScrollHost(Body) { Dock = DockStyle.Fill, MaxContentWidth = S(maxWidth), BackColor = Theme.P.Bg };
        Controls.Add(Scroller);
        return Body;
    }

    /// <summary>Page title row with optional back button, subtitle and actions on the right.</summary>
    protected Row PageHeader(string title, string? subtitle = null, bool back = false, params Control[] actions)
    {
        var row = new Row { Gap = S(12), Align = VAlign.Center };
        if (back && Host.Main?.CanGoBack == true)
        {
            var b = ButtonEx.Icon(Icons.Back, T("Back"), "Alt+←", Kind.Ghost, 38);
            b.Click += (_, _) => Host.Main?.GoBack();
            row.Controls.Add(b);
        }
        var col = new Column { Gap = S(4) }.Flex();
        col.Controls.Add(new TextLabel(title, Fonts.Heading(21f), Theme.P.Text) { MaxLines = 2 });
        if (!string.IsNullOrEmpty(subtitle)) col.Controls.Add(new TextLabel(subtitle, Fonts.Ui(10f), Theme.P.Text2));
        row.Controls.Add(col);
        foreach (var a in actions) row.Controls.Add(a);
        return row;
    }

    protected static void Go(Func<PageBase> factory) => Host.Main?.Navigate(factory);

    // ── keyboard helpers ─────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    /// <summary>True while a text box has the keyboard focus (single-key shortcuts are then ignored).</summary>
    public static bool IsTyping() => Control.FromHandle(GetFocus()) is TextBoxBase;
}
