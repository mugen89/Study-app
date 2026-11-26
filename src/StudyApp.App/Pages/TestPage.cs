using StudyApp.App.Dialogs;
using StudyApp.App.Shell;
using StudyApp.Data.Services;
using StudyApp.Domain.Grading;
using StudyApp.Domain.Sessions;

namespace StudyApp.App.Pages;

/// <summary>A test: one question at a time, free navigation, optional timer, feedback now or at the end.</summary>
public sealed class TestPage : PageBase
{
    private readonly TestRunner _runner;
    private readonly SessionOptions _o;
    private readonly SessionBar _topBar = new(true, 76);
    private readonly SessionBar _bottomBar = new(false, 92);
    private readonly Column _stage = new() { Gap = S(16), Padding = Pad(28, 24, 28, 36) };
    private readonly ScrollHost _scroll;
    private readonly TextLabel _progressText;
    private readonly TextLabel _timer;
    private readonly ProgressBarEx _progress;
    private readonly ButtonEx _flag;
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 500 };
    private QuestionPalette? _palette;
    private bool _showPalette;
    private QuestionView? _view;
    private bool _finished;
    private bool _awaitingSelfGrade;

    public override string NavKey => "review";
    public override bool FocusMode => true;
    public override bool KeepInHistory => false;

    public TestPage(SessionOptions options, List<Item> items)
    {
        _o = options;
        _runner = new TestRunner(Host.Store, Host.Settings, options, items);
        _scroll = new ScrollHost(_stage) { Dock = DockStyle.Fill, MaxContentWidth = S(Math.Clamp(Host.Settings.ContentWidth, 520, 1400)), BackColor = Theme.P.Bg };
        _stage.BackColor = Theme.P.Bg;

        var end = ButtonEx.Icon(Icons.Close, T("End test"), "Esc", Kind.Ghost, 38);
        end.Click += (_, _) => FinishRequested();
        var titleCol = new Column { Gap = S(2) };
        titleCol.Controls.Add(new TextLabel(options.Title.Length > 0 ? options.Title : T("Test"), Fonts.UiSemibold(10.5f), Theme.P.Text, wrap: false));
        _progressText = new TextLabel("", Fonts.Ui(9f), Theme.P.Text2, wrap: false);
        titleCol.Controls.Add(_progressText);
        titleCol.FixedWidth(S(280));
        _progress = new ProgressBarEx(0, 8).Flex();
        _timer = new TextLabel("0:00", Fonts.UiSemibold(10f), Theme.P.Text2, wrap: false).FixedWidth(S(64));
        var palette = ButtonEx.Icon(Icons.Grid, T("All questions"), "P");
        palette.Click += (_, _) => TogglePalette();
        _flag = ButtonEx.Icon(Icons.Flag, T("Mark this question to come back to"), "M");
        _flag.Click += (_, _) => ToggleMark();
        var top = new Row { Gap = S(10) };
        top.Controls.Add(end);
        top.Controls.Add(titleCol);
        top.Controls.Add(_progress);
        top.Controls.Add(_timer);
        top.Controls.Add(palette);
        top.Controls.Add(_flag);
        _topBar.MaxContentWidth = S(1180);
        _topBar.SetContent(top);

        Controls.Add(_scroll);
        Controls.Add(_bottomBar);
        Controls.Add(_topBar);

        _clock.Tick += (_, _) => Tick();
        _clock.Start();
        ShowQuestion(0);
    }

    public override void OnDeactivated()
    {
        _clock.Stop();
        _clock.Dispose();
    }

    private void Tick()
    {
        if (_runner.Remaining is { } left)
        {
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            _timer.Text = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
            _timer.ForeColor = left.TotalMinutes < 1 ? Theme.P.Danger : left.TotalMinutes < 5 ? Theme.P.Warning : Theme.P.Text2;
            if (_runner.TimeUp && !_finished)
            {
                _clock.Stop();
                Toast.Show(T("Time is up!"), Tone.Warning);
                Finish(timedOut: true);
                return;
            }
        }
        else
        {
            var t = _runner.Elapsed;
            _timer.Text = $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        }
        _timer.Invalidate();
    }

    private void UpdateHeader()
    {
        int answered = _runner.AnsweredCount;
        _progress.Value = _runner.Questions.Count == 0 ? 0 : (float)answered / _runner.Questions.Count;
        _progressText.Text = F("Question {0} of {1} · {2} answered", _runner.Index + 1, _runner.Questions.Count, answered);
        _progressText.Invalidate();
        _flag.Style = _runner.Current.Flagged ? Kind.Soft : Kind.Ghost;
        _flag.Invalidate();
        _palette?.Invalidate();
    }

    private void ShowQuestion(int index)
    {
        _runner.GoTo(index);
        var q = _runner.Current;
        _awaitingSelfGrade = false;
        _stage.SuspendLayout();
        foreach (Control c in _stage.Controls.Cast<Control>().ToList()) { _stage.Controls.Remove(c); c.Dispose(); }
        _palette = null;
        if (_showPalette)
        {
            _palette = new QuestionPalette(_runner, ShowQuestion);
            _stage.Controls.Add(_palette);
        }

        var card = new Card(28) { Gap = S(16) };
        var head = new Row { Gap = S(10) };
        head.Controls.Add(new TextLabel(F("Question {0}", index + 1), Fonts.UiSemibold(10f), Theme.P.AccentText, wrap: false));
        head.Controls.Add(new Spacer());
        if (q.Flagged) head.Controls.Add(Chip.Status(T("Marked"), Theme.P.Warning, Icons.Flag));
        card.Controls.Add(head);

        _view = new QuestionView(q.Item, q.OptionOrder, q.Answer, autoSubmit: _runner.Immediate && !q.Submitted);
        _view.Answered += (_, _) => Submit();
        _view.SubmitRequested += (_, _) =>
        {
            if (_runner.Immediate) Submit();
            else Next();
        };
        _view.SelectionChanged += (_, _) => UpdateHeader();
        card.Controls.Add(_view);
        _stage.Controls.Add(card);
        _stage.ResumeLayout(true);

        if (q.Submitted)
        {
            if (q.Item.Type is ItemTypes.Short or ItemTypes.Flashcard) _view.ShowFeedback(q.Result, _runner.CheckTyped(q));
            else _view.ShowFeedback(q.Result);
        }
        _scroll.ScrollToTop();
        _scroll.PerformLayout();
        BuildBottom();
        UpdateHeader();
        _view.FocusInput();
    }

    private void BuildBottom()
    {
        var q = _runner.Current;
        var row = new Row { Gap = S(10) };
        var prev = new ButtonEx(T("Previous"), Icons.ChevronLeft, Kind.Secondary, 46).WithTip(T("Previous question"), "←");
        prev.Enabled = _runner.Index > 0;
        prev.Click += (_, _) => ShowQuestion(_runner.Index - 1);
        row.Controls.Add(prev);
        row.Controls.Add(new Spacer());

        bool last = _runner.Index == _runner.Questions.Count - 1;
        if (_runner.Immediate && !q.Submitted)
        {
            if (_awaitingSelfGrade)
            {
                var grade = new SelfGradeBar(_runner.CheckTyped(q).Suggested).Also(b => b.Width = S(560));
                grade.Graded += (_, r) => ApplySelfGrade(r);
                row.Controls.Add(grade);
            }
            else if (_view!.IsTyped || q.Item.Type == ItemTypes.Multiple)
            {
                var check = new ButtonEx(_view.IsTyped ? T("Show answer") : T("Check answer"), Icons.Check, Kind.Primary, 46).WithTip(T("Check answer"), "Enter");
                check.Click += (_, _) => Submit();
                row.Controls.Add(check);
            }
            else row.Controls.Add(new TextLabel(T("Choose an answer"), Fonts.Ui(10f), Theme.P.Text2, wrap: false));
            row.Controls.Add(new Spacer());
            var skip = new ButtonEx(last ? T("Finish test") : T("Skip"), last ? Icons.CheckCircle : Icons.ChevronRight, Kind.Ghost, 46);
            skip.Click += (_, _) => { if (last) FinishRequested(); else Next(); };
            row.Controls.Add(skip);
        }
        else
        {
            var finish = new ButtonEx(T("Finish test"), Icons.CheckCircle, last ? Kind.Primary : Kind.Secondary, 46).WithTip(T("Finish the test"), "Ctrl+Enter");
            finish.Click += (_, _) => FinishRequested();
            row.Controls.Add(finish);
            if (!last)
            {
                var next = new ButtonEx(T("Next"), Icons.ChevronRight, Kind.Primary, 46).WithTip(T("Next question"), "→ / Enter");
                next.Click += (_, _) => Next();
                row.Controls.Add(next);
            }
        }
        _bottomBar.SetContent(row);
    }

    private void Next()
    {
        if (_runner.Index < _runner.Questions.Count - 1) ShowQuestion(_runner.Index + 1);
        else FinishRequested();
    }

    /// <summary>Immediate feedback: grade the current question.</summary>
    private void Submit()
    {
        var q = _runner.Current;
        if (!_runner.Immediate || q.Submitted || _view == null) return;
        if (_view.IsTyped)
        {
            _view.ShowFeedback(Result.None, _runner.CheckTyped(q));
            _awaitingSelfGrade = true;
            BuildBottom();
            return;
        }
        if (q.Answer.IsEmpty) return;
        var result = _runner.Submit();
        _view.ShowFeedback(result);
        BuildBottom();
        UpdateHeader();
        LayoutProps.Relayout(_stage);
    }

    private void ApplySelfGrade(Result result)
    {
        if (!_awaitingSelfGrade) return;
        _awaitingSelfGrade = false;
        _runner.Submit(result);
        BuildBottom();
        UpdateHeader();
    }

    private void ToggleMark()
    {
        _runner.Current.Flagged = !_runner.Current.Flagged;
        ShowQuestion(_runner.Index);
    }

    private void TogglePalette()
    {
        _showPalette = !_showPalette;
        ShowQuestion(_runner.Index);
    }

    private void FinishRequested()
    {
        if (_finished) return;
        int open = _runner.Questions.Count(q => !q.Submitted && q.Answer.IsEmpty);
        var message = open > 0
            ? F("{0} not answered yet — they will count as missed.", N(open, "question", "questions"))
            : T("You answered every question.");
        if (!Ask.Confirm(T("Finish the test?"), message, T("Finish test"), cancelText: T("Keep going"))) return;
        Finish(timedOut: false);
    }

    private void Finish(bool timedOut)
    {
        if (_finished) return;
        var toGrade = _runner.NeedSelfGrade().Where(q => !q.Answer.IsEmpty || q.Answer.Text.Length > 0).ToList();
        if (toGrade.Count > 0) SelfGradeDialog.Show(toGrade, _runner);
        _finished = true;
        _clock.Stop();
        var summary = _runner.Finish(timedOut);
        Host.Main?.RefreshSidebar();
        Host.Main?.Replace(() => new SummaryPage(summary));
    }

    public override bool CanLeave()
    {
        if (_finished || !Host.Settings.ConfirmLeaveSession) return true;
        if (!Ask.Confirm(T("Leave the test?"), T("Your answers so far will be graded and saved."), T("Leave"), cancelText: T("Keep going"))) return false;
        _finished = true;
        _runner.Finish();
        return true;
    }

    public override bool HandleKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: FinishRequested(); return true;
            case Keys.Control | Keys.Enter: FinishRequested(); return true;
            case Keys.Left: if (_runner.Index > 0) ShowQuestion(_runner.Index - 1); return true;
            case Keys.Right: Next(); return true;
            case Keys.P: TogglePalette(); return true;
            case Keys.M: ToggleMark(); return true;
        }
        var q = _runner.Current;
        if (_awaitingSelfGrade)
        {
            if (keyData is Keys.D1 or Keys.NumPad1) { ApplySelfGrade(Result.Missed); return true; }
            if (keyData is Keys.D2 or Keys.NumPad2) { ApplySelfGrade(Result.Partial); return true; }
            if (keyData is Keys.D3 or Keys.NumPad3) { ApplySelfGrade(Result.GotIt); return true; }
        }
        if (keyData is Keys.Enter or Keys.Space)
        {
            if (_runner.Immediate && !q.Submitted && !_awaitingSelfGrade)
            {
                if (_view?.IsTyped == true || q.Item.Type == ItemTypes.Multiple || !q.Answer.IsEmpty) Submit();
                return true;
            }
            if (!_awaitingSelfGrade) Next();
            return true;
        }
        if (!q.Submitted && _view?.HandleKey(keyData) == true) return true;
        return false;
    }

    /// <summary>Grid of question numbers to jump around; shows answered/marked/result.</summary>
    private sealed class QuestionPalette : UiControl, IHeightForWidth
    {
        private readonly TestRunner _runner;
        private readonly Action<int> _go;
        private int _hover = -1;

        public QuestionPalette(TestRunner runner, Action<int> go)
        {
            _runner = runner;
            _go = go;
            Cursor = Cursors.Hand;
        }

        private int Cell => S(42);
        private int PerRow(int w) => Math.Max(1, (w - S(32)) / Cell);

        public int HeightFor(int width) => S(32) + (_runner.Questions.Count + PerRow(width) - 1) / PerRow(width) * Cell + S(28);

        private Rectangle CellRect(int i)
        {
            int per = PerRow(Width);
            return new Rectangle(S(16) + i % per * Cell, S(16) + i / per * Cell, Cell - S(6), Cell - S(6));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < _runner.Questions.Count; i++) if (CellRect(i).Contains(e.Location)) h = i;
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (int i = 0; i < _runner.Questions.Count; i++) if (CellRect(i).Contains(e.Location)) _go(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.P.Bg);
            Gfx.Smooth(g);
            var p = Theme.P;
            Gfx.FillRound(g, p.Surface, new RectangleF(0, 0, Width, Height), S(12));
            Gfx.DrawRound(g, p.Border, new RectangleF(0, 0, Width, Height), S(12));
            for (int i = 0; i < _runner.Questions.Count; i++)
            {
                var q = _runner.Questions[i];
                var r = CellRect(i);
                Color fill = p.Surface2, fore = p.Text2;
                if (q.Submitted && _runner.Immediate)
                {
                    var c = q.Result == Result.GotIt ? p.Success : q.Result == Result.Partial ? p.Warning : p.Danger;
                    fill = Gfx.Blend(p.Surface, c, p.Dark ? 0.3f : 0.18f);
                    fore = c;
                }
                else if (!q.Answer.IsEmpty)
                {
                    fill = p.AccentSoft;
                    fore = p.AccentText;
                }
                if (i == _hover) fill = Gfx.Blend(fill, p.Text, 0.06f);
                Gfx.FillRound(g, fill, r, S(8));
                if (i == _runner.Index) Gfx.DrawRound(g, p.Accent, r, S(8), Sf(2));
                Gfx.Text(g, (i + 1).ToString(), Fonts.UiSemibold(9.5f), fore, r, Gfx.Center);
                if (q.Flagged) Gfx.Circle(g, p.Warning, new RectangleF(r.Right - S(9), r.Y + S(3), S(6), S(6)));
            }
            Gfx.Text(g, T("Click a number to jump to that question. Orange dot = marked."), Fonts.Ui(8.5f), p.Text3,
                new Rectangle(S(16), Height - S(26), Width - S(32), S(18)), Gfx.Left);
        }
    }
}

