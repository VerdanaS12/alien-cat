using AlienCat.Core.Pet;
using AlienCat.Core.Storage;
using Xunit;
using static AlienCat.Core.Tests.Time.FakeClock;

namespace AlienCat.Core.Tests.Pet;

public class PetBrainTests
{
    static readonly Bounds Screen = new(0, 0, 1000, 700);
    static readonly DateTimeOffset Start = At(2026, 11, 5, 12, 0);

    static PetContext Ctx(DateTimeOffset now, ActivityLevel level = ActivityLevel.Normal, bool hunt = true,
        Vec? cursor = null, double still = 0, bool fullscreen = false, bool overdue = false) =>
        new(now, Kyiv, Screen, 128, level, hunt, cursor, still, fullscreen, overdue);

    static PetBrain Brain(ScriptedRandom? rng = null) => new(rng ?? new ScriptedRandom(), Start, new Vec(500, 700));

    /// <summary>Runs the brain in small steps.</summary>
    static DateTimeOffset Run(PetBrain b, DateTimeOffset now, double seconds, Func<DateTimeOffset, PetContext>? ctx = null, double dt = 0.05)
    {
        for (var t = 0.0; t < seconds; t += dt)
        {
            now = now.AddSeconds(dt);
            b.Update(ctx?.Invoke(now) ?? Ctx(now), dt);
        }
        return now;
    }

    [Fact]
    public void Pick_UsesCumulativeWeights()
    {
        var w = ActivityWeights.For(ActivityLevel.Normal, isNight: false);
        Assert.Equal(PetActivity.Walk, ActivityWeights.Pick(w, 0.0));
        Assert.Equal(PetActivity.Fly, ActivityWeights.Pick(w, 0.9999));
        var total = w.Values.Sum();
        Assert.Equal(PetActivity.Sit, ActivityWeights.Pick(w, (w[PetActivity.Walk] + 0.01) / total));
    }

    [Fact]
    public void Night_SleepWeightTripled()
    {
        var day = ActivityWeights.For(ActivityLevel.Normal, false);
        var night = ActivityWeights.For(ActivityLevel.Normal, true);
        Assert.Equal(3 * day[PetActivity.Sleep], night[PetActivity.Sleep]);
        Assert.Equal(3 * day[PetActivity.Yawn], night[PetActivity.Yawn]);
        Assert.Equal(day[PetActivity.Walk], night[PetActivity.Walk]);
        Assert.True(ActivityWeights.IsNight(At(2026, 11, 5, 23), Kyiv));
        Assert.True(ActivityWeights.IsNight(At(2026, 11, 5, 6, 59), Kyiv));
        Assert.False(ActivityWeights.IsNight(At(2026, 11, 5, 7), Kyiv));
        Assert.False(ActivityWeights.IsNight(At(2026, 11, 5, 21, 59), Kyiv));
    }

    [Fact]
    public void Weights_MatchSpecTable()
    {
        var calm = ActivityWeights.For(ActivityLevel.Calm, false);
        var playful = ActivityWeights.For(ActivityLevel.Playful, false);
        Assert.Equal(2, calm[PetActivity.Walk]);
        Assert.Equal(3, calm[PetActivity.Sleep]);
        Assert.Equal(5, playful[PetActivity.Walk]);
        Assert.Equal(2, playful[PetActivity.Fly]);
    }

    [Fact]
    public void Idle_AfterWait_PicksActivityByWeight()
    {
        // constructor: next game (0.5 → 15 min), idle wait (0 → 3 s); then choice 0 → Walk
        var rng = new ScriptedRandom(0.5, 0.0, 0.0, 0.0);
        var b = Brain(rng);
        Assert.Equal(PetActivity.Idle, b.Activity);
        Run(b, Start, 2.9);
        Assert.Equal(PetActivity.Idle, b.Activity);
        Run(b, Start.AddSeconds(2.9), 0.3);
        Assert.Equal(PetActivity.Walk, b.Activity);
    }

    [Fact]
    public void Walk_StaysInsideWorkArea_TurnsAtEdge()
    {
        var b = Brain();
        b.Start(PetActivity.Walk, 60);
        b.Position = new Vec(930, 700);
        b.Facing = 1;
        Run(b, Start, 5);
        Assert.Equal(-1, b.Facing);
        Assert.InRange(b.Position.X, 64, 1000 - 64);
        Assert.Equal(700, b.Position.Y);
    }

