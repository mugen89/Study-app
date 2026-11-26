using System.Diagnostics;
using System.Text;

namespace StudyApp.App.Dialogs;

/// <summary>Themed modal dialog: heading, scrolling content column and a footer with buttons.</summary>
public class DialogBase : Form
{
    private readonly ScrollHost _scroll;
    private readonly FooterPanel _footer;
    public Column Content { get; }
    public Row Buttons { get; }

    public DialogBase(string title, int width = 520, string? subtitle = null)
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        Text = title;
        Font = Fonts.Ui(10f);
        BackColor = Theme.P.Surface;

        Content = new Column { Gap = S(14), Padding = Pad(26, 22, 26, 22), BackColor = Theme.P.Surface };
        Content.Controls.Add(new TextLabel(title, Fonts.Heading(15f), Theme.P.Text));
        if (!string.IsNullOrEmpty(subtitle)) Content.Controls.Add(new TextLabel(subtitle, Fonts.Ui(10f), Theme.P.Text2));
        _scroll = new ScrollHost(Content) { Dock = DockStyle.Fill, BackColor = Theme.P.Surface };

        Buttons = new Row { Gap = S(10) };
        Buttons.Controls.Add(new Spacer());
        _footer = new FooterPanel(Buttons) { Dock = DockStyle.Bottom, Height = S(70) };

