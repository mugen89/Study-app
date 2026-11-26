using StudyApp.Domain.Grading;
using StudyApp.Domain.Srs;

namespace StudyApp.App.Pages;

/// <summary>
/// A flashcard painted in the reading font with a real 3D-style flip when the answer is revealed.
/// Front: question (+ optional hint). Back: question, answer, explanation, page reference, note.
/// </summary>
public sealed class FlashcardView : UiControl, IHeightForWidth
{
    private Item _item;
    private bool _revealed;
    private bool _hintShown;
    private Rectangle _hintRect;
    private Bitmap? _from, _to;
    private float _flip = -1;

    public event EventHandler? RevealRequested;

    public FlashcardView(Item item)
    {
        _item = item;
        Cursor = Cursors.Hand;
    }

    public bool Revealed => _revealed;

    public void SetItem(Item item)
    {
        _item = item;
        LayoutProps.Relayout(this);
        Invalidate();
    }

    private int Pad => S(36);
    private int MinHeight => S(330);

    private RichStyle FrontStyle(bool small) => RichStyle.Study(small ? Host.Settings.QuestionFontSize : Host.Settings.CardFontSize,
        small ? Theme.P.Text2 : Theme.P.Text, center: Host.Settings.CenterCardText);

    private RichStyle BackStyle => RichStyle.Study(Host.Settings.CardFontSize * 0.92f, Theme.P.Text, center: Host.Settings.CenterCardText);
    private RichStyle SideStyle => RichStyle.Study(Host.Settings.AnswerFontSize, Theme.P.Text2);

    private sealed class Plan
    {
        public RichLayout? Front, Back, Hint, Explanation, Note;
        public int Height;
    }

    private Plan Measure(int width, bool revealed)
    {
        var plan = new Plan();
        int inner = Math.Max(S(120), width - Pad * 2);
        int h = Pad + S(24);
        plan.Front = RichLayout.Build(MarkdownLite.Parse(_item.Prompt), FrontStyle(revealed), inner);
        h += plan.Front.Height;
        if (!revealed)
        {
            if (_item.Hint.Length > 0)
            {
                h += S(24);
                if (_hintShown)
                {
                    plan.Hint = RichLayout.Build(MarkdownLite.Parse(_item.Hint), RichStyle.Study(Host.Settings.AnswerFontSize, Theme.P.Text2, Host.Settings.CenterCardText), inner - S(32));
                    h += plan.Hint.Height + S(24);
                }
                else h += S(24);
            }
        }
        else
        {
            h += S(28) + S(1) + S(28);
            plan.Back = RichLayout.Build(MarkdownLite.Parse(_item.Answer), BackStyle, inner);
            h += plan.Back.Height;
            if (_item.Explanation.Length > 0)
            {
                plan.Explanation = RichLayout.Build(MarkdownLite.Parse(_item.Explanation), SideStyle, inner - S(36));
                h += S(28) + S(26) + plan.Explanation.Height + S(22);
            }
            if (_item.PersonalNote.Length > 0)
            {
                plan.Note = RichLayout.Build(MarkdownLite.Parse(_item.PersonalNote), RichStyle.Ui(9.5f, Theme.P.Text), inner - S(60));
                h += S(14) + plan.Note.Height + S(24);
            }
            if (_item.Page.Length > 0 || _item.Tags.Count > 0) h += S(24) + S(26);
        }
        h += Pad;
        plan.Height = Math.Max(MinHeight, h);
        return plan;
    }

    public int HeightFor(int width) => Measure(width, _revealed).Height;

