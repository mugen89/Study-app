using StudyApp.App.Dialogs;
using StudyApp.App.Shell;
using StudyApp.Data.Services;
using StudyApp.Domain.Import;
using StudyApp.Domain.Samples;

namespace StudyApp.App.Pages;

/// <summary>Dashboard: today's work, streak, activity, recent subjects and weak spots.</summary>
public sealed class HomePage : PageBase
{
    public override string NavKey => "home";

    public HomePage()
    {
        var body = UseStandardLayout();
        var subjects = Host.Store.GetSubjects(includeArchived: false);

        var hour = DateTime.Now.Hour;
        var greeting = hour < 12 ? T("Good morning") : hour < 18 ? T("Good afternoon") : T("Good evening");
        if (!string.IsNullOrWhiteSpace(Host.Settings.UserName)) greeting += ", " + Host.Settings.UserName.Trim();
        var today = DateTime.Now;
        var subtitle = char.ToUpper(L.Weekday(today)[0], L.Culture) + L.Weekday(today)[1..] + ", " + L.Date(today);

        var import = new ButtonEx(T("Import"), Icons.Import, Kind.Secondary).WithTip(T("Import JSON from your AI chat"), "Ctrl+I");
        import.Click += (_, _) => Go(() => new ImportPage());
        var newSubject = new ButtonEx(T("New subject"), Icons.Add, Kind.Primary).WithTip(T("Create a subject"), "Ctrl+N");
        newSubject.Click += (_, _) => SubjectsPage.CreateSubject();
        body.Controls.Add(PageHeader(greeting, subtitle, false, import, newSubject));

        if (subjects.Count == 0)
        {
            body.Controls.Add(Onboarding());
            return;
        }

        var summaries = Host.Store.SubjectSummaries();
        int due = Host.Store.CountDueToday();
        int answeredToday = Host.Store.CountAnswersToday();
        int streak = Host.Store.Streak();
        var (answers7, accuracy7) = Host.Store.RecentAccuracy(7);
        int goal = Math.Max(1, Host.Settings.DailyGoal);

        // Stat tiles
        var tiles = new CardGrid { MinCellWidth = S(220), Gap = S(16), MaxColumns = 4 };
        tiles.Controls.Add(new StatTile(Icons.Review, due.ToString("N0", L.Culture), T("Due today"), due == 0 ? T("Nothing waiting – nice!") : T("Scheduled for review today"), Theme.P.Accent));
        tiles.Controls.Add(new StatTile(Icons.Bolt, N(streak, "day", "days"), T("Study streak"), streak == 0 ? T("Study today to start a streak") : T("Keep it going!"), Theme.P.Warning));
        tiles.Controls.Add(new StatTile(Icons.CheckCircle, $"{answeredToday} / {goal}", T("Answered today"), answeredToday >= goal ? T("Daily goal reached!") : F("{0} to go for your daily goal", goal - answeredToday), Theme.P.Success));
        tiles.Controls.Add(new StatTile(Icons.Chart, answers7 == 0 ? "–" : $"{accuracy7 * 100:0}%", T("Accuracy (7 days)"), answers7 == 0 ? T("No answers this week yet") : N(answers7, "answer", "answers"), Theme.P.Info));
        body.Controls.Add(tiles);

        // Hero: today's review
        var hero = new Card(24) { Gap = S(12), AccentStripe = Theme.P.Accent };
        if (due > 0)
        {
            hero.Controls.Add(new TextLabel(T("Ready for today's review?"), Fonts.Heading(15f), Theme.P.Text));
            int subjectsWithDue = summaries.Values.Count(s => s.DueToday > 0);
            hero.Controls.Add(new TextLabel(F("{0} waiting in {1}. Reviewing at the right moment is the fastest way to remember for good.",
                N(due, "item", "items"), N(subjectsWithDue, "subject", "subjects")), Fonts.Ui(10.5f), Theme.P.Text2));
            var start = new ButtonEx(F("Start review ({0})", due), Icons.Play, Kind.Primary, 44) { FontPt = 11f }.WithTip(T("Study everything that is due"), "Enter");
            start.Width = start.GetPreferredSize(Size.Empty).Width;
            start.Click += (_, _) => StudyLauncher.QuickStudy(new());
            hero.Controls.Add(Ui.Row(10, start).Also(r => r.Margin = new Padding(0, S(4), 0, 0)));
        }
        else if (summaries.Values.Sum(x => x.New) is var fresh && fresh > 0 && Host.Store.CountNewToday() < Host.Settings.NewPerDay * Math.Max(1, summaries.Count))
        {
            hero.Controls.Add(new TextLabel(T("Time to learn something new"), Fonts.Heading(15f), Theme.P.Text));
            hero.Controls.Add(new TextLabel(F("Nothing is due for review. {0} you have never studied are waiting — a few new ones every day adds up fast.", N(fresh, "item", "items")), Fonts.Ui(10.5f), Theme.P.Text2));
            var learn = new ButtonEx(T("Start learning"), Icons.Play, Kind.Primary, 44) { FontPt = 11f }.WithTip(T("New items, up to your daily limit"), "Enter");
            learn.Click += (_, _) => StudyLauncher.QuickStudy(new());
            var test = new ButtonEx(T("Take a test"), Icons.CheckCircle, Kind.Secondary, 44).OnClick(() => StudyLauncher.Test(new()));
            hero.Controls.Add(Ui.Row(10, learn, test).Also(r => r.Margin = new Padding(0, S(4), 0, 0)));
        }
        else
        {
            hero.Controls.Add(new TextLabel(T("All caught up for today"), Fonts.Heading(15f), Theme.P.Text));
            hero.Controls.Add(new TextLabel(T("Nothing is due. You can learn new items, take a practice test, or read your notes."), Fonts.Ui(10.5f), Theme.P.Text2));
            var practice = new ButtonEx(T("Practise"), Icons.Layers, Kind.Primary).OnClick(() => StudyLauncher.StudyWithOptions(new()));
            var test = new ButtonEx(T("Take a test"), Icons.CheckCircle, Kind.Secondary).OnClick(() => StudyLauncher.Test(new()));
            hero.Controls.Add(Ui.Row(10, practice, test));
        }

        // Continue studying
        var recent = new Card(20) { Gap = S(10) };
        recent.Controls.Add(new SectionHeader(T("Continue studying"), null,
            new ButtonEx(T("All subjects"), Icons.ChevronRight, Kind.Ghost, 32).OnClick(() => Go(() => new SubjectsPage()))));
        foreach (var s in subjects.OrderByDescending(s => s.LastOpenedAt ?? s.CreatedAt).Take(4))
            recent.Controls.Add(new SubjectLine(s, summaries.GetValueOrDefault(s.Id) ?? new SubjectSummary { SubjectId = s.Id }));

        // Activity
        var activity = new Card(20) { Gap = S(10) };
        activity.Controls.Add(new SectionHeader(T("Activity"), streak > 0 ? F("{0} in a row", N(streak, "day", "days")) : null));
        activity.Controls.Add(new Heatmap(Host.Store.Activity(7 * 18), 18));

        var left = Ui.Stack(18, hero, recent);
        body.Controls.Add(new TwoColumns(left, activity, 0.56f) { StackBelow = S(900) });

        // Weak spots
        var weak = Host.Store.WeakItems(null, 5);
        if (weak.Count > 0)
        {
            var weakCard = new Card(20) { Gap = S(10) };
            var practiceWeak = new ButtonEx(T("Practise these"), Icons.Play, Kind.Soft, 34).OnClick(() =>
                StudyLauncher.ReviewItems(Host.Store.WeakItems(null, 30).Select(i => i.Id), T("Weak spots")));
            var harder = new ButtonEx(T("Get harder questions"), Icons.Chat, Kind.Ghost, 34).WithTip(T("Opens the \"weak topics\" prompt for your AI chat"));
            harder.Click += (_, _) => Go(() => new PromptsPage(Domain.Prompts.PromptDefaults.Weak));
            weakCard.Controls.Add(new SectionHeader(T("Your weak spots"), T("The items you miss most. Re-read the pages and practise them again."), harder, practiceWeak));
            foreach (var item in weak) weakCard.Controls.Add(new ItemRow(item, showContext: true));
            body.Controls.Add(weakCard);
        }
    }