        Controls.Add(_scroll);
        Controls.Add(_footer);
        ClientSize = new Size(S(width), S(320));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleTitleBar(Handle);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToContent();
        if (Owner != null)
        {
            var o = Owner.Bounds;
            Location = new Point(o.X + (o.Width - Width) / 2, o.Y + Math.Max(S(40), (o.Height - Height) / 3));
        }
    }

    /// <summary>Sizes the dialog to its content (capped to the screen; then it scrolls).</summary>
    protected void FitToContent()
    {
        int needed = Content.HeightFor(ClientSize.Width - S(12)) + _footer.Height;
        int max = (int)((Screen.FromControl(this).WorkingArea.Height - S(60)) * 0.92);
        ClientSize = new Size(ClientSize.Width, Math.Clamp(needed, S(160), max));
        _scroll.PerformLayout();
    }

    protected void Relayout()
    {
        _scroll.PerformLayout();
    }

    public ButtonEx AddButton(string text, Kind kind, DialogResult result, string? glyph = null)
    {
        var b = new ButtonEx(text, glyph, kind, 38) { DialogResult = result, MinWidth = S(96) };
        b.Width = b.GetPreferredSize(Size.Empty).Width;
        Buttons.Controls.Add(b);
        if (result == DialogResult.Cancel) CancelButton = b;
        return b;
    }

    public ButtonEx AddAction(string text, Kind kind, Action action, string? glyph = null)
    {
        var b = new ButtonEx(text, glyph, kind, 38) { MinWidth = S(96) };
        b.Width = b.GetPreferredSize(Size.Empty).Width;
        b.Click += (_, _) => action();
        Buttons.Controls.Add(b);
        return b;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        if (keyData == (Keys.Control | Keys.Enter) && AcceptButton != null)
        {
            AcceptButton.PerformClick();
            return true;
        }
        if (keyData == Keys.Enter && AcceptButton != null && !(ActiveControl is InputBox ib && ib.Box.Multiline) && PageBaseIsNotTypingMultiline())
        {
            AcceptButton.PerformClick();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static bool PageBaseIsNotTypingMultiline()
    {
        var focused = Shell.PageBase.IsTyping();
        if (!focused) return true;
        return Form.ActiveForm?.ActiveControl switch
        {
            InputBox ib => !ib.Box.Multiline,
            _ => true,
        };
    }

    private sealed class FooterPanel : Panel
    {
        private readonly Row _row;

        public FooterPanel(Row row)
        {
            _row = row;
            BackColor = Theme.IsDark ? Theme.P.Bg : Theme.P.Surface2;
            Controls.Add(row);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int h = S(38);
            _row.SetBounds(S(26), (Height - h) / 2, Width - S(52), h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var pen = new Pen(Theme.P.Border);
            e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }
    }
}

/// <summary>Standard confirmation / information / text-input dialogs.</summary>
public static class Ask
{
    private static IWin32Window? Owner => Form.ActiveForm ?? Host.Main;

    public static bool Confirm(string title, string message, string confirmText, bool danger = false, string? cancelText = null)
    {
        using var d = new DialogBase(title, 480);
        d.Content.Controls.Add(new TextLabel(message, Fonts.Ui(10.5f), Theme.P.Text2));
        d.AddButton(cancelText ?? T("Cancel"), Kind.Secondary, DialogResult.Cancel);
        var ok = d.AddButton(confirmText, danger ? Kind.Danger : Kind.Primary, DialogResult.OK);
        d.AcceptButton = ok;
        d.Shown += (_, _) => ok.Focus();
        return d.ShowDialog(Owner) == DialogResult.OK;
    }

    public static void Info(string title, string message)
    {
        using var d = new DialogBase(title, 480);
        d.Content.Controls.Add(new TextLabel(message, Fonts.Ui(10.5f), Theme.P.Text2));
        var ok = d.AddButton(T("OK"), Kind.Primary, DialogResult.OK);
        d.AcceptButton = ok;
        d.ShowDialog(Owner);
    }

    public static string? Prompt(string title, string label, string initial = "", string placeholder = "", bool multiline = false)
    {
        using var d = new DialogBase(title, 500);
        var input = new InputBox(placeholder, multiline, 5) { Text = initial };
        d.Content.Controls.Add(Ui.Field(label, input));
        d.AddButton(T("Cancel"), Kind.Secondary, DialogResult.Cancel);
        var ok = d.AddButton(T("Save"), Kind.Primary, DialogResult.OK);
        d.AcceptButton = ok;
        d.Shown += (_, _) => { input.Focus(); input.Box.SelectAll(); };
        return d.ShowDialog(Owner) == DialogResult.OK ? input.Text.Trim() : null;
    }
}
/// <summary>Friendly error message with "copy details" and "report a problem".</summary>
public sealed class ErrorDialog : DialogBase
{
    private ErrorDialog(string message, Exception? ex) : base(T("Something went wrong"), 560)
    {
        Content.Controls.Add(new TextLabel(message, Fonts.Ui(10.5f), Theme.P.Text));
        Content.Controls.Add(new TextLabel(T("Your study data is safe. If this keeps happening, use \"Copy details\" and send them to the person who gave you the app."), Fonts.Ui(9.5f), Theme.P.Text2));
        var details = Details(message, ex);
        if (ex != null)
        {
            var box = new InputBox("", multiline: true, lines: 6, mono: true) { Text = details, ReadOnly = true };
            Content.Controls.Add(box);
        }
        AddAction(T("Copy details"), Kind.Secondary, () =>
        {
            try { Clipboard.SetText(details); } catch { /* clipboard busy */ }
        }, Icons.Copy);
        AddAction(T("Report a problem"), Kind.Secondary, () => Feedback.OpenIssue(T("Error report"), details), Icons.Feedback);
        var ok = AddButton(T("Close"), Kind.Primary, DialogResult.OK);
        AcceptButton = ok;
    }

    private static string Details(string message, Exception? ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Study App {Host.Version} · {Environment.OSVersion} · {(Environment.Is64BitProcess ? "x64" : "x86")}");
        sb.AppendLine(message);
        if (ex != null) sb.AppendLine(ex.ToString());
        return sb.ToString();
    }

    public static void Show(string message, Exception? ex)
    {
        Log.Error(message, ex);
        try
        {
            using var d = new ErrorDialog(message, ex);
            d.ShowDialog(Form.ActiveForm ?? Host.Main);
        }
        catch
        {
            MessageBox.Show(message + "\n\n" + ex?.Message, "Study App", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void ShowUnhandled(Exception ex) => Show(T("An unexpected error occurred."), ex);
}

/// <summary>Opens the GitHub "new issue" page pre-filled with app and system details.</summary>
public static class Feedback
{
    public static string SystemInfo() =>
        $"Study App {Host.Version}\nWindows {Environment.OSVersion.Version}\nLanguage: {L.Lang}\nTheme: {Host.Settings.Theme}";

    public static void OpenIssue(string title, string body)
    {
        var text = body + "\n\n---\n" + SystemInfo();
        if (text.Length > 6000) text = text[..6000];
        var url = $"{Host.RepoUrl}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(text)}";
        OpenUrl(url);
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Could not open " + url, ex); }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn("Could not open folder " + path, ex); }
    }
}
