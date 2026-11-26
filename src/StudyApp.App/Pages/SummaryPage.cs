using StudyApp.App.Dialogs;
using StudyApp.App.Shell;
using StudyApp.Data.Services;
using StudyApp.Domain.Prompts;

namespace StudyApp.App.Pages;

/// <summary>End of a session or test: score, time, missed items with page references, and what to do next.</summary>
public sealed class SummaryPage : PageBase
{
    private readonly SessionSummary _s;

    public override string NavKey => "review";
    public override bool FocusMode => true;
    public override bool KeepInHistory => false;

    public SummaryPage(SessionSummary summary)
    {
        _s = summary;
        var body = UseStandardLayout(1000);
        bool test = summary.Mode == SessionMode.Test;
        var done = new ButtonEx(T("Done"), Icons.Check, Kind.Secondary).WithTip(T("Back to where you started"), "Esc");
        done.Click += (_, _) => Host.Main?.GoBack();
        body.Controls.Add(PageHeader(test ? T("Test results") : T("Session complete"), summary.Title, false, done));

        // Hero
        var hero = new Card(26) { Gap = S(14) };
        var row = new Row { Gap = S(28), Align = VAlign.Center };
        var ring = new Ring(150)
        {
            Center = $"{summary.Score * 100:0}%",
            Caption = test ? T("score") : T("recalled"),
            Color = summary.Score >= 0.8 ? Theme.P.Success : summary.Score >= 0.6 ? Theme.P.Warning : Theme.P.Danger,
        };
        ring.AnimateTo((float)summary.Score);
        row.Controls.Add(ring);
        var info = new Column { Gap = S(10) }.Flex();
        info.Controls.Add(new TextLabel(Message(summary.Score), Fonts.Heading(15f), Theme.P.Text));
        var chips = new Row { Gap = S(8), Wrap = true };
        chips.Controls.Add(Chip.Status(F("{0} got it", summary.GotIt), Theme.P.Success, Icons.Check));
        if (summary.Partial > 0) chips.Controls.Add(Chip.Status(F("{0} partly", summary.Partial), Theme.P.Warning));
        chips.Controls.Add(Chip.Status(F("{0} missed", summary.Missed), Theme.P.Danger, Icons.Close));
        chips.Controls.Add(Chip.Neutral(L.Duration(summary.Duration), Icons.Timer));
        chips.Controls.Add(Chip.Neutral(N(summary.Total, "item", "items")));
        info.Controls.Add(chips);
        if (summary.TimedOut) info.Controls.Add(new TextLabel(T("Time ran out — unanswered questions count as missed."), Fonts.Ui(9.5f), Theme.P.Warning));
        var actions = new Row { Gap = S(10), Wrap = true };
        if (summary.MissedItems.Count > 0)
        {
            var reviewMissed = new ButtonEx(F("Review missed now ({0})", summary.MissedItems.Count), Icons.Refresh, Kind.Primary, 42).WithTip(T("Go through the items you missed again"), "Enter");
            reviewMissed.Click += (_, _) => StudyLauncher.ReviewItems(summary.MissedItems.Select(i => i.Id), T("Missed items"), asTest: test);
            actions.Controls.Add(reviewMissed);
        }
        var again = new ButtonEx(test ? T("New test") : T("Study more"), Icons.Play, summary.MissedItems.Count > 0 ? Kind.Secondary : Kind.Primary, 42);
        again.Click += (_, _) =>
        {
            var scope = summary.Options.Clone();
            scope.ItemIds.Clear();
            if (test) StudyLauncher.Test(scope);
            else StudyLauncher.StudyWithOptions(scope);
        };
        actions.Controls.Add(again);
        if (summary.MissedItems.Count > 0)
        {
            var harder = new ButtonEx(T("Get harder questions on these"), Icons.Chat, Kind.Ghost, 42).WithTip(T("Builds an AI prompt from the items you missed"));
            harder.Click += (_, _) => OpenWeakPrompt();
            actions.Controls.Add(harder);
        }
        info.Controls.Add(actions);
        row.Controls.Add(info);
        hero.Controls.Add(row);
        body.Controls.Add(hero);

        // Pages to re-read
        var pages = summary.PagesToReread;
        if (pages.Count > 0)
        {
            var card = new Card(20) { Gap = S(10) };
            var copy = new ButtonEx(T("Copy list"), Icons.Copy, Kind.Ghost, 32).OnClick(() =>
            {
                Clipboard.SetText(string.Join(", ", pages.Select(L.Page)));
                Toast.Show(T("Copied."), Tone.Success, seconds: 2);
            });
            var source = summary.MissedItems.Select(i => i.Source).FirstOrDefault(x => x.Length > 0);
            card.Controls.Add(new SectionHeader(T("Pages to re-read"), source != null ? F("In {0}", source) : T("Go back to these pages in your textbook."), copy));
            var flow = new Row { Gap = S(8), Wrap = true };
            foreach (var p in pages) flow.Controls.Add(Chip.PageRef(p));
            card.Controls.Add(flow);
            body.Controls.Add(card);
        }

        // Missed items
        var missed = summary.MissedEntries;
        if (missed.Count > 0)
        {
            body.Controls.Add(new SectionHeader(T("What to review"), T("Each item you missed, with the correct answer and where to find it.")));
            foreach (var e in missed) body.Controls.Add(MissedCard(e));
        }
        else
        {
            body.Controls.Add(new Banner(Tone.Success, T("Perfect — nothing to review!"), T("Everything was answered correctly. The schedule will show these items again later.")));
        }
    }

