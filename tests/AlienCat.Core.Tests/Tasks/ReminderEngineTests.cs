using AlienCat.Core.Tasks;
using Xunit;
using static AlienCat.Core.Tests.Time.FakeClock;

namespace AlienCat.Core.Tests.Tasks;

public class ReminderEngineTests
{
    static readonly DateTimeOffset Session = At(2026, 11, 5, 8, 0);
    static readonly QuietHours NoQuiet = new(false, new TimeOnly(23, 0), new TimeOnly(8, 0));
    static readonly Dictionary<string, DateTimeOffset> NoSnoozes = new();
    static readonly HashSet<string> NoHandled = new();

    static CatTask T(string id, DateTimeOffset? due, Priority p = Priority.Normal, bool done = false,
        ItemKind kind = ItemKind.Task, Repeat repeat = Repeat.None) =>
        new(id, "Задача " + id, due, done, p, repeat, kind);

    static IReadOnlyList<Reminder> Pending(IReadOnlyList<CatTask> tasks, DateTimeOffset now,
        IReadOnlyDictionary<string, DateTimeOffset>? snoozes = null, IReadOnlySet<string>? handled = null,
        QuietHours? quiet = null, DateTimeOffset? session = null) =>
        ReminderEngine.GetPending(tasks, now, session ?? Session, snoozes ?? NoSnoozes, handled ?? NoHandled,
            quiet ?? NoQuiet, Kyiv);

    [Fact]
    public void GetPending_DueTask_ReturnsDue()
    {
        var r = Assert.Single(Pending([T("a", At(2026, 11, 5, 10))], At(2026, 11, 5, 10, 0, 30)));
        Assert.Equal(ReminderKind.Due, r.Kind);
        Assert.Equal("a", r.Task.Id);
    }

    [Fact]
    public void GetPending_FutureNormal_None() =>
        Assert.Empty(Pending([T("a", At(2026, 11, 5, 10, 10))], At(2026, 11, 5, 10)));

    [Fact]
    public void GetPending_ImportantTenMinutesBefore_ReturnsEarly()
    {
        var tasks = new[] { T("a", At(2026, 11, 5, 10, 10), Priority.Important) };
        var r = Assert.Single(Pending(tasks, At(2026, 11, 5, 10)));
        Assert.Equal(ReminderKind.Early, r.Kind);
        Assert.Empty(Pending(tasks, At(2026, 11, 5, 9, 59)));
    }

    [Fact]
    public void GetPending_UrgentAtDueTime_ReturnsDueNotEarly()
    {
        var r = Assert.Single(Pending([T("a", At(2026, 11, 5, 10), Priority.Urgent)], At(2026, 11, 5, 10, 1)));
        Assert.Equal(ReminderKind.Due, r.Kind);
    }

    [Fact]
    public void GetPending_DoneOrNoteOrNoDate_Ignored() =>
        Assert.Empty(Pending([
            T("a", At(2026, 11, 5, 10), done: true),
            T("b", At(2026, 11, 5, 10), kind: ItemKind.Note),
            T("c", null)], At(2026, 11, 5, 11)));

    [Fact]
    public void GetPending_Snoozed_HiddenUntilSnoozeEnds()
    {
        var tasks = new[] { T("a", At(2026, 11, 5, 10)) };
        var snoozes = new Dictionary<string, DateTimeOffset> { ["a"] = At(2026, 11, 5, 10, 10) };
        Assert.Empty(Pending(tasks, At(2026, 11, 5, 10, 5), snoozes));
        Assert.Single(Pending(tasks, At(2026, 11, 5, 10, 10), snoozes));
    }

    [Fact]
    public void GetPending_SnoozedTaskFromBeforeSession_StillPopsWhenSnoozeEnds()
    {
        var tasks = new[] { T("a", At(2026, 11, 5, 7)) };
        var snoozes = new Dictionary<string, DateTimeOffset> { ["a"] = At(2026, 11, 5, 9) };
        Assert.Single(Pending(tasks, At(2026, 11, 5, 9, 1), snoozes));
    }

    [Fact]
    public void GetPending_HandledKey_NotReturned()
    {
        var tasks = new[] { T("a", At(2026, 11, 5, 10)) };
        var key = ReminderEngine.Key(new Reminder(tasks[0], ReminderKind.Due));
        Assert.Empty(Pending(tasks, At(2026, 11, 5, 10, 1), handled: new HashSet<string> { key }));
    }

    [Fact]
    public void Key_ChangesWhenDueMoves()
    {
        var a = T("a", At(2026, 11, 5, 10));
        var b = a with { Due = At(2026, 11, 6, 10) };
        Assert.NotEqual(ReminderEngine.Key(new Reminder(a, ReminderKind.Due)), ReminderEngine.Key(new Reminder(b, ReminderKind.Due)));
    }

    [Fact]
    public void GetPending_QuietHours_ReturnsNothing()
    {
        var quiet = new QuietHours(true, new TimeOnly(23, 0), new TimeOnly(8, 0));
        var tasks = new[] { T("a", At(2026, 11, 6, 1)) };
        var session = At(2026, 11, 5, 20);
        Assert.Empty(Pending(tasks, At(2026, 11, 6, 2), quiet: quiet, session: session));
        Assert.Single(Pending(tasks, At(2026, 11, 6, 8), quiet: quiet, session: session));
    }

