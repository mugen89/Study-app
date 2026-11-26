using StudyApp.App.Dialogs;
using StudyApp.Data.Services;
using StudyApp.Domain.Sessions;

namespace StudyApp.App.Pages;

/// <summary>Starts study sessions and tests from anywhere in the app.</summary>
public static class StudyLauncher
{
    public static SessionOptions Scope(long? subjectId = null, long? chapterId = null, long? deckId = null)
    {
        var o = new SessionOptions { SubjectId = subjectId };
        if (chapterId.HasValue) o.ChapterIds.Add(chapterId.Value);
        if (deckId.HasValue) o.DeckIds.Add(deckId.Value);
        return o;
    }

    public static string ScopeText(SessionOptions o)
    {
        var parts = new List<string>();
        if (o.SubjectId is long sid && Host.Store.GetSubject(sid) is { } s) parts.Add(s.Name);
        foreach (var cid in o.ChapterIds)
            if (Host.Store.GetChapter(cid) is { } c) parts.Add(c.DisplayName);
        foreach (var did in o.DeckIds)
            if (Host.Store.GetDeck(did) is { } d) parts.Add(d.Title);
        if (o.ItemIds.Count > 0) parts.Add(N(o.ItemIds.Count, "item", "items"));
        return parts.Count == 0 ? T("All subjects") : string.Join("  ›  ", parts);
    }

    private static SessionOptions Merge(SessionOptions preferences, SessionOptions scope, SessionMode mode)
    {
        var o = preferences.Clone();
        o.Mode = mode;
        o.SubjectId = scope.SubjectId;
        o.ChapterIds = scope.ChapterIds.ToList();
        o.DeckIds = scope.DeckIds.ToList();
        o.ItemIds = scope.ItemIds.ToList();
        o.Title = scope.Title.Length > 0 ? scope.Title : ScopeText(scope);
        return o;
    }

    /// <summary>Smart review: due items + new ones, no questions asked. Offers practice when nothing is due.</summary>
    public static void QuickStudy(SessionOptions scope)
    {
        var o = Merge(SessionOptions.DefaultFlashcards(), scope, SessionMode.Flashcards);
        o.Types = ItemTypes.Studyable.ToList();
        var items = SessionBuilder.Build(Host.Store, o, Host.Settings);
        if (items.Count == 0)
        {
            var practice = o.Clone();
            practice.Order = ItemOrder.Shuffle;
            practice.MaxItems = 30;
            var all = SessionBuilder.Build(Host.Store, practice, Host.Settings);
            if (all.Count == 0)
            {
                Ask.Info(T("Nothing to study yet"), T("This selection has no flashcards or questions. Import a JSON file from your AI chat first."));
                return;
            }
            if (!Ask.Confirm(T("All caught up!"),
                    T("Nothing is due here today. Do you want to practise anyway? Practising still counts towards your statistics."),
                    T("Practise anyway"), cancelText: T("Not now")))
                return;
            o = practice;
            items = all;
        }
        Start(o, items);
    }

    /// <summary>Flashcards with the options dialog.</summary>
    public static void StudyWithOptions(SessionOptions scope)
    {
        var o = Merge(Host.Settings.LastFlashcardSession, scope, SessionMode.Flashcards);
        var chosen = SessionSetupDialog.Show(o, ScopeText(scope));
        if (chosen == null) return;
        Host.Settings.LastFlashcardSession = chosen.PreferencesOnly();
        Host.SaveSettings();
        var items = SessionBuilder.Build(Host.Store, chosen, Host.Settings);
        if (items.Count > 0) Start(chosen, items);
    }

    public static void Test(SessionOptions scope)
    {
        var o = Merge(Host.Settings.LastTestSession, scope, SessionMode.Test);
        if (SessionBuilder.Build(Host.Store, o, Host.Settings).Count == 0)
        {
            // e.g. a flashcards-only deck: test with typed answers instead of showing "no items".
            if (!o.Types.Contains(ItemTypes.Flashcard)) o.Types.Add(ItemTypes.Flashcard);
            o.OnlyMissed = o.OnlyFlagged = o.OnlyNew = false;
            o.Difficulties.Clear();
        }
        var chosen = SessionSetupDialog.Show(o, ScopeText(scope));
        if (chosen == null) return;
        Host.Settings.LastTestSession = chosen.PreferencesOnly();
        Host.SaveSettings();
        var items = SessionBuilder.Build(Host.Store, chosen, Host.Settings);
        if (items.Count == 0) return;
        Host.Main?.Navigate(() => new TestPage(chosen, items));
    }

    /// <summary>Studies a fixed list of items (e.g. "review missed items now").</summary>
    public static void ReviewItems(IEnumerable<long> itemIds, string title, bool asTest = false)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var o = asTest ? SessionOptions.DefaultTest() : SessionOptions.DefaultFlashcards();
        o.ItemIds = ids;
        o.MaxItems = 0;
        o.Title = title;
        if (asTest) o.Feedback = FeedbackTiming.Immediate;
        var items = SessionBuilder.Build(Host.Store, o, Host.Settings);
        if (items.Count == 0) return;
        if (asTest) Host.Main?.Navigate(() => new TestPage(o, items));
        else Start(o, items);
    }

    private static void Start(SessionOptions o, List<Item> items)
    {
        if (o.SubjectId is long sid) Host.Store.TouchSubject(sid);
        Host.Main?.Navigate(() => new StudyPage(o, items));
    }

    public static void Read(long deckId) => Host.Main?.Navigate(() => new ReaderPage(deckId));
}
