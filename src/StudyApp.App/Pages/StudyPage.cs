using StudyApp.App.Dialogs;
using StudyApp.App.Shell;
using StudyApp.Data.Services;
using StudyApp.Domain.Grading;
using StudyApp.Domain.Sessions;
using StudyApp.Domain.Srs;

namespace StudyApp.App.Pages;

/// <summary>Fixed bar at the top or bottom of a session page; content is centred with a maximum width.</summary>
public sealed class SessionBar : Panel
{
    private Control? _content;
    private readonly bool _top;

    public SessionBar(bool top, int height)
    {
        _top = top;
        Dock = top ? DockStyle.Top : DockStyle.Bottom;
        Height = S(height);
        BackColor = top ? Theme.P.Bg : Theme.P.Surface;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    public int MaxContentWidth { get; set; } = S(980);

    public void SetContent(Control? content)
    {
        SuspendLayout();
        if (_content != null)
        {
            Controls.Remove(_content);
            _content.Dispose();
        }
        _content = content;
        if (content != null) Controls.Add(content);
        ResumeLayout(true);
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (_content == null) return;
        int w = Math.Min(Width - S(48), MaxContentWidth);
        int h = LayoutProps.HeightOf(_content, w);
        _content.SetBounds((Width - w) / 2, (Height - h) / 2, w, h);
        if (_content is LayoutPanel lp) lp.PerformLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(Theme.P.Border);
        if (_top) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        else e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }
}

/// <summary>
/// Flashcard / review session with spaced repetition. Flashcards flip; questions are answered and
/// checked; then the learner rates herself (Again / Hard / Good / Easy) which schedules the next review.
/// </summary>
public sealed class StudyPage : PageBase
{
    private readonly ReviewRunner _runner;
    private readonly SessionOptions _options;
    private readonly SessionBar _topBar = new(true, 76);
    private readonly SessionBar _bottomBar = new(false, 104);
    private readonly Column _stage = new() { Gap = S(16), Padding = Pad(28, 28, 28, 36) };
    private readonly ScrollHost _scroll;
    private readonly TextLabel _progressText;
    private readonly TextLabel _timer;
    private readonly ProgressBarEx _progress;
    private readonly ButtonEx _undo, _flag, _note, _edit, _wrong;
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };

    private Item? _item;
    private FlashcardView? _card;
    private QuestionView? _question;
    private Result _graded = Result.None;
    private bool _answered;
    private bool _finished;

    public override string NavKey => "review";
    public override bool FocusMode => true;
    public override bool KeepInHistory => false;

    public StudyPage(SessionOptions options, List<Item> items)
    {
        _options = options;
        _runner = new ReviewRunner(Host.Store, Host.Settings, options, items);
        _scroll = new ScrollHost(_stage) { Dock = DockStyle.Fill, MaxContentWidth = S(Math.Clamp(Host.Settings.ContentWidth, 520, 1400)), BackColor = Theme.P.Bg };
        _stage.BackColor = Theme.P.Bg;

        var end = ButtonEx.Icon(Icons.Close, T("End session"), "Esc", Kind.Ghost, 38);
        end.Click += (_, _) => EndSession();
        var titleCol = new Column { Gap = S(2) };
        titleCol.Controls.Add(new TextLabel(options.Title.Length > 0 ? options.Title : T("Study session"), Fonts.UiSemibold(10.5f), Theme.P.Text, wrap: false));
        _progressText = new TextLabel("", Fonts.Ui(9f), Theme.P.Text2, wrap: false);
        titleCol.Controls.Add(_progressText);
        titleCol.FixedWidth(S(280));
        _progress = new ProgressBarEx(0, 8).Flex();
        _timer = new TextLabel("0:00", Fonts.UiSemibold(9.5f), Theme.P.Text2, wrap: false).FixedWidth(S(56));
        _undo = ButtonEx.Icon(Icons.Undo, T("Undo last answer"), "Ctrl+Z");
        _undo.Click += (_, _) => Undo();
        _flag = ButtonEx.Icon(Icons.Flag, T("Flag for later"), "F");
        _flag.Click += (_, _) => ToggleFlag();
        _note = ButtonEx.Icon(Icons.Note, T("Personal note"), "N");
        _note.Click += (_, _) => EditNote();
        _edit = ButtonEx.Icon(Icons.Edit, T("Edit item"), "E");
        _edit.Click += (_, _) => EditItem();
        _wrong = ButtonEx.Icon(Icons.Bug, T("Mark as wrong in source"), "W");
        _wrong.Click += (_, _) => MarkWrong();
        var top = new Row { Gap = S(10) };
        top.Controls.Add(end);
        top.Controls.Add(titleCol);
        top.Controls.Add(_progress);
        top.Controls.Add(_timer);
        top.Controls.Add(_undo);
        top.Controls.Add(_flag);
        top.Controls.Add(_note);
        top.Controls.Add(_edit);
        top.Controls.Add(_wrong);
        _topBar.MaxContentWidth = S(1180);
        _topBar.SetContent(top);

        Controls.Add(_scroll);
        Controls.Add(_bottomBar);
        Controls.Add(_topBar);

        _clock.Tick += (_, _) => UpdateTimer();
        _clock.Start();
        Next();
    }

