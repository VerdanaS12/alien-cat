using AlienCat.Core.Storage;
using AlienCat.Core.Tasks;
using Xunit;
using static AlienCat.Core.Tests.Time.FakeClock;

namespace AlienCat.Core.Tests.Tasks;

public class ReminderSessionTests
{
    static CatTask T(string id, DateTimeOffset? due, bool done = false) =>
        new(id, "Задача " + id, due, done, Priority.Normal, Repeat.None, ItemKind.Task);

    [Fact]
    public void Tick_AfterSleepGap_RestartsSession()
    {
        var s = new ReminderSession(At(2026, 11, 5, 22));
        Assert.False(s.Tick(At(2026, 11, 5, 22, 0, 5)));
        Assert.Equal(At(2026, 11, 5, 22), s.SessionStart);
        Assert.True(s.Tick(At(2026, 11, 6, 8)));          // laptop slept overnight
        Assert.Equal(At(2026, 11, 6, 8), s.SessionStart);
    }

    [Fact]
    public void AfterWake_OvernightTasksGoToMorningListNotPopups()
    {
        var s = new ReminderSession(At(2026, 11, 5, 22));
        s.Tick(At(2026, 11, 6, 8));
        var tasks = new[] { T("a", At(2026, 11, 6, 6)), T("b", At(2026, 11, 6, 7)) };
        var pending = ReminderEngine.GetPending(tasks, At(2026, 11, 6, 8, 0, 5), s.SessionStart,
            new Dictionary<string, DateTimeOffset>(), new HashSet<string>(), new QuietHours(false, default, default), Kyiv);
        Assert.Empty(pending);
        var state = new AppState { LastMorning = new DateOnly(2026, 11, 5) };
        Assert.True(ReminderSession.ShouldShowMorning(state, At(2026, 11, 6, 8), Kyiv));
        state.LastMorning = new DateOnly(2026, 11, 6);
        Assert.False(ReminderSession.ShouldShowMorning(state, At(2026, 11, 6, 9), Kyiv));
    }

    [Fact]
    public void ShouldCloseSign_WhenTaskDoneDeletedOrRescheduled()
    {
        var shown = new Reminder(T("a", At(2026, 11, 5, 10)), ReminderKind.Due);
        Assert.False(ReminderSession.ShouldCloseSign(shown, [T("a", At(2026, 11, 5, 10))]));
        Assert.True(ReminderSession.ShouldCloseSign(shown, []));
        Assert.True(ReminderSession.ShouldCloseSign(shown, [T("a", At(2026, 11, 5, 10), done: true)]));
        Assert.True(ReminderSession.ShouldCloseSign(shown, [T("a", At(2026, 11, 5, 18))]));
    }

    [Fact]
    public void RescheduledTask_ReArmsWithNewKey()
    {
        var oldTask = T("a", At(2026, 11, 5, 10));
        var handled = new HashSet<string> { ReminderEngine.Key(new Reminder(oldTask, ReminderKind.Due)) };
        var moved = oldTask with { Due = At(2026, 11, 5, 18) };
        var pending = ReminderEngine.GetPending([moved], At(2026, 11, 5, 18, 1), At(2026, 11, 5, 8),
            new Dictionary<string, DateTimeOffset>(), handled, new QuietHours(false, default, default), Kyiv);
        Assert.Single(pending);
    }

    [Fact]
    public void Prune_RemovesEntriesOfVanishedTasks()
    {
        var state = new AppState();
        state.Snoozes["gone"] = At(2026, 11, 5, 10);
        state.Snoozes["a"] = At(2026, 11, 5, 10);
        state.HandledKeys.Add("gone|Due|x");
        state.HandledKeys.Add("a|Due|x");
        Assert.True(ReminderSession.Prune(state, [T("a", At(2026, 11, 5, 9))]));
        Assert.Equal(new[] { "a" }, state.Snoozes.Keys.ToArray());
        Assert.Equal(new[] { "a|Due|x" }, state.HandledKeys.ToArray());
        Assert.False(ReminderSession.Prune(state, [T("a", At(2026, 11, 5, 9))]));
    }
}