    [Fact]
    public void GetPending_OrdersByPriorityThenDue()
    {
        var tasks = new[]
        {
            T("n1", At(2026, 11, 5, 9)),
            T("u", At(2026, 11, 5, 9, 30), Priority.Urgent),
            T("i", At(2026, 11, 5, 9, 10), Priority.Important),
            T("n0", At(2026, 11, 5, 8, 30)),
        };
        var ids = Pending(tasks, At(2026, 11, 5, 10)).Select(r => r.Task.Id).ToArray();
        Assert.Equal(new[] { "u", "i", "n0", "n1" }, ids);
    }

    [Fact]
    public void GetPending_OverdueBeforeSessionStart_NotPopped()
    {
        var tasks = new[] { T("a", At(2026, 11, 5, 8)) };
        var session = At(2026, 11, 5, 9);
        Assert.Empty(Pending(tasks, At(2026, 11, 5, 9, 1), session: session));
        Assert.Contains(tasks[0], ReminderEngine.MorningSummary(tasks, At(2026, 11, 5, 9, 1), Kyiv));
    }

    [Fact]
    public void GetPending_TaskNoLongerListed_NotReturned()
    {
        var gone = T("gone", At(2026, 11, 5, 10));
        var stays = T("stays", At(2026, 11, 5, 10));
        var before = Pending([gone, stays], At(2026, 11, 5, 10, 1));
        Assert.Equal(2, before.Count);
        var after = Pending([stays], At(2026, 11, 5, 10, 1));
        Assert.DoesNotContain(after, r => r.Task.Id == "gone");
    }

    [Fact]
    public void SnoozeUntil_TenMinutes() =>
        Assert.Equal(At(2026, 11, 5, 10, 10), ReminderEngine.SnoozeUntil(SnoozeOption.TenMinutes, At(2026, 11, 5, 10), Kyiv));

    [Fact]
    public void SnoozeUntil_OneHour() =>
        Assert.Equal(At(2026, 11, 5, 11), ReminderEngine.SnoozeUntil(SnoozeOption.OneHour, At(2026, 11, 5, 10), Kyiv));

    [Fact]
    public void SnoozeUntil_TomorrowMorning_Is0900NextDay() =>
        Assert.Equal(At(2026, 11, 6, 9), ReminderEngine.SnoozeUntil(SnoozeOption.TomorrowMorning, At(2026, 11, 5, 23, 30), Kyiv));

    [Fact]
    public void IsQuiet_CrossesMidnight()
    {
        var q = new QuietHours(true, new TimeOnly(23, 0), new TimeOnly(8, 0));
        Assert.True(ReminderEngine.IsQuiet(q, At(2026, 11, 5, 23, 30), Kyiv));
        Assert.True(ReminderEngine.IsQuiet(q, At(2026, 11, 6, 7, 59), Kyiv));
        Assert.False(ReminderEngine.IsQuiet(q, At(2026, 11, 6, 8, 0), Kyiv));
        Assert.False(ReminderEngine.IsQuiet(q with { Enabled = false }, At(2026, 11, 5, 23, 30), Kyiv));
        var day = new QuietHours(true, new TimeOnly(13, 0), new TimeOnly(14, 0));
        Assert.True(ReminderEngine.IsQuiet(day, At(2026, 11, 5, 13, 30), Kyiv));
        Assert.False(ReminderEngine.IsQuiet(day, At(2026, 11, 5, 14, 30), Kyiv));
    }

    [Fact]
    public void IsNagging_FiveMinutes_True_FourMinutes_False()
    {
        var shown = At(2026, 11, 5, 10);
        Assert.True(ReminderEngine.IsNagging(shown, At(2026, 11, 5, 10, 5)));
        Assert.False(ReminderEngine.IsNagging(shown, At(2026, 11, 5, 10, 4)));
    }

    [Fact]
    public void MorningSummary_TodayAndOverdue_SortedByDue_ExcludesTomorrow()
    {
        var tasks = new[]
        {
            T("today", At(2026, 11, 5, 15)),
            T("overdue", At(2026, 11, 3, 10)),
            T("tomorrow", At(2026, 11, 6, 9)),
            T("done", At(2026, 11, 5, 9), done: true),
            T("note", At(2026, 11, 5, 9), kind: ItemKind.Note),
        };
        var ids = ReminderEngine.MorningSummary(tasks, At(2026, 11, 5, 8), Kyiv).Select(t => t.Id).ToArray();
        Assert.Equal(new[] { "overdue", "today" }, ids);
    }

    [Fact]
    public void HasOverdue_OnlyPastAndOpen()
    {
        Assert.True(ReminderEngine.HasOverdue([T("a", At(2026, 11, 5, 9))], At(2026, 11, 5, 10)));
        Assert.False(ReminderEngine.HasOverdue([T("a", At(2026, 11, 5, 11))], At(2026, 11, 5, 10)));
        Assert.False(ReminderEngine.HasOverdue([T("a", At(2026, 11, 5, 9), done: true)], At(2026, 11, 5, 10)));
    }
}