    /// <summary>Turns the card over with an animation.</summary>
    public void Reveal()
    {
        if (_revealed) return;
        if (!Theme.Animations || Width <= 0)
        {
            _revealed = true;
            LayoutProps.Relayout(this);
            Invalidate();
            return;
        }
        _from = Snapshot(false, Height);
        _revealed = true;
        int newHeight = HeightFor(Width);
        _to = Snapshot(true, newHeight);
        _flip = 0;
        LayoutProps.Relayout(this);
        Animator.Tween(this, 300, t => { _flip = t; Invalidate(); }, () =>
        {
            _flip = -1;
            _from?.Dispose(); _to?.Dispose();
            _from = _to = null;
            Invalidate();
        });
    }

    private Bitmap Snapshot(bool revealed, int height)
    {
        var bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, height));
        using var g = Graphics.FromImage(bmp);
        g.Clear(BackColor);
        DrawCard(g, revealed, new Rectangle(0, 0, Width, height));
        return bmp;
    }

    public void ToggleHint()
    {
        if (_revealed || _item.Hint.Length == 0) return;
        _hintShown = !_hintShown;
        LayoutProps.Relayout(this);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = _revealed ? (_hintRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default) : Cursors.Hand;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (!_revealed && _hintRect.Contains(e.Location)) { ToggleHint(); return; }
        if (!_revealed) RevealRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_flip >= 0 && _from != null && _to != null)
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            var img = _flip < 0.5f ? _from : _to;
            float scale = _flip < 0.5f ? 1 - _flip * 2 : _flip * 2 - 1;
            float w = Math.Max(1, Width * scale);
            g.DrawImage(img, new RectangleF((Width - w) / 2f, 0, w, img.Height));
            return;
        }
        DrawCard(g, _revealed, ClientRectangle);
    }

    private void DrawCard(Graphics g, bool revealed, Rectangle bounds)
    {
        Gfx.Smooth(g);
        var p = Theme.P;
        var plan = Measure(bounds.Width, revealed);
        var card = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height - S(3));
        Gfx.FillRound(g, p.Dark ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(22, 30, 40, 80), new RectangleF(card.X, card.Y + S(3), card.Width, card.Height), S(18));
        Gfx.FillRound(g, p.Surface, card, S(18));
        Gfx.DrawRound(g, p.Border, card, S(18));

        int inner = Math.Max(S(120), bounds.Width - Pad * 2);
        int x = bounds.X + Pad;
        // Small caption
        Gfx.Text(g, revealed ? T("Answer") : T("Question"), Fonts.UiSemibold(8.5f), p.Text3, new Rectangle(x, bounds.Y + S(20), inner, S(18)),
            (Host.Settings.CenterCardText ? Gfx.Center : Gfx.Left));

        // Vertically centre short content on the front.
        int contentH = plan.Height - Pad * 2 - S(24);
        int y = bounds.Y + Pad + S(24);
        if (!revealed && plan.Height == MinHeight)
        {
            int used = plan.Front!.Height + (_item.Hint.Length > 0 ? S(48) + (plan.Hint?.Height ?? 0) : 0);
            y += Math.Max(0, (contentH - used) / 2 - S(10));
        }

        plan.Front!.Draw(g, x, y, Rectangle.Round(bounds));
        y += plan.Front.Height;

        if (!revealed)
        {
            _hintRect = Rectangle.Empty;
            if (_item.Hint.Length > 0)
            {
                y += S(24);
                var label = _hintShown ? T("Hide hint") : T("Show hint (H)");
                var font = Fonts.UiSemibold(9.5f);
                int w = Gfx.Measure(label, font).Width + S(40);
                int hx = Host.Settings.CenterCardText ? x + (inner - w) / 2 : x;
                _hintRect = new Rectangle(hx, y - S(4), w, S(30));
                Gfx.FillRound(g, p.AccentSoft, _hintRect, S(15));
                Gfx.Glyph(g, Icons.Lightbulb, Fonts.Icon(9.5f), p.AccentText, new Rectangle(hx + S(10), y - S(4), S(18), S(30)));
                Gfx.Text(g, label, font, p.AccentText, new Rectangle(hx + S(30), y - S(4), w - S(34), S(30)), Gfx.Left);
                y += S(24);
                if (_hintShown && plan.Hint != null)
                {
                    y += S(12);
                    plan.Hint.Draw(g, x + S(16), y, Rectangle.Round(bounds));
                }
            }
            return;
        }

        y += S(28);
        using (var pen = new Pen(p.Border, Math.Max(1, Sf(1))) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            g.DrawLine(pen, x + inner * 0.15f, y, x + inner * 0.85f, y);
        y += S(28);
        plan.Back!.Draw(g, x, y, Rectangle.Round(bounds));
        y += plan.Back.Height;

        if (plan.Explanation != null)
        {
            y += S(28);
            var box = new RectangleF(x, y, inner, plan.Explanation.Height + S(26) + S(22));
            Gfx.FillRound(g, p.Surface2, box, S(12));
            Gfx.Glyph(g, Icons.Info, Fonts.Icon(9.5f), p.Text2, new Rectangle(x + S(16), y + S(10), S(18), S(20)));
            Gfx.Text(g, T("Explanation"), Fonts.UiSemibold(9f), p.Text2, new Rectangle(x + S(40), y + S(10), inner - S(56), S(20)), Gfx.Left);
            plan.Explanation.Draw(g, x + S(18), y + S(36), Rectangle.Round(bounds));
            y += (int)box.Height;
        }

        if (plan.Note != null)
        {
            y += S(14);
            var box = new RectangleF(x, y, inner, plan.Note.Height + S(24));
            Gfx.FillRound(g, p.WarningSoft, box, S(12));
            Gfx.Glyph(g, Icons.Note, Fonts.Icon(10f), p.Warning, new Rectangle(x + S(14), y + S(10), S(20), S(20)));
            plan.Note.Draw(g, x + S(44), y + S(12), Rectangle.Round(bounds));
            y += (int)box.Height;
        }

        if (_item.Page.Length > 0 || _item.Tags.Count > 0)
        {
            y += S(24);
            int cx = x;
            if (_item.Page.Length > 0) cx = PaintChip(g, cx, y, L.Page(_item.Page), p.AccentText, p.AccentSoft, Icons.Book) + S(8);
            foreach (var tag in _item.Tags.Take(4)) cx = PaintChip(g, cx, y, "#" + tag, p.Text2, p.Surface2, null) + S(6);
        }
    }

    public static int PaintChip(Graphics g, int x, int y, string text, Color fore, Color back, string? glyph)
    {
        var font = Fonts.UiSemibold(8.5f);
        int w = Gfx.Measure(text, font).Width + S(22) + (glyph != null ? S(18) : 0);
        Gfx.FillRound(g, back, new RectangleF(x, y, w, S(26)), S(13));
        int tx = x + S(11);
        if (glyph != null)
        {
            Gfx.Glyph(g, glyph, Fonts.Icon(8.5f), fore, new Rectangle(tx - S(2), y, S(16), S(26)));
            tx += S(18);
        }
        Gfx.Text(g, text, font, fore, new Rectangle(tx, y, w - (tx - x), S(26)), Gfx.Left);
        return x + w;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _from?.Dispose();
            _to?.Dispose();
        }
        base.Dispose(disposing);
    }
}