    [Fact]
    public void EndDrag_InAir_FallsThenLandsOnGround()
    {
        var b = Brain();
        b.BeginDrag(new Vec(400, 300));
        Assert.Equal(PetActivity.Dragged, b.Activity);
        b.DragTo(new Vec(420, 250));
        Assert.Equal(new Vec(420, 250), b.Position);
        b.EndDrag(Ctx(Start));
        Assert.Equal(PetActivity.Fall, b.Activity);
        var now = Run(b, Start, 1.5);
        Assert.Equal(700, b.Position.Y);
        Assert.True(b.Activity is PetActivity.Land or PetActivity.Idle);
        Run(b, now, 1);
        Assert.NotEqual(PetActivity.Fall, b.Activity);
    }

    [Fact]
    public void EndDrag_OnGround_GoesIdle()
    {
        var b = Brain();
        b.BeginDrag(new Vec(400, 700));
        b.EndDrag(Ctx(Start));
        Assert.Equal(PetActivity.Idle, b.Activity);
    }

    [Fact]
    public void StartReminder_InterruptsSleep()
    {
        var b = Brain();
        b.Start(PetActivity.Sleep, 100);
        b.StartReminder();
        Assert.Equal(PetActivity.Remind, b.Activity);
        Run(b, Start, 30);
        Assert.Equal(PetActivity.Remind, b.Activity);
    }

    [Fact]
    public void StartReminder_WhileDragged_StartsAfterDrop()
    {
        var b = Brain();
        b.BeginDrag(new Vec(400, 700));
        b.StartReminder();
        Assert.Equal(PetActivity.Dragged, b.Activity);
        b.EndDrag(Ctx(Start));
        Run(b, Start, 0.1);
        Assert.Equal(PetActivity.Remind, b.Activity);
    }

    [Fact]
    public void StartReminder_WhileFalling_StartsAfterLanding()
    {
        var b = Brain();
        b.BeginDrag(new Vec(400, 200));
        b.EndDrag(Ctx(Start));
        b.StartReminder();
        Assert.Equal(PetActivity.Fall, b.Activity);
        Run(b, Start, 3);
        Assert.Equal(PetActivity.Remind, b.Activity);
        Assert.Equal(700, b.Position.Y);
    }

    [Fact]
    public void EndReminder_Done_Happy()
    {
        var b = Brain();
        b.StartReminder();
        b.Nagging = true;
        b.EndReminder(done: true);
        Assert.Equal(PetActivity.Happy, b.Activity);
        Assert.False(b.Nagging);
        b.StartReminder();
        b.EndReminder(done: false);
        Assert.Equal(PetActivity.Idle, b.Activity);
    }

    [Fact]
    public void Fullscreen_HidesAndRestores()
    {
        var b = Brain();
        b.Start(PetActivity.Walk, 60);
        var pos = b.Position;
        b.Update(Ctx(Start, fullscreen: true), 0.05);
        Assert.Equal(PetActivity.Hidden, b.Activity);
        Run(b, Start, 5, t => Ctx(t, fullscreen: true));
        Assert.Equal(pos, b.Position);
        b.Update(Ctx(Start.AddSeconds(6)), 0.05);
        Assert.Equal(PetActivity.Idle, b.Activity);
    }

    [Fact]
    public void Click_Purrs_ButNotWhileReminding()
    {
        var b = Brain();
        b.Click();
        Assert.Equal(PetActivity.Purr, b.Activity);
        b.StartReminder();
        b.Click();
        Assert.Equal(PetActivity.Remind, b.Activity);
    }

