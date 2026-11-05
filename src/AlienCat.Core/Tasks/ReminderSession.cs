using AlienCat.Core.Storage;

namespace AlienCat.Core.Tasks;

/// <summary>Tracks the "session" the app is awake in, so a long sleep counts like a fresh start.</summary>
public sealed class ReminderSession(DateTimeOffset start)
{
    public static readonly TimeSpan WakeGap = TimeSpan.FromMinutes(5);
    DateTimeOffset _lastTick = start;

    public DateTimeOffset SessionStart { get; private set; } = start;

    /// <summary>Call regularly; returns true when the PC just woke up (gap longer than <see cref="WakeGap"/>).</summary>
    public bool Tick(DateTimeOffset now)
    {
        var woke = now - _lastTick > WakeGap;
        if (woke) SessionStart = now;
        _lastTick = now;
        return woke;
    }

    public static bool ShouldShowMorning(AppState state, DateTimeOffset now, TimeZoneInfo zone) =>
        state.LastMorning != DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

    /// <summary>The shown reminder is stale: the task was finished, deleted or moved in Notion.</summary>
    public static bool ShouldCloseSign(Reminder shown, IReadOnlyList<CatTask> current)
    {
        var task = current.FirstOrDefault(t => t.Id == shown.Task.Id);
        return task is null || task.Done || task.Due != shown.Task.Due;
    }

    /// <summary>Forgets snoozes and handled reminders of tasks that no longer exist. Use only with an online result.</summary>
    public static bool Prune(AppState state, IReadOnlyList<CatTask> tasks)
    {
        var ids = tasks.Select(t => t.Id).ToHashSet();
        var changed = false;
        foreach (var id in state.Snoozes.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            state.Snoozes.Remove(id);
            changed = true;
        }
        changed |= state.HandledKeys.RemoveWhere(k => !ids.Contains(k.Split('|')[0])) > 0;
        return changed;
    }
}