public enum OptionState { Normal, Selected, Correct, Wrong, Missed, Dimmed }

/// <summary>One answer option: letter badge + text, with correct/wrong states and an optional "why".</summary>
public sealed class OptionButton : UiControl, IHeightForWidth
{
    private readonly string _letter;
    private readonly string _text;
    private readonly string? _why;
    private OptionState _state;
    private bool _showWhy;
    private RichLayout? _layout, _whyLayout;

    public OptionButton(string letter, string text, string? why)
    {
        _letter = letter;
        _text = text;
        _why = why;
        Cursor = Cursors.Hand;
    }

    public bool Locked { get; set; }

    public OptionState State
    {
        get => _state;
        set { _state = value; Invalidate(); }
    }

    public void ShowWhy(bool show)
    {
        _showWhy = show && !string.IsNullOrWhiteSpace(_why);
        _layout = null;
        LayoutProps.Relayout(this);
        Invalidate();
    }

    private int TextLeft => S(62);

    private RichLayout TextLayout(int width)
    {
        int w = Math.Max(S(60), width - TextLeft - S(18));
        if (_layout == null || _layout.Width != w)
        {
            _layout = RichLayout.Build(MarkdownLite.Parse(_text), RichStyle.Study(Host.Settings.AnswerFontSize, Theme.P.Text, lineHeight: Math.Min(Host.Settings.LineHeight, 1.45f)), w);
            _whyLayout = _showWhy ? RichLayout.Build(MarkdownLite.Parse(_why!), RichStyle.Ui(9.5f, Theme.P.Text2), w) : null;
        }
        return _layout;
    }