    [Fact]
    public void CursorHunt_RequiresStillNearAndCooldown()
    {
        var near = new Vec(650, 690);  // 150 px from (500,700)
        var far = new Vec(750, 690);   // 250 px
        var b = Brain();
        b.Update(Ctx(Start, cursor: near, still: 1.9), 0.05);
        Assert.Equal(PetActivity.Idle, b.Activity);
        b.Update(Ctx(Start, cursor: far, still: 5), 0.05);
        Assert.Equal(PetActivity.Idle, b.Activity);
        b.Update(Ctx(Start, cursor: near, still: 5, hunt: false), 0.05);
        Assert.Equal(PetActivity.Idle, b.Activity);
        b.Update(Ctx(Start, cursor: near, still: 2), 0.05);
        Assert.Equal(PetActivity.CursorHunt, b.Activity);

        // cursor moves → hunt ends
        b.Update(Ctx(Start.AddSeconds(1), cursor: near, still: 0), 0.05);
        Assert.NotEqual(PetActivity.CursorHunt, b.Activity);
        b.Start(PetActivity.Idle, 60);
        b.Update(Ctx(Start.AddMinutes(5), cursor: near, still: 3), 0.05);
        Assert.NotEqual(PetActivity.CursorHunt, b.Activity); // cooldown
        b.Update(Ctx(Start.AddMinutes(10).AddSeconds(1), cursor: near, still: 3), 0.05);
        Assert.Equal(PetActivity.CursorHunt, b.Activity);
    }

    [Fact]
    public void CursorHunt_EndsAfterEightSeconds()
    {
        var b = Brain();
        var near = new Vec(560, 690);
        b.Update(Ctx(Start, cursor: near, still: 3), 0.05);
        Assert.Equal(PetActivity.CursorHunt, b.Activity);
        Run(b, Start, 8.5, t => Ctx(t, cursor: near, still: 10));
        Assert.NotEqual(PetActivity.CursorHunt, b.Activity);
    }

    [Fact]
    public void Games_FirstAfterInterval_ThenRepeats()
    {
        // Normal: interval 10–20 min; 0.5 → 15 min
        var b = Brain(new ScriptedRandom(0.5) { Fallback = 0.5 });
        var seen = new HashSet<PetActivity>();
        var now = Start;
        for (var i = 0; i < 14 * 60 * 4; i++)   // 14 minutes at 4 Hz
        {
            now = now.AddSeconds(0.25);
            b.Update(Ctx(now), 0.25);
            seen.Add(b.Activity);
        }
        Assert.DoesNotContain(PetActivity.YarnPlay, seen);
        Assert.DoesNotContain(PetActivity.ButterflyHunt, seen);
        Assert.DoesNotContain(PetActivity.MouseChase, seen);
        for (var i = 0; i < 2 * 60 * 4; i++)    // until minute 16
        {
            now = now.AddSeconds(0.25);
            b.Update(Ctx(now), 0.25);
            seen.Add(b.Activity);
        }
        Assert.True(seen.Contains(PetActivity.YarnPlay) || seen.Contains(PetActivity.ButterflyHunt) || seen.Contains(PetActivity.MouseChase));
    }

    [Fact]
    public void Game_HasToyAndEndsWithin25Seconds()
    {
        var b = Brain();
        b.Start(PetActivity.YarnPlay, 25);
        b.Update(Ctx(Start), 0.05);
        Assert.NotNull(b.ToyPosition);
        Run(b, Start, 26);
        Assert.NotEqual(PetActivity.YarnPlay, b.Activity);
    }

    [Fact]
    public void Sad_WhenOverdueAndIdle()
    {
        var b = Brain();
        b.Update(Ctx(Start, overdue: true), 0.05);
        Assert.True(b.Sad);
        b.Update(Ctx(Start, overdue: false), 0.05);
        Assert.False(b.Sad);
        b.Start(PetActivity.Walk, 30);
        b.Update(Ctx(Start, overdue: true), 0.05);
        Assert.False(b.Sad);
    }

    [Fact]
    public void Fly_RisesAndReturnsToGround()
    {
        var b = Brain(new ScriptedRandom() { Fallback = 0.0 });
        b.Start(PetActivity.Fly, 0);
        Run(b, Start, 1.6);
        Assert.InRange(b.Position.Y, 700 - 121, 700 - 100);
        Run(b, Start.AddSeconds(2), 10);
        Assert.Equal(700, b.Position.Y);
        Assert.NotEqual(PetActivity.Fly, b.Activity);
    }

    [Fact]
    public void ActivityChanged_Raised()
    {
        var b = Brain();
        var changes = new List<PetActivity>();
        b.ActivityChanged += changes.Add;
        b.Click();
        Assert.Equal(new[] { PetActivity.Purr }, changes.ToArray());
    }
}