    public override void OnDeactivated()
    {
        _clock.Stop();
        _clock.Dispose();
    }

    private void UpdateTimer()
    {
        var t = _runner.Elapsed;
        if (_options.TimeLimitMinutes > 0)
        {
            var left = TimeSpan.FromMinutes(_options.TimeLimitMinutes) - t;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            _timer.Text = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
            _timer.ForeColor = left.TotalMinutes < 1 ? Theme.P.Danger : Theme.P.Text2;
        }
        else _timer.Text = $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        _timer.Invalidate();
    }

    private void UpdateProgress()
    {
        int done = _runner.Answered;
        int total = done + _runner.Remaining;
        _progress.Value = total == 0 ? 0 : (float)done / total;
        _progressText.Text = F("{0} answered · {1} left", done, _runner.Remaining);
        _progressText.Invalidate();
        _undo.Enabled = _runner.CanUndo;
        if (_item != null)
        {
            _flag.Style = _item.Flagged ? Kind.Soft : Kind.Ghost;
            _flag.Tint = null;
            _note.Style = _item.PersonalNote.Length > 0 ? Kind.Soft : Kind.Ghost;
            _flag.Invalidate();
            _note.Invalidate();
        }
    }

    // ── flow ─────────────────────────────────────────────────────────

    private void Next()
    {
        var item = _runner.Next();
        if (item == null)
        {
            Finish();
            return;
        }
        Show(item);
    }

    private void Show(Item item)
    {
        _item = item;
        _answered = false;
        _graded = Result.None;
        _stage.SuspendLayout();
        foreach (Control c in _stage.Controls.Cast<Control>().ToList()) { _stage.Controls.Remove(c); c.Dispose(); }
        _card = null;
        _question = null;

        if (item.Type == ItemTypes.Flashcard)
        {
            _card = new FlashcardView(item);
            _card.RevealRequested += (_, _) => Reveal();
            _stage.Controls.Add(_card);
            var show = new ButtonEx(T("Show answer"), Icons.Eye, Kind.Primary, 52) { FontPt = 11.5f }.WithTip(T("Show answer"), T("Space"));
            show.FixedWidth(S(320));
            show.Click += (_, _) => Reveal();
            _bottomBar.SetContent(Centered(show));
        }
        else
        {
            var order = Enumerable.Range(0, item.Data.Options?.Count ?? 0).ToArray();
            if (_options.ShuffleChoices && ItemTypes.IsChoice(item.Type)) order = order.OrderBy(_ => Random.Shared.Next()).ToArray();
            var card = new Card(28) { Gap = S(16) };
            _question = new QuestionView(item, order, new Answer(), autoSubmit: true);
            _question.Answered += (_, _) => Check();
            _question.SubmitRequested += (_, _) => Check();
            card.Controls.Add(_question);
            _stage.Controls.Add(card);
            if (_question.IsTyped || item.Type == ItemTypes.Multiple)
            {
                var check = new ButtonEx(_question.IsTyped ? T("Show answer") : T("Check answer"), Icons.Check, Kind.Primary, 52) { FontPt = 11.5f }.WithTip(T("Check answer"), "Enter");
                check.FixedWidth(S(320));
                check.Click += (_, _) => Check();
                _bottomBar.SetContent(Centered(check));
            }
            else
            {
                _bottomBar.SetContent(Centered(new TextLabel(item.Type == ItemTypes.TrueFalse ? T("Choose True or False (T / F)") : T("Choose an answer (1–9 or A–H)"), Fonts.Ui(10f), Theme.P.Text2, wrap: false)));
            }
        }

        if (item.ChapterTitle.Length > 0 || item.DeckTitle.Length > 0)
            _stage.Controls.Add(new CenteredText(string.Join("  ›  ", new[] { item.SubjectName, item.ChapterTitle, item.DeckTitle }.Where(x => x.Length > 0)), Fonts.Ui(8.5f), Theme.P.Text3));

        _stage.ResumeLayout(true);
        _scroll.ScrollToTop();
        _scroll.PerformLayout();
        UpdateProgress();
        _question?.FocusInput();
    }

    private static Control Centered(Control c)
    {
        var row = new Row { Gap = S(8) };
        row.Controls.Add(new Spacer());
        row.Controls.Add(c);
        row.Controls.Add(new Spacer());
        return row;
    }

    private void Reveal()
    {
        if (_card == null || _card.Revealed || _item == null) return;
        _card.Reveal();
        _answered = true;
        ShowRating(null);
    }

    private void Check()
    {
        if (_question == null || _item == null || _answered) return;
        _answered = true;
        if (_question.IsTyped)
        {
            var check = Grader.CheckShort(_question.Answer.Text, Grader.AcceptedAnswers(_item), Host.Settings.ShortAnswerStrictness);
            _question.ShowFeedback(Result.None, check);
            _graded = Result.None;
            var suggested = _question.Answer.Text.Trim().Length == 0 ? (Rating?)null : Sm2.RatingFor(check.Suggested);
            ShowRating(suggested, new[] { T("Missed"), T("Partly"), T("Got it"), T("Easy") });
        }
        else
        {
            _graded = Grader.Grade(_item, _question.Answer, Host.Settings.ShortAnswerStrictness);
            _question.ShowFeedback(_graded);
            ShowRating(Sm2.RatingFor(_graded));
        }
        LayoutProps.Relayout(_stage);
    }