    public int HeightFor(int width)
    {
        var t = TextLayout(width);
        int h = Math.Max(S(56), t.Height + S(30));
        if (_showWhy && _whyLayout != null) h += _whyLayout.Height + S(8);
        return h;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        Cursor = Locked ? Cursors.Default : Cursors.Hand;
        base.OnMouseEnter(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.P.Bg);
        Gfx.Smooth(g);
        var p = Theme.P;
        float hover = Locked ? 0 : HoverAnim.Value;
        Color fill, border, badgeFill, badgeText;
        string? glyph = null;
        switch (_state)
        {
            case OptionState.Selected:
                fill = p.AccentSoft; border = p.Accent; badgeFill = p.Accent; badgeText = p.OnAccent; break;
            case OptionState.Correct:
                fill = p.SuccessSoft; border = p.Success; badgeFill = p.Success; badgeText = Color.White; glyph = Icons.Check; break;
            case OptionState.Wrong:
                fill = p.DangerSoft; border = p.Danger; badgeFill = p.Danger; badgeText = Color.White; glyph = Icons.Close; break;
            case OptionState.Missed:
                fill = p.Surface; border = p.Success; badgeFill = p.SuccessSoft; badgeText = p.Success; glyph = Icons.Check; break;
            case OptionState.Dimmed:
                fill = p.Surface; border = p.Border; badgeFill = p.Surface2; badgeText = p.Text3; break;
            default:
                fill = Gfx.Blend(p.Surface, Gfx.Blend(p.Surface, p.Accent, 0.06f), hover);
                border = Gfx.Blend(p.Border, p.Accent, hover * 0.8f);
                badgeFill = Gfx.Blend(p.Surface2, p.AccentSoft, hover);
                badgeText = Gfx.Blend(p.Text2, p.AccentText, hover);
                break;
        }
        var rect = new RectangleF(0, 0, Width, Height);
        Gfx.FillRound(g, fill, rect, S(12));
        Gfx.DrawRound(g, border, rect, S(12), _state is OptionState.Normal or OptionState.Dimmed ? 1f : Sf(1.8f));

        var badge = new RectangleF(S(16), S(14), S(30), S(30));
        Gfx.FillRound(g, badgeFill, badge, S(9));
        if (glyph != null) Gfx.Glyph(g, glyph, Fonts.Icon(10f), badgeText, Rectangle.Round(badge));
        else Gfx.Text(g, _letter, Fonts.UiSemibold(10.5f), badgeText, Rectangle.Round(badge), Gfx.Center);

        var layout = TextLayout(Width);
        int ty = Math.Max(S(15), (Math.Max(S(56), layout.Height + S(30)) - layout.Height) / 2);
        layout.Draw(g, TextLeft, ty, ClientRectangle);
        if (_showWhy && _whyLayout != null)
            _whyLayout.Draw(g, TextLeft, ty + layout.Height + S(8), ClientRectangle);
    }
}