/// <summary>Before showing exam results: the learner grades her written answers against the model answers.</summary>
public static class SelfGradeDialog
{
    public static void Show(List<TestQuestion> questions, TestRunner runner)
    {
        using var d = new DialogBase(T("Check your written answers"), 760,
            T("Compare each answer with the model answer and choose how well you did."));
        var choices = new List<(TestQuestion Q, Segmented S)>();
        foreach (var q in questions)
        {
            var card = new Card(18) { Gap = S(8) };
            card.Controls.Add(new RichTextView(q.Item.Prompt, RichStyle.Study(Host.Settings.AnswerFontSize, Theme.P.Text)));
            card.Controls.Add(new TextLabel(T("Your answer:") + " " + (q.Answer.Text.Trim().Length > 0 ? q.Answer.Text.Trim() : T("(empty)")), Fonts.Ui(10f), Theme.P.Text2));
            var model = new Card(14) { Gap = S(6), Fill = Theme.P.SuccessSoft, Stroke = Theme.P.SuccessSoft };
            model.Controls.Add(new TextLabel(T("Model answer"), Fonts.UiSemibold(9f), Theme.P.Success));
            model.Controls.Add(new RichTextView(q.Item.Answer + (q.Item.Data.KeyPoints is { Count: > 0 } kp ? "\n\n" + string.Join("\n", kp.Select(k => "- " + k)) : ""),
                RichStyle.Study(Host.Settings.AnswerFontSize * 0.95f, Theme.P.Text)));
            card.Controls.Add(model);
            var suggested = runner.CheckTyped(q).Suggested;
            var seg = new Segmented(suggested switch { Result.GotIt => 2, Result.Partial => 1, _ => 0 }, T("Missed it"), T("Partly"), T("Got it"));
            card.Controls.Add(seg.Align(HAlign.Left));
            choices.Add((q, seg));
            d.Content.Controls.Add(card);
        }
        var ok = d.AddButton(T("Show results"), Kind.Primary, DialogResult.OK);
        d.AcceptButton = ok;
        d.ShowDialog(Form.ActiveForm ?? Host.Main);
        foreach (var (q, seg) in choices)
            q.Answer.SelfGrade = seg.SelectedIndex switch { 2 => Result.GotIt, 1 => Result.Partial, _ => Result.Missed };
    }
}