    private static string Message(double score) => score switch
    {
        >= 0.9 => T("Excellent work!"),
        >= 0.75 => T("Great job!"),
        >= 0.5 => T("Good effort — keep going."),
        _ => T("Every mistake is a lesson. Let's review them."),
    };

    private void OpenWeakPrompt()
    {
        var first = _s.MissedItems.FirstOrDefault();
        var o = Host.Settings.PromptOptions.Clone();
        if (first != null)
        {
            var subject = Host.Store.GetSubject(first.SubjectId);
            o.Subject = subject?.Name ?? first.SubjectName;
            o.Source = subject?.Source ?? first.Source;
            o.ChapterNumber = o.ChapterTitle = o.Pages = "";
        }
        o.WeakItems = PromptText.WeakItems(_s.MissedItems, L.IsBg);
        Go(() => new PromptsPage(PromptDefaults.Weak, o));
    }

    private static Control MissedCard(SessionEntry e)
    {
        var item = e.Item;
        var card = new Card(20) { Gap = S(10), AccentStripe = e.Result == Result.Partial ? Theme.P.Warning : Theme.P.Danger };
        var head = new Row { Gap = S(8), Wrap = true };
        head.Controls.Add(Chip.Status(e.Result == Result.Partial ? T("Partly") : T("Missed"), e.Result == Result.Partial ? Theme.P.Warning : Theme.P.Danger));
        head.Controls.Add(Chip.Neutral(ItemTypes.DisplayName(item.Type)));
        if (item.Page.Length > 0) head.Controls.Add(Chip.PageRef(item.Page));
        head.Controls.Add(new Spacer());
        var flag = ButtonEx.Icon(Icons.Flag, item.Flagged ? T("Remove flag") : T("Flag for later"), null, item.Flagged ? Kind.Soft : Kind.Ghost, 32);
        flag.Click += (_, _) =>
        {
            item.Flagged = !item.Flagged;
            Host.Store.SetFlagged(item.Id, item.Flagged);
            flag.Style = item.Flagged ? Kind.Soft : Kind.Ghost;
            flag.Invalidate();
        };
        var note = ButtonEx.Icon(Icons.Note, T("Personal note"), null, item.PersonalNote.Length > 0 ? Kind.Soft : Kind.Ghost, 32);
        note.Click += (_, _) =>
        {
            var text = NoteDialog.Show(item);
            if (text == null) return;
            item.PersonalNote = text;
            Host.Store.SetPersonalNote(item.Id, text);
            note.Style = text.Length > 0 ? Kind.Soft : Kind.Ghost;
            note.Invalidate();
        };
        var edit = ButtonEx.Icon(Icons.Edit, T("Edit item"), null, Kind.Ghost, 32);
        edit.Click += (_, _) => ItemEditorDialog.Edit(item);
        var ask = new ButtonEx(T("Ask AI to explain"), Icons.Chat, Kind.Ghost, 32).WithTip(T("Copies a prompt you can paste into your AI chat"));
        ask.Click += (_, _) =>
        {
            Clipboard.SetText(PromptText.Explain(item, e.GivenText, Host.Store.GetSubject(item.SubjectId), L.IsBg).Replace("\n", "\r\n"));
            Toast.Show(T("Prompt copied — paste it into your AI chat."), Tone.Success);
        };
        head.Controls.Add(ask);
        head.Controls.Add(flag);
        head.Controls.Add(note);
        head.Controls.Add(edit);
        card.Controls.Add(head);

        card.Controls.Add(new RichTextView(item.Prompt, RichStyle.Study(Host.Settings.AnswerFontSize + 1, Theme.P.Text)));
        if (e.GivenText.Trim().Length > 0)
            card.Controls.Add(new TextLabel(T("Your answer:") + " " + e.GivenText.Trim(), Fonts.Ui(10f), Theme.P.Danger));
        var correct = new Card(14) { Gap = S(4), Fill = Theme.P.SuccessSoft, Stroke = Theme.P.SuccessSoft };
        correct.Controls.Add(new TextLabel(T("Correct answer"), Fonts.UiSemibold(9f), Theme.P.Success));
        var answerText = item.Type is ItemTypes.Single or ItemTypes.Multiple
            ? string.Join("\n", (item.Data.Correct ?? new()).Where(i => i < (item.Data.Options?.Count ?? 0)).Select(i => "- " + item.Data.Options![i].Text))
            : item.CorrectAnswerText();
        correct.Controls.Add(new RichTextView(answerText, RichStyle.Study(Host.Settings.AnswerFontSize, Theme.P.Text)));
        card.Controls.Add(correct);
        if (item.Explanation.Length > 0)
            card.Controls.Add(new RichTextView(item.Explanation, RichStyle.Study(Host.Settings.AnswerFontSize * 0.95f, Theme.P.Text2)));
        return card;
    }

    public override bool HandleKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Host.Main?.GoBack(); return true; }
        if (keyData == Keys.Enter && _s.MissedItems.Count > 0)
        {
            StudyLauncher.ReviewItems(_s.MissedItems.Select(i => i.Id), T("Missed items"), asTest: _s.Mode == SessionMode.Test);
            return true;
        }
        return false;
    }
}