/// <summary>
/// Shows a question of any type with its answer controls, and the feedback after answering:
/// correct option(s), explanation, page reference, model answer and key points.
/// </summary>
public sealed class QuestionView : Column
{
    private readonly Item _item;
    private readonly int[] _order;
    private readonly Answer _answer;
    private readonly List<OptionButton> _options = new();
    private readonly InputBox? _typed;
    private readonly Column _feedback = new() { Gap = S(12), Visible = false };
    private bool _locked;

    /// <summary>Raised when the learner gave an answer that can be checked immediately (choice / true-false click).</summary>
    public event EventHandler? Answered;
    /// <summary>Raised when the selection changed (for exam mode).</summary>
    public event EventHandler? SelectionChanged;
    /// <summary>Enter pressed in the typed-answer box (Shift+Enter adds a new line).</summary>
    public event EventHandler? SubmitRequested;

    public QuestionView(Item item, int[] optionOrder, Answer answer, bool autoSubmit)
    {
        _item = item;
        _order = optionOrder.Length > 0 ? optionOrder : Enumerable.Range(0, item.Data.Options?.Count ?? 0).ToArray();
        _answer = answer;
        Gap = S(16);
        AutoSubmit = autoSubmit;

        var chips = new Row { Gap = S(8), Wrap = true };
        chips.Controls.Add(Chip.Neutral(ItemTypes.DisplayName(item.Type == ItemTypes.Flashcard ? ItemTypes.Short : item.Type)));
        if (item.Difficulty > 0) chips.Controls.Add(Chip.DifficultyChip(item.Difficulty));
        if (item.Type == ItemTypes.Multiple) chips.Controls.Add(Chip.Accent(T("Select all that apply")));
        if (item.Flagged) chips.Controls.Add(Chip.Status(T("Flagged"), Theme.P.Warning, Icons.Flag));
        Controls.Add(chips);

        Controls.Add(new RichTextView(item.Prompt, RichStyle.Study(Host.Settings.QuestionFontSize, Theme.P.Text)));

        switch (item.Type)
        {
            case ItemTypes.Single:
            case ItemTypes.Multiple:
                {
                    var opts = item.Data.Options ?? new();
                    var list = new Column { Gap = S(10) };
                    for (int display = 0; display < _order.Length; display++)
                    {
                        int original = _order[display];
                        if (original >= opts.Count) continue;
                        var btn = new OptionButton(((char)('A' + display)).ToString(), opts[original].Text, opts[original].Why);
                        btn.State = answer.Selected.Contains(original) ? OptionState.Selected : OptionState.Normal;
                        btn.Click += (_, _) => Choose(original);
                        _options.Add(btn);
                        list.Controls.Add(btn);
                    }
                    Controls.Add(list);
                    break;
                }
            case ItemTypes.TrueFalse:
                {
                    var row = new CardGrid { MinCellWidth = S(200), Gap = S(12), MaxColumns = 2 };
                    var t = new OptionButton("T", T("True"), null) { State = answer.Truth == true ? OptionState.Selected : OptionState.Normal };
                    var f = new OptionButton("F", T("False"), null) { State = answer.Truth == false ? OptionState.Selected : OptionState.Normal };
                    t.Click += (_, _) => ChooseTruth(true);
                    f.Click += (_, _) => ChooseTruth(false);
                    _options.Add(t);
                    _options.Add(f);
                    row.Controls.Add(t);
                    row.Controls.Add(f);
                    Controls.Add(row);
                    break;
                }
            default:
                _typed = new InputBox(T("Type your answer (optional) — then press Check"), multiline: true, lines: 3, fontPt: 11f) { Text = answer.Text };
                _typed.Changed += (_, _) =>
                {
                    _answer.Text = _typed.Text;
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                };
                _typed.Box.KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.Enter && !e.Shift && !_locked)
                    {
                        e.SuppressKeyPress = true;
                        SubmitRequested?.Invoke(this, EventArgs.Empty);
                    }
                };
                Controls.Add(_typed);
                break;
        }
        Controls.Add(_feedback);
    }

    public bool AutoSubmit { get; set; }
    public bool IsTyped => _typed != null;
    public bool HasAnswer => !_answer.IsEmpty;
    public Answer Answer => _answer;

    public void FocusInput()
    {
        if (_typed != null && !_locked) _typed.Focus();
    }

    private void Choose(int original)
    {
        if (_locked) return;
        if (_item.Type == ItemTypes.Single)
        {
            _answer.Selected.Clear();
            _answer.Selected.Add(original);
        }
        else if (!_answer.Selected.Remove(original)) _answer.Selected.Add(original);

        for (int i = 0; i < _options.Count; i++)
            _options[i].State = _answer.Selected.Contains(_order[i]) ? OptionState.Selected : OptionState.Normal;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if (AutoSubmit && _item.Type == ItemTypes.Single) Answered?.Invoke(this, EventArgs.Empty);
    }

    private void ChooseTruth(bool value)
    {
        if (_locked) return;
        _answer.Truth = value;
        _options[0].State = value ? OptionState.Selected : OptionState.Normal;
        _options[1].State = value ? OptionState.Normal : OptionState.Selected;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if (AutoSubmit) Answered?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keyboard: 1–9 / A–H choose options, T/F for true/false.</summary>
    public bool HandleKey(Keys key)
    {
        if (_locked) return false;
        if (_item.Type == ItemTypes.TrueFalse)
        {
            if (key is Keys.T or Keys.D1 or Keys.NumPad1) { ChooseTruth(true); return true; }
            if (key is Keys.F or Keys.D2 or Keys.NumPad2) { ChooseTruth(false); return true; }
            return false;
        }
        if (!ItemTypes.IsChoice(_item.Type)) return false;
        int index = key switch
        {
            >= Keys.D1 and <= Keys.D9 => key - Keys.D1,
            >= Keys.NumPad1 and <= Keys.NumPad9 => key - Keys.NumPad1,
            >= Keys.A and <= Keys.H => key - Keys.A,
            _ => -1,
        };
        if (index < 0 || index >= _order.Length) return false;
        Choose(_order[index]);
        return true;
    }

    public void Lock()
    {
        _locked = true;
        foreach (var o in _options) o.Locked = true;
        if (_typed != null) _typed.ReadOnly = true;
    }

    /// <summary>Marks options, shows the explanation, the page and (for typed answers) the model answer.</summary>
    public void ShowFeedback(Result result, ShortCheck? check = null)
    {
        Lock();
        var p = Theme.P;
        if (ItemTypes.IsChoice(_item.Type))
        {
            var correct = _item.Data.Correct ?? new();
            for (int i = 0; i < _options.Count; i++)
            {
                int original = _order[i];
                bool isCorrect = correct.Contains(original);
                bool chosen = _answer.Selected.Contains(original);
                _options[i].State = isCorrect ? (chosen ? OptionState.Correct : OptionState.Missed) : chosen ? OptionState.Wrong : OptionState.Dimmed;
                _options[i].ShowWhy(isCorrect || chosen);
            }
        }
        else if (_item.Type == ItemTypes.TrueFalse)
        {
            bool truth = _item.Data.Truth == true;
            _options[0].State = truth ? (_answer.Truth == true ? OptionState.Correct : OptionState.Missed) : (_answer.Truth == true ? OptionState.Wrong : OptionState.Dimmed);
            _options[1].State = !truth ? (_answer.Truth == false ? OptionState.Correct : OptionState.Missed) : (_answer.Truth == false ? OptionState.Wrong : OptionState.Dimmed);
        }

        _feedback.SuspendLayout();
        foreach (Control c in _feedback.Controls.Cast<Control>().ToList()) { _feedback.Controls.Remove(c); c.Dispose(); }

        if (_typed != null)
        {
            var model = new Card(18) { Gap = S(8), Fill = p.SuccessSoft, Stroke = Gfx.Blend(p.Surface, p.Success, 0.35f) };
            model.Controls.Add(new TextLabel(T("Model answer"), Fonts.UiSemibold(9.5f), p.Success));
            model.Controls.Add(new RichTextView(_item.Answer, RichStyle.Study(Host.Settings.AnswerFontSize, p.Text)));
            if (_item.Data.KeyPoints is { Count: > 0 } points)
            {
                model.Controls.Add(new TextLabel(T("A good answer mentions:"), Fonts.UiSemibold(9.5f), p.Text2));
                model.Controls.Add(new RichTextView(string.Join("\n", points.Select(k => "- " + k)), RichStyle.Study(Host.Settings.AnswerFontSize * 0.95f, p.Text)));
            }
            _feedback.Controls.Add(model);
            if (check is { } c && _answer.Text.Trim().Length > 0)
            {
                var verdict = c.Suggested switch
                {
                    Result.GotIt => c.NearMiss ? T("Very close — check the spelling.") : T("Your answer matches the model answer."),
                    Result.Partial => T("Your answer is partly similar. Compare it with the model answer."),
                    _ => T("Your answer looks different. Compare it with the model answer."),
                };
                _feedback.Controls.Add(new TextLabel(verdict, Fonts.Ui(9.5f), p.Text2));
            }
        }
        else
        {
            var (title, tone) = result switch
            {
                Result.GotIt => (T("Correct!"), Tone.Success),
                Result.Partial => (T("Partly correct"), Tone.Warning),
                _ => (T("Not quite"), Tone.Danger),
            };
            string? detail = result == Result.GotIt ? null : T("Correct answer:") + " " + CorrectText();
            _feedback.Controls.Add(new Banner(tone, title, detail));
        }

        if (_item.Explanation.Length > 0)
        {
            var exp = new Card(18) { Gap = S(8), Fill = p.Surface2, Stroke = p.Surface2 };
            exp.Controls.Add(new TextLabel(T("Explanation"), Fonts.UiSemibold(9.5f), p.Text2));
            exp.Controls.Add(new RichTextView(_item.Explanation, RichStyle.Study(Host.Settings.AnswerFontSize, p.Text)));
            _feedback.Controls.Add(exp);
        }

        var chips = new Row { Gap = S(8), Wrap = true };
        if (_item.Page.Length > 0)
        {
            var page = Chip.PageRef(_item.Page);
            if (result != Result.GotIt) page.Text = F("Re-read {0}", L.Page(_item.Page));
            page.Width = page.GetPreferredSize(Size.Empty).Width;
            chips.Controls.Add(page);
        }
        if (_item.Source.Length > 0) chips.Controls.Add(Chip.Neutral(_item.Source, Icons.Book));
        if (chips.Controls.Count > 0) _feedback.Controls.Add(chips);
        if (_item.PersonalNote.Length > 0) _feedback.Controls.Add(new Banner(Tone.Warning, T("Your note"), _item.PersonalNote));

        _feedback.ResumeLayout(true);
        _feedback.Visible = true;
        LayoutProps.Relayout(this);
    }

    private string CorrectText()
    {
        if (_item.Type == ItemTypes.TrueFalse) return _item.Data.Truth == true ? T("True") : T("False");
        var opts = _item.Data.Options ?? new();
        var correct = _item.Data.Correct ?? new();
        return string.Join("; ", correct.Where(i => i < opts.Count).Select(i =>
        {
            int display = Array.IndexOf(_order, i);
            return (display >= 0 ? $"{(char)('A' + display)}) " : "") + TextUtil.StripMarkdown(opts[i].Text);
        }));
    }
}

