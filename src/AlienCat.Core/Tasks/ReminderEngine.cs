namespace AlienCat.Core.Tasks;

public enum ReminderKind { Early, Due }

public sealed record Reminder(CatTask Task, ReminderKind Kind);

public sealed record QuietHours(bool Enabled, TimeOnly Start, TimeOnly End);

public enum SnoozeOption { TenMinutes, OneHour, TomorrowMorning }

public static class ReminderEngine
{
    public static readonly TimeSpan EarlyLead = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan NagAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeOnly MorningTime = new(9, 0);

    /// <summary>Reminders that should be shown now, most important first.</summary>
    public static IReadOnlyList<Reminder> GetPending(
        IReadOnlyList<CatTask> tasks,
        DateTimeOffset now,
        DateTimeOffset sessionStart,
        IReadOnlyDictionary<string, DateTimeOffset> snoozes,
        IReadOnlySet<string> handledKeys,
        QuietHours quiet,
        TimeZoneInfo zone)
    {
        if (IsQuiet(quiet, now, zone)) return [];

        var result = new List<Reminder>();
        foreach (var task in tasks)
        {
            if (task.Done || task.Kind != ItemKind.Task || task.Due is not { } due) continue;

            Reminder? reminder = null;
            if (snoozes.TryGetValue(task.Id, out var until))
            {
                if (until > now) continue;
                if (due <= until) reminder = new Reminder(task, ReminderKind.Due); // snooze ended
            }

            if (reminder is null)
            {
                if (due <= now)
                {
                    if (due >= sessionStart) reminder = new Reminder(task, ReminderKind.Due);
                }
                else if (task.Priority != Priority.Normal && due - EarlyLead <= now && due - EarlyLead >= sessionStart)
                {
                    reminder = new Reminder(task, ReminderKind.Early);
                }
            }

            if (reminder is not null && !handledKeys.Contains(Key(reminder))) result.Add(reminder);
        }

        return result
            .OrderByDescending(r => r.Task.Priority)
            .ThenBy(r => r.Task.Due)
            .ToList();
    }

    public static string Key(Reminder r) => $"{r.Task.Id}|{r.Kind}|{r.Task.Due:O}";

    public static DateTimeOffset SnoozeUntil(SnoozeOption option, DateTimeOffset now, TimeZoneInfo zone) => option switch
    {
        SnoozeOption.TenMinutes => now.AddMinutes(10),
        SnoozeOption.OneHour => now.AddHours(1),
        SnoozeOption.TomorrowMorning => Recurrence.ToOffset(
            TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(1).Add(MorningTime.ToTimeSpan()), zone),
        _ => throw new ArgumentOutOfRangeException(nameof(option)),
    };

    public static bool IsQuiet(QuietHours q, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!q.Enabled || q.Start == q.End) return false;
        var t = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        return q.Start < q.End
            ? t >= q.Start && t < q.End
            : t >= q.Start || t < q.End;
    }

    public static bool IsNagging(DateTimeOffset shownAt, DateTimeOffset now) => now - shownAt >= NagAfter;

    /// <summary>Open tasks due before the end of today (including overdue ones), earliest first.</summary>
    public static IReadOnlyList<CatTask> MorningSummary(IReadOnlyList<CatTask> tasks, DateTimeOffset now, TimeZoneInfo zone)
    {
        var endOfToday = EndOfDay(now, zone);
        return tasks
            .Where(t => !t.Done && t.Kind == ItemKind.Task && t.Due is { } d && d < endOfToday)
            .OrderBy(t => t.Due)
            .ToList();
    }

    public static bool HasOverdue(IReadOnlyList<CatTask> tasks, DateTimeOffset now) =>
        tasks.Any(t => !t.Done && t.Kind == ItemKind.Task && t.Due is { } d && d < now);

    public static DateTimeOffset EndOfDay(DateTimeOffset now, TimeZoneInfo zone) =>
        Recurrence.ToOffset(TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(1), zone);
}
