using AlienCat.Core.Tasks;

namespace AlienCat.Core.Notion;

public static class NotionFields
{
    public const string Title = "Название";
    public const string Due = "Напомнить";
    public const string Done = "Готово";
    public const string Priority = "Приоритет";
    public const string Repeat = "Повтор";
    public const string Kind = "Тип";

    public const string KindTask = "Задача";
    public const string KindNote = "Заметка";

    /// <summary>Field name → Notion property type, in the order shown to the user.</summary>
    public static readonly IReadOnlyList<(string Name, string Type)> Required =
    [
        (Title, "title"), (Due, "date"), (Done, "checkbox"), (Priority, "select"), (Repeat, "select"), (Kind, "select"),
    ];

    public static Tasks.Priority ParsePriority(string? name) => name switch
    {
        "Важный" => Tasks.Priority.Important,
        "Срочный" => Tasks.Priority.Urgent,
        _ => Tasks.Priority.Normal,
    };

    public static Tasks.Repeat ParseRepeat(string? name) => name switch
    {
        "Каждый день" => Tasks.Repeat.Daily,
        "По будням" => Tasks.Repeat.Weekdays,
        "Каждую неделю" => Tasks.Repeat.Weekly,
        "Каждый месяц" => Tasks.Repeat.Monthly,
        _ => Tasks.Repeat.None,
    };

    public static ItemKind ParseKind(string? name) => name == KindNote ? ItemKind.Note : ItemKind.Task;
}
