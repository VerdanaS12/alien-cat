using AlienCat.Core.Pet;
using AlienCat.Core.Storage;
using Xunit;
using static AlienCat.Core.Tests.Time.FakeClock;

namespace AlienCat.Core.Tests.Pet;

public class TrickScheduleTests
{
    static readonly DateTimeOffset Start = At(2026, 11, 7, 12);

    [Theory]
    [InlineData(TrickKind.Climb, 15, 30)]
    [InlineData(TrickKind.Away, 60, 120)]
    [InlineData(TrickKind.ScreenPounce, 60, 120)]
    [InlineData(TrickKind.CursorSteal, 120, 180)]
    public void FirstDue_WithinBaseInterval(TrickKind kind, double min, double max)
    {
        var lo = new TrickSchedule(new ScriptedRandom { Fallback = 0.0 }, Start, ActivityLevel.Normal);
        var hi = new TrickSchedule(new ScriptedRandom { Fallback = 0.999999 }, Start, ActivityLevel.Normal);
        Assert.Equal(Start.AddMinutes(min), lo.NextAt(kind));
        Assert.InRange((hi.NextAt(kind) - Start).TotalMinutes, max - 0.01, max);
        Assert.False(lo.IsDue(kind, Start.AddMinutes(min - 1)));
        Assert.True(lo.IsDue(kind, Start.AddMinutes(min)));
    }

    [Fact]
    public void Level_ScalesIntervals()
    {
        var calm = new TrickSchedule(new ScriptedRandom { Fallback = 0.0 }, Start, ActivityLevel.Calm);
        var playful = new TrickSchedule(new ScriptedRandom { Fallback = 0.0 }, Start, ActivityLevel.Playful);
        Assert.Equal(Start.AddMinutes(120), calm.NextAt(TrickKind.Away));
        Assert.Equal(Start.AddMinutes(30), playful.NextAt(TrickKind.Away));
    }

    [Fact]
    public void Done_ReschedulesFromNow()
    {
        var s = new TrickSchedule(new ScriptedRandom { Fallback = 0.0 }, Start, ActivityLevel.Normal);
        var now = Start.AddMinutes(70);
        s.Done(TrickKind.Away, now, ActivityLevel.Normal);
        Assert.Equal(now.AddMinutes(60), s.NextAt(TrickKind.Away));
        Assert.False(s.IsDue(TrickKind.Away, now.AddMinutes(59)));
    }

    [Fact]
    public void Settings_Enabled()
    {
        var all = new TrickSettings();
        Assert.True(all.Enabled(TrickKind.Climb));
        Assert.True(all.WallClimb && all.WebHang && all.Away && all.ScreenPounce && all.CursorSteal);
        var webOnly = TrickSettings.AllOff with { WebHang = true };
        Assert.True(webOnly.Enabled(TrickKind.Climb));
        Assert.False(webOnly.Enabled(TrickKind.Away));
        Assert.False(TrickSettings.AllOff.Enabled(TrickKind.Climb));
    }
}