    private Control Onboarding()
    {
        var card = new Card(28) { Gap = S(16) };
        card.Controls.Add(new TextLabel(T("Welcome to Study App"), Fonts.Heading(17f), Theme.P.Text));
        card.Controls.Add(new TextLabel(T("Turn your textbook into flashcards, tests and notes with the help of ChatGPT or Claude — then study them here with spaced repetition."), Fonts.Ui(11f), Theme.P.Text2));

        var steps = new CardGrid { MinCellWidth = S(230), Gap = S(14), MaxColumns = 3 };
        steps.Controls.Add(Step("1", Icons.Library, T("Create a subject"), T("One subject per course or textbook. Add chapters if you like.")));
        steps.Controls.Add(Step("2", Icons.Chat, T("Copy a prompt"), T("Open AI prompts, fill in the chapter and pages, and paste the prompt into your AI chat that has the textbook.")));
        steps.Controls.Add(Step("3", Icons.Import, T("Import the reply"), T("Copy the AI's JSON reply and paste it on the Import page — or drag the file in.")));
        card.Controls.Add(steps);

        var create = new ButtonEx(T("Create a subject"), Icons.Add, Kind.Primary, 42).OnClick(SubjectsPage.CreateSubject);
        var prompts = new ButtonEx(T("Get a prompt"), Icons.Chat, Kind.Secondary, 42).OnClick(() => Go(() => new PromptsPage()));
        var import = new ButtonEx(T("Import JSON"), Icons.Import, Kind.Secondary, 42).OnClick(() => Go(() => new ImportPage()));
        var sample = new ButtonEx(T("Try the sample subject"), Icons.Lightbulb, Kind.Soft, 42).WithTip(T("Adds a small example subject so you can try everything"));
        sample.Click += (_, _) => LoadSample();
        card.Controls.Add(Ui.Row(10, create, prompts, import, sample).Also(r => r.Wrap = true));
        return card;
    }