/// <summary>Again / Hard / Good / Easy, with the next interval on each button.</summary>
public sealed class RatingBar : Row
{
    public event EventHandler<Rating>? Rated;

    public RatingBar(Item item, Rating? suggested, string[]? labels = null)
    {
        Gap = S(10);
        var srs = Host.Settings.ToSrs();
        long now = Time.NowMs();
        var defs = new (Rating R, string Label, Color C)[]
        {
            (Rating.Again, labels?[0] ?? T("Again"), Theme.P.Danger),
            (Rating.Hard, labels?[1] ?? T("Hard"), Theme.P.Warning),
            (Rating.Good, labels?[2] ?? T("Good"), Theme.P.Success),
            (Rating.Easy, labels?[3] ?? T("Easy"), Theme.P.Info),
        };
        foreach (var (r, label, c) in defs)
        {
            var interval = Host.Settings.ShowIntervals ? Sm2.Preview(item, r, now, srs) : "";
            var b = new RatingButton(label, interval, ((int)r).ToString(), c, suggested == r).Flex();
            b.Click += (_, _) => Rated?.Invoke(this, r);
            Tips.Set(b, label, ((int)r).ToString());
            Controls.Add(b);
        }
    }

    private sealed class RatingButton : UiControl, IHeightForWidth
    {
        private readonly string _label;
        private readonly string _sub;
        private readonly string _key;
        private readonly Color _color;
        private readonly bool _suggested;

