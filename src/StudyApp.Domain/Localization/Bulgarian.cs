namespace StudyApp.Domain.Localization;

/// <summary>Bulgarian translations. Keys are the exact English UI strings.</summary>
public static partial class Bulgarian
{
    public readonly record struct PluralForms(string One, string Many);

    /// <summary>Noun forms after a number: 1 карта / 5 карти, 1 въпрос / 5 въпроса.</summary>
    public static readonly Dictionary<string, PluralForms> Plurals = new()
    {
        ["card"] = new("карта", "карти"),
        ["question"] = new("въпрос", "въпроса"),
        ["note"] = new("бележка", "бележки"),
        ["section"] = new("раздел", "раздела"),
        ["chapter"] = new("глава", "глави"),
        ["project"] = new("проект", "проекта"),
        ["set"] = new("комплект", "комплекта"),
        ["item"] = new("елемент", "елемента"),
        ["day"] = new("ден", "дни"),
        ["hour"] = new("час", "часа"),
        ["minute"] = new("минута", "минути"),
        ["second"] = new("секунда", "секунди"),
        ["review"] = new("преговор", "преговора"),
        ["file"] = new("файл", "файла"),
        ["page"] = new("страница", "страници"),
        ["answer"] = new("отговор", "отговора"),
        ["mistake"] = new("грешка", "грешки"),
        ["backup"] = new("резервно копие", "резервни копия"),
        ["warning"] = new("предупреждение", "предупреждения"),
        ["attempt"] = new("опит", "опита"),
        ["term"] = new("термин", "термина"),
        ["duplicate"] = new("дубликат", "дубликата"),
        ["template"] = new("шаблон", "шаблона"),
        ["deck"] = new("комплект", "комплекта"),
        ["error"] = new("грешка", "грешки"),
        ["result"] = new("резултат", "резултата"),
        ["subject"] = new("предмет", "предмета"),
    };

    public static readonly Dictionary<string, string> Strings = new(StringComparer.Ordinal);

    static Bulgarian()
    {
        foreach (var (en, bg) in Entries()) Strings[en] = bg;
    }

    private static partial IEnumerable<(string En, string Bg)> Entries();
}