    private static Control Step(string number, string glyph, string title, string text)
    {
        var c = new Card(18) { Gap = S(8), Fill = Theme.P.Surface2, Stroke = Theme.P.Surface2 };
        var head = new Row { Gap = S(10) };
        head.Controls.Add(new IconBadge(glyph, 36, Theme.P.Accent));
        head.Controls.Add(new TextLabel(number + ". " + title, Fonts.UiSemibold(11f), Theme.P.Text, wrap: false));
        c.Controls.Add(head);
        c.Controls.Add(new TextLabel(text, Fonts.Ui(9.5f), Theme.P.Text2));
        return c;
    }

    public static void LoadSample()
    {
        var parsed = ImportParser.Parse(SampleContent.Json(L.Lang), "sample.json");
        if (parsed.Package == null) return;
        var plan = new ImportPlan { Package = parsed.Package, NewSubjectName = parsed.Package.Subject, NewSubjectColor = "#E0525E" };
        var outcome = ImportService.Execute(Host.Store, plan);
        var subject = Host.Store.GetSubject(outcome.SubjectId);
        if (subject != null)
        {
            subject.Icon = "heart";
            Host.Store.UpdateSubject(subject);
        }
        Toast.Show(T("Sample subject added. Have a look around!"), Tone.Success);
        Host.Main?.Navigate(() => new SubjectPage(outcome.SubjectId));
    }

    public override bool HandleKey(Keys keyData)
    {
        if (keyData == Keys.Enter && Host.Store.GetSubjects().Count > 0)
        {
            StudyLauncher.QuickStudy(new());
            return true;
        }
        return false;
    }

    /// <summary>Compact subject row with progress and a Study button.</summary>
    private sealed class SubjectLine : Row
    {
        public SubjectLine(Subject s, SubjectSummary sum)
        {
            Gap = S(12);
            var color = Gfx.FromHex(s.Color, Theme.P.Accent);
            Controls.Add(new IconBadge(Icons.ForSubject(s.Icon), 40, color));
            var col = new Column { Gap = S(6) }.Flex();
            var name = new TextLabel(s.Name, Fonts.UiSemibold(10.5f), Theme.P.Text, wrap: false) { IsLink = true };
            name.Click += (_, _) => Go(() => new SubjectPage(s.Id));
            col.Controls.Add(name);
            var bar = new ProgressBarEx(0, 6)
            {
                Segments = sum.Items == 0 ? null : new() { ((float)sum.Mastered / sum.Items, Theme.P.Success), ((float)sum.Learning / sum.Items, Gfx.Blend(Theme.P.Surface, color, 0.6f)) },
            };
            col.Controls.Add(bar);
            col.Controls.Add(new TextLabel(sum.Items == 0 ? T("No items yet") : F("{0} · {1:0}% mastered · {2} due", N(sum.Items, "item", "items"), sum.MasteredShare * 100, sum.DueToday), Fonts.Ui(8.5f), Theme.P.Text3, wrap: false));
            Controls.Add(col);
            var study = new ButtonEx(sum.DueToday > 0 ? T("Study") : T("Open"), sum.DueToday > 0 ? Icons.Play : Icons.ChevronRight, sum.DueToday > 0 ? Kind.Soft : Kind.Ghost, 34);
            study.Click += (_, _) =>
            {
                if (sum.DueToday > 0) StudyLauncher.QuickStudy(StudyLauncher.Scope(s.Id));
                else Go(() => new SubjectPage(s.Id));
            };
            Controls.Add(study);
        }
    }
}