        public RatingButton(string label, string sub, string key, Color color, bool suggested)
        {
            _label = label;
            _sub = sub;
            _key = key;
            _color = color;
            _suggested = suggested;
            Height = S(62);
            Cursor = Cursors.Hand;
        }

        public int HeightFor(int width) => S(62);
        public override Size GetPreferredSize(Size proposedSize) => new(S(120), S(62));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.P.Bg);
            Gfx.Smooth(g);
            var p = Theme.P;
            float h = Math.Max(HoverAnim.Value, _suggested ? 0.55f : 0f);
            var rect = new RectangleF(0, 0, Width, Height);
            var fill = Gfx.Blend(p.Surface, Gfx.Blend(p.Surface, _color, p.Dark ? 0.3f : 0.16f), h);
            Gfx.FillRound(g, fill, rect, S(12));
            Gfx.DrawRound(g, Gfx.Blend(p.Border, _color, 0.35f + 0.65f * h), rect, S(12), _suggested ? Sf(2f) : Sf(1.2f));
            var top = new Rectangle(0, S(10), Width, S(24));
            Gfx.Text(g, _label, Fonts.UiSemibold(11f), Gfx.Blend(p.Text, _color, 0.5f), top, Gfx.Center);
            var sub = string.IsNullOrEmpty(_sub) ? _key : $"{_sub}  ·  {_key}";
            Gfx.Text(g, sub, Fonts.Ui(8.5f), p.Text3, new Rectangle(0, S(34), Width, S(18)), Gfx.Center);
        }
    }
}

/// <summary>Missed / Partly / Got it — self-grading for written answers (keys 1–3).</summary>
public sealed class SelfGradeBar : Row
{
    public event EventHandler<Result>? Graded;

    public SelfGradeBar(Result suggested)
    {
        Gap = S(10);
        var defs = new (Result R, string Label, string Glyph, Kind K)[]
        {
            (Result.Missed, T("Missed it") + "  (1)", Icons.Close, Kind.Secondary),
            (Result.Partial, T("Partly") + "  (2)", Icons.Edit, Kind.Secondary),
            (Result.GotIt, T("Got it") + "  (3)", Icons.Check, Kind.Secondary),
        };
        foreach (var (r, label, glyph, kind) in defs)
        {
            var color = r == Result.GotIt ? Theme.P.Success : r == Result.Partial ? Theme.P.Warning : Theme.P.Danger;
            var b = new ButtonEx(label, glyph, r == suggested ? Kind.Primary : kind, 52) { Tint = r == suggested ? color : null, FontPt = 10.5f }.Flex();
            b.Click += (_, _) => Graded?.Invoke(this, r);
            Controls.Add(b);
        }
    }
}
