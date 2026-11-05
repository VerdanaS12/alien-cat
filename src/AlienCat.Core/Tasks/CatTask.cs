namespace AlienCat.Core.Tasks;

public enum Priority { Normal, Important, Urgent }

public enum Repeat { None, Daily, Weekdays, Weekly, Monthly }

public enum ItemKind { Task, Note }

public sealed record CatTask(
    string Id,
    string Title,
    DateTimeOffset? Due,
    bool Done,
    Priority Priority,
    Repeat Repeat,
    ItemKind Kind);