    private void ShowRating(Rating? suggested, string[]? labels = null)
    {
        if (_item == null) return;
        var bar = new RatingBar(_item, suggested, labels);
        bar.Rated += (_, r) => Rate(r);
        _suggested = suggested;
        _bottomBar.SetContent(bar);
    }

    private Rating? _suggested;

    private void Rate(Rating rating)
    {
        if (_item == null || !_answered) return;
        var result = _graded != Result.None && rating != Rating.Again
            ? _graded
            : rating switch { Rating.Again => Result.Missed, Rating.Hard => Result.Partial, _ => Result.GotIt };
        if (_graded == Result.GotIt && rating == Rating.Again) result = Result.Missed;
        _runner.Answer(rating, result, _question?.Answer.Text ?? "");
        Host.Main?.RefreshSidebar();
        Next();
    }

    private void Undo()
    {
        var item = _runner.Undo();
        if (item != null) Show(item);
    }

    private void ToggleFlag()
    {
        if (_item == null) return;
        _item.Flagged = !_item.Flagged;
        Host.Store.SetFlagged(_item.Id, _item.Flagged);
        Toast.Show(_item.Flagged ? T("Flagged — find it later on the Review page.") : T("Flag removed."), Tone.Info, seconds: 2);
        UpdateProgress();
    }

    private void EditNote()
    {
        if (_item == null) return;
        var note = NoteDialog.Show(_item);
        if (note == null) return;
        _item.PersonalNote = note;
        Host.Store.SetPersonalNote(_item.Id, note);
        Refresh(_item);
    }

    private void EditItem()
    {
        if (_item == null) return;
        if (!ItemEditorDialog.Edit(_item)) return;
        if (_item.WrongInSource) { SkipCurrent(); return; }
        Refresh(_item);
    }

    private void Refresh(Item item)
    {
        bool revealed = _card?.Revealed == true;
        bool answered = _answered;
        if (_card != null && !answered)
        {
            _card.SetItem(item);
            UpdateProgress();
            return;
        }
        Show(item);
        if (revealed) Reveal();
    }

    private void MarkWrong()
    {
        if (_item == null) return;
        var item = _item;
        Host.Store.SetWrongInSource(item.Id, true);
        Toast.Show(T("Marked as wrong in source. It is skipped until you fix it (Review → Wrong in source)."), Tone.Warning, T("Undo"),
            () => Host.Store.SetWrongInSource(item.Id, false));
        SkipCurrent();
    }

    private void SkipCurrent()
    {
        _item = null;
        Next();
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        _clock.Stop();
        var summary = _runner.Finish();
        Host.Main?.RefreshSidebar();
        if (summary.Total == 0)
        {
            Host.Main?.GoBack();
            return;
        }
        Host.Main?.Replace(() => new SummaryPage(summary));
    }

    private void EndSession()
    {
        if (_runner.Answered == 0)
        {
            _finished = true;
            _runner.Finish();
            Host.Main?.GoBack();
            return;
        }
        Finish();
    }

    public override bool CanLeave()
    {
        if (_finished || _runner.Answered == 0 || !Host.Settings.ConfirmLeaveSession) return true;
        if (!Ask.Confirm(T("Leave this session?"), T("Your answers so far are saved."), T("Leave"), cancelText: T("Keep studying"))) return false;
        _finished = true;
        _runner.Finish();
        return true;
    }

    public override bool HandleKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: EndSession(); return true;
            case Keys.Control | Keys.Z: Undo(); return true;
        }
        if (_item == null) return false;

        if (!_answered)
        {
            if (_card != null && keyData is Keys.Space or Keys.Enter) { Reveal(); return true; }
            if (_card != null && keyData == Keys.H) { _card.ToggleHint(); return true; }
            if (_question != null)
            {
                if (keyData is Keys.Enter && (_question.IsTyped || _question.HasAnswer)) { Check(); return true; }
                if (_question.HandleKey(keyData)) return true;
            }
        }
        else
        {
            switch (keyData)
            {
                case Keys.D1 or Keys.NumPad1: Rate(Rating.Again); return true;
                case Keys.D2 or Keys.NumPad2: Rate(Rating.Hard); return true;
                case Keys.D3 or Keys.NumPad3: Rate(Rating.Good); return true;
                case Keys.D4 or Keys.NumPad4: Rate(Rating.Easy); return true;
                case Keys.Space or Keys.Enter: Rate(_suggested ?? Rating.Good); return true;
            }
        }

        switch (keyData)
        {
            case Keys.F: ToggleFlag(); return true;
            case Keys.N: EditNote(); return true;
            case Keys.E: EditItem(); return true;
            case Keys.W: MarkWrong(); return true;
        }
        return false;
    }
}
