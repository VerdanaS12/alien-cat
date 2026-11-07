using AlienCat.Core.Pet;
using AlienCat.Core.Storage;
using Xunit;
using static AlienCat.Core.Tests.Time.FakeClock;

namespace AlienCat.Core.Tests.Pet;

public class PetTrickTests
{
    static readonly Bounds Screen = new(0, 0, 1000, 700);
    const double S = 128;
    static readonly DateTimeOffset Start = At(2026, 11, 7, 12, 0);

    static PetContext Ctx(DateTimeOffset now, Vec? cursor = null, double idle = 0, bool fullscreen = false,
        TrickSettings? tricks = null, OutfitMode outfit = OutfitMode.None, ActivityLevel level = ActivityLevel.Normal,
        bool button = false) =>
        new(now, Kyiv, Screen, S, level, false, cursor, 0, fullscreen, false, idle, tricks, outfit, button);

    static PetBrain Brain(double x = 500, ScriptedRandom? rng = null) => new(rng ?? new ScriptedRandom(), Start, new Vec(x, 700));

    sealed class Sim(PetBrain brain)
    {
        public DateTimeOffset Now = Start;
        public Func<DateTimeOffset, PetContext> Context = t => Ctx(t);
        public readonly HashSet<PetActivity> Seen = new();
        public readonly HashSet<Surface> Surfaces = new();
        public readonly HashSet<TrickPhase> Phases = new();
        public bool WasInvisible;

        public void Run(double seconds, double dt = 0.05, Func<bool>? until = null)
        {
            for (var t = 0.0; t < seconds; t += dt)
            {
                Now = Now.AddSeconds(dt);
                brain.Update(Context(Now), dt);
                Seen.Add(brain.Activity);
                Surfaces.Add(brain.Surface);
                Phases.Add(brain.Trick);
                if (!brain.IsVisible) WasInvisible = true;
                if (until?.Invoke() == true) return;
            }
        }
    }

    // ---------------------------------------------------------- screen pounce

    [Fact]
    public void Pounce_LeapsToGlassSlidesAndReturns()
    {
        var b = Brain();
        Assert.True(b.BeginTrick(PetActivity.ScreenPounce, Ctx(Start)));
        var sim = new Sim(b);
        sim.Run(0.25);
        Assert.Equal(TrickPhase.Leap, b.Trick);
        Assert.InRange(b.Boost, 1.01, 1.99);
        sim.Run(0.4);
        Assert.Equal(Surface.Glass, b.Surface);
        Assert.Equal(TrickPhase.Cling, b.Trick);
        Assert.Equal(PetBrain.PounceBoost, b.Boost);
        var glass = b.GlassStart!.Value;
        Assert.InRange(glass.X, 250, 750);
        Assert.InRange(glass.Y, 245, 385);
        sim.Run(1.0);
        Assert.Equal(TrickPhase.Slide, b.Trick);
        Assert.True(b.Position.Y > glass.Y);
        Assert.Equal(glass.X, b.Position.X);
        sim.Run(15, until: () => b.Surface == Surface.Ground);
        sim.Run(2);
        Assert.Equal(Surface.Ground, b.Surface);
        Assert.Equal(1, b.Boost);
        Assert.Equal(700, b.Position.Y);
        Assert.Null(b.GlassStart);
        Assert.NotEqual(PetActivity.ScreenPounce, b.Activity);
    }

    // ------------------------------------------------------------- wall climb

    [Fact]
    public void Wall_ClimbsNearestWallHoldsAndComesDown()
    {
        var b = Brain(x: 300);
        Assert.True(b.BeginTrick(PetActivity.WallClimb, Ctx(Start)));
        var sim = new Sim(b);
        sim.Run(10, until: () => b.Surface == Surface.LeftWall);
        Assert.Equal(Surface.LeftWall, b.Surface);
        Assert.Equal(S / 2, b.Position.X);
        sim.Run(15, until: () => b.Trick == TrickPhase.Hold);
        Assert.Equal(TrickPhase.Hold, b.Trick);
        var height = 700 - S / 2 - b.Position.Y;
        Assert.InRange(height, 0.3 * 700 - 1, 0.66 * 700 + 1);
        sim.Run(40, until: () => b.Activity != PetActivity.WallClimb && b.Activity != PetActivity.Fall && b.Activity != PetActivity.Land);
        Assert.Equal(Surface.Ground, b.Surface);
        Assert.Equal(700, b.Position.Y);
    }

    [Fact]
    public void Wall_RightSideWhenCloserToRight()
    {
        var b = Brain(x: 800);
        b.BeginTrick(PetActivity.WallClimb, Ctx(Start));
        new Sim(b).Run(10, until: () => b.Surface == Surface.RightWall);
        Assert.Equal(Surface.RightWall, b.Surface);
        Assert.Equal(1000 - S / 2, b.Position.X);
    }

    // ---------------------------------------------------------------- web hang

    [Fact]
    public void Web_FliesUpHangsOnThreadSwingsAndLands()
    {
        var b = Brain();
        Assert.True(b.BeginTrick(PetActivity.WebHang, Ctx(Start)));
        var sim = new Sim(b);
        sim.Run(10, until: () => b.Surface == Surface.Thread);
        Assert.True(sim.WasInvisible);
        Assert.Equal(Surface.Thread, b.Surface);
        var anchor = b.ThreadAnchor!.Value;
        Assert.Equal(0, anchor.Y);
        Assert.InRange(anchor.X, S / 2, 1000 - S / 2);
        sim.Run(15, until: () => b.Trick == TrickPhase.Swing);
        var xs = new List<double>();
        for (var i = 0; i < 160; i++) { sim.Run(0.05); if (b.Trick == TrickPhase.Swing) xs.Add(b.Position.X); }
        Assert.True(xs.Max() - xs.Min() > 20, "cat should swing sideways");
        Assert.All(xs, x => Assert.InRange(x, anchor.X - 0.6 * 700, anchor.X + 0.6 * 700));
        sim.Run(40, until: () => b.Activity is PetActivity.Idle or PetActivity.Walk or PetActivity.Sit);
        Assert.Equal(Surface.Ground, b.Surface);
        Assert.Null(b.ThreadAnchor);
        Assert.Equal(700, b.Position.Y);
        Assert.True(b.IsVisible);
    }

    [Fact]
    public void Web_DragBreaksThread()
    {
        var b = Brain();
        b.BeginTrick(PetActivity.WebHang, Ctx(Start));
        var sim = new Sim(b);
        sim.Run(15, until: () => b.Surface == Surface.Thread);
        b.BeginDrag(new Vec(400, 300));
        Assert.Equal(PetActivity.Dragged, b.Activity);
        Assert.Equal(Surface.Ground, b.Surface);
        Assert.Null(b.ThreadAnchor);
        b.EndDrag(Ctx(sim.Now));
        Assert.Equal(PetActivity.Fall, b.Activity);
    }

    // ------------------------------------------------------------ cursor steal

    static Vec MouthAbove(double x, double up) => new(x, 700 - 0.4 * S - up);

    [Fact]
    public void Steal_RequiresIdleUserAndReachableCursor()
    {
        var b = Brain();
        Assert.False(b.BeginTrick(PetActivity.CursorSteal, Ctx(Start, MouthAbove(650, 50), idle: 5)));
        Assert.False(b.BeginTrick(PetActivity.CursorSteal, Ctx(Start, MouthAbove(650, 400), idle: 30)));
        Assert.False(b.BeginTrick(PetActivity.CursorSteal, Ctx(Start, null, idle: 30)));
        Assert.False(b.BeginTrick(PetActivity.CursorSteal, Ctx(Start, MouthAbove(650, 50), idle: 30, fullscreen: true)));
        Assert.Equal(PetActivity.Idle, b.Activity);
        Assert.True(b.BeginTrick(PetActivity.CursorSteal, Ctx(Start, MouthAbove(650, 50), idle: 30)));
        Assert.Equal(PetActivity.CursorSteal, b.Activity);
    }

    [Fact]
    public void Steal_CarriesCursorAtMostThreeSecondsAndThreeHundredPixels()
    {
        var b = Brain();
        var origin = MouthAbove(700, 120);
        Vec cursor = origin;
        b.BeginTrick(PetActivity.CursorSteal, Ctx(Start, cursor, idle: 30));
        var sim = new Sim(b) { Context = t => Ctx(t, cursor, idle: 30) };
        var carried = 0.0;
        var maxDist = 0.0;
        for (var i = 0; i < 400; i++)
        {
            sim.Run(0.05);
            if (b.CursorTarget is { } target)
            {
                cursor = target;            // the app moves the real cursor here
                carried += 0.05;
                maxDist = Math.Max(maxDist, target.DistanceTo(origin));
                var mouth = b.Mouth(S);
                if (b.Trick == TrickPhase.Carry) Assert.True(target.DistanceTo(mouth) < 1);
            }
        }
        Assert.True(carried > 0.5, "cursor should be carried");
        Assert.InRange(carried, 0, PetBrain.StealMaxSeconds + 0.11);
        Assert.InRange(maxDist, 0, PetBrain.StealMaxDistance + 1);
        Assert.Null(b.CursorTarget);
        Assert.NotEqual(PetActivity.CursorSteal, b.Activity);
    }

    [Fact]
    public void Steal_UserMovesMouse_ReleasesImmediately()
    {
        var b = Brain();
        var cursor = MouthAbove(600, 60);
        b.BeginTrick(PetActivity.CursorSteal, Ctx(Start, cursor, idle: 30));
        var sim = new Sim(b) { Context = t => Ctx(t, b.CursorTarget ?? cursor, idle: 30) };
        sim.Run(5, until: () => b.CursorTarget is not null);
        Assert.NotNull(b.CursorTarget);
        b.UserMovedCursor();
        Assert.Null(b.CursorTarget);
        Assert.NotEqual(PetActivity.CursorSteal, b.Activity);
    }

    [Fact]
    public void Steal_UserActiveDuringApproach_Aborts()
    {
        var b = Brain();
        var cursor = MouthAbove(900, 60);
        b.BeginTrick(PetActivity.CursorSteal, Ctx(Start, cursor, idle: 30));
        var sim = new Sim(b) { Context = t => Ctx(t, cursor, idle: 0.5) };
        sim.Run(0.2);
        Assert.NotEqual(PetActivity.CursorSteal, b.Activity);
        Assert.Null(b.CursorTarget);
    }

    // -------------------------------------------------------------------- away

    [Fact]
    public void Away_LeavesScreenAndComesBackWithinFiveMinutes()
    {
        var b = Brain();
        Assert.True(b.BeginTrick(PetActivity.Away, Ctx(Start)));
        var sim = new Sim(b);
        sim.Run(30, until: () => !b.IsVisible);
        Assert.False(b.IsVisible);
        Assert.Equal(TrickPhase.Gone, b.Trick);
        sim.Run(PetBrain.AwayMaxSeconds + 30, dt: 0.25, until: () => b.Trick == TrickPhase.Return);
        Assert.Equal(TrickPhase.Return, b.Trick);
        Assert.True(b.IsVisible);
        sim.Run(30, until: () => b.Activity != PetActivity.Away);
        Assert.NotEqual(PetActivity.Away, b.Activity);
        Assert.InRange(b.Position.X, S / 2, 1000 - S / 2);
        Assert.Equal(700, b.Position.Y);
    }

    [Fact]
    public void Away_Reminder_RunsBackAndReminds()
    {
        var b = Brain();
        b.BeginTrick(PetActivity.Away, Ctx(Start));
        var sim = new Sim(b);
        sim.Run(30, until: () => !b.IsVisible);
        b.StartReminder();
        sim.Run(1);
        Assert.True(b.IsVisible);
        sim.Run(10, until: () => b.Activity == PetActivity.Remind);
        Assert.Equal(PetActivity.Remind, b.Activity);
        Assert.InRange(b.Position.X, S / 2, 1000 - S / 2);
    }

    [Fact]
    public void Away_Call_ComesBackEarly()
    {
        var b = Brain();
        b.BeginTrick(PetActivity.Away, Ctx(Start));
        var sim = new Sim(b);
        sim.Run(30, until: () => !b.IsVisible);
        b.Call();
        sim.Run(1);
        Assert.True(b.IsVisible);
        sim.Run(10, until: () => b.Activity != PetActivity.Away);
        Assert.NotEqual(PetActivity.Away, b.Activity);
    }

    [Fact]
    public void Away_StaysAwayDuringFullscreen()
    {
        var b = Brain();
        b.BeginTrick(PetActivity.Away, Ctx(Start));
        var sim = new Sim(b);
        sim.Run(30, until: () => !b.IsVisible);
        b.Update(Ctx(sim.Now, fullscreen: true), 0.05);
        Assert.Equal(PetActivity.Away, b.Activity);
        Assert.Equal(TrickPhase.Gone, b.Trick);
    }

    // ------------------------------------------------ interrupts on all tricks

    [Theory]
    [InlineData(PetActivity.WallClimb)]
    [InlineData(PetActivity.WebHang)]
    [InlineData(PetActivity.ScreenPounce)]
    public void Reminder_DuringTrick_ComesDownThenReminds(PetActivity trick)
    {
        var b = Brain(x: 300);
        b.BeginTrick(trick, Ctx(Start));
        var sim = new Sim(b);
        sim.Run(trick == PetActivity.ScreenPounce ? 1.2 : 12);
        b.StartReminder();
        sim.Run(10, until: () => b.Activity == PetActivity.Remind);
        Assert.Equal(PetActivity.Remind, b.Activity);
        Assert.Equal(Surface.Ground, b.Surface);
        Assert.Equal(700, b.Position.Y);
        Assert.Equal(1, b.Boost);
        Assert.True(b.IsVisible);
    }

    [Theory]
    [InlineData(PetActivity.WallClimb)]
    [InlineData(PetActivity.WebHang)]
    [InlineData(PetActivity.ScreenPounce)]
    public void Fullscreen_DuringTrick_HidesThenRestoresOnGround(PetActivity trick)
    {
        var b = Brain(x: 300);
        b.BeginTrick(trick, Ctx(Start));
        var sim = new Sim(b);
        sim.Run(trick == PetActivity.ScreenPounce ? 1.2 : 12);
        b.Update(Ctx(sim.Now, fullscreen: true), 0.05);
        Assert.Equal(PetActivity.Hidden, b.Activity);
        b.Update(Ctx(sim.Now.AddSeconds(1)), 0.05);
        Assert.Equal(Surface.Ground, b.Surface);
        Assert.Equal(700, b.Position.Y);
        Assert.Null(b.ThreadAnchor);
        Assert.True(b.IsVisible);
    }

    [Fact]
    public void Click_IgnoredDuringTricks()
    {
        var b = Brain();
        b.BeginTrick(PetActivity.WallClimb, Ctx(Start));
        b.Click();
        Assert.Equal(PetActivity.WallClimb, b.Activity);
    }

    // ------------------------------------------------------------- scheduling

    [Fact]
    public void Tricks_AllHappenWithinFourHours_WhenEnabled()
    {
        var b = Brain(rng: new ScriptedRandom { Fallback = 0.5 });
        var cursor = MouthAbove(560, 60);
        var sim = new Sim(b) { Context = t => Ctx(t, b.CursorTarget ?? cursor, idle: 60, tricks: new TrickSettings()) };
        sim.Run(4 * 3600, dt: 0.25);
        Assert.Contains(PetActivity.Away, sim.Seen);
        Assert.Contains(PetActivity.ScreenPounce, sim.Seen);
        Assert.Contains(PetActivity.CursorSteal, sim.Seen);
        Assert.True(sim.Seen.Contains(PetActivity.WallClimb) || sim.Seen.Contains(PetActivity.WebHang));
    }

    [Fact]
    public void Tricks_NeverHappen_WhenDisabled()
    {
        var b = Brain(rng: new ScriptedRandom { Fallback = 0.5 });
        var cursor = MouthAbove(560, 60);
        var sim = new Sim(b) { Context = t => Ctx(t, cursor, idle: 60, tricks: TrickSettings.AllOff) };
        sim.Run(4 * 3600, dt: 0.25);
        foreach (var a in new[] { PetActivity.Away, PetActivity.ScreenPounce, PetActivity.CursorSteal, PetActivity.WallClimb, PetActivity.WebHang })
            Assert.DoesNotContain(a, sim.Seen);
    }

    [Fact]
    public void Tricks_OnlyWebWhenWallDisabled()
    {
        var b = Brain(rng: new ScriptedRandom { Fallback = 0.5 });
        var tricks = TrickSettings.AllOff with { WebHang = true };
        var sim = new Sim(b) { Context = t => Ctx(t, tricks: tricks) };
        sim.Run(2 * 3600, dt: 0.25);
        Assert.Contains(PetActivity.WebHang, sim.Seen);
        Assert.DoesNotContain(PetActivity.WallClimb, sim.Seen);
    }

    // ----------------------------------------------------------------- outfits

    [Fact]
    public void Outfit_FixedMode_ChangesInAPuffWhenIdle()
    {
        var b = Brain();
        var sim = new Sim(b) { Context = t => Ctx(t, outfit: OutfitMode.Pilot) };
        sim.Run(0.1);
        Assert.Equal(PetActivity.ChangeOutfit, b.Activity);
        sim.Run(1.2);
        Assert.Equal(Outfit.Pilot, b.Outfit);
        Assert.NotEqual(PetActivity.ChangeOutfit, b.Activity);
        sim.Context = t => Ctx(t, outfit: OutfitMode.None);
        sim.Run(15, until: () => b.Outfit == Outfit.None);
        Assert.Equal(Outfit.None, b.Outfit);
    }

    [Fact]
    public void Outfit_NoneByDefault()
    {
        var b = Brain();
        new Sim(b).Run(5);
        Assert.Equal(Outfit.None, b.Outfit);
        Assert.DoesNotContain(PetActivity.ChangeOutfit, new[] { b.Activity });
    }

    // ------------------------------------------------------- review fixes

    [Fact]
    public void Steal_CursorMovedDuringApproach_NeverYanksItBack()
    {
        var b = Brain();
        var origin = MouthAbove(900, 60);
        var cursor = origin;
        b.BeginTrick(PetActivity.CursorSteal, Ctx(Start, cursor, idle: 30));
        var sim = new Sim(b) { Context = t => Ctx(t, cursor, idle: 30) };
        sim.Run(0.5);
        cursor = new Vec(origin.X - 40, origin.Y);     // user nudged the mouse; idle timer still high
        sim.Run(5);
        Assert.Null(b.CursorTarget);
        Assert.DoesNotContain(TrickPhase.Grab, sim.Phases);
        Assert.DoesNotContain(TrickPhase.Carry, sim.Phases);
    }

    [Fact]
    public void Steal_NeverWhileAMouseButtonIsHeld()
    {
        var b = Brain();
        Assert.False(b.BeginTrick(PetActivity.CursorSteal, Ctx(Start, MouthAbove(650, 50), idle: 30, button: true)));

        var cursor = MouthAbove(600, 60);
        var button = false;
        b.BeginTrick(PetActivity.CursorSteal, Ctx(Start, cursor, idle: 30));
        var sim = new Sim(b) { Context = t => Ctx(t, b.CursorTarget ?? cursor, idle: 30, button: button) };
        sim.Run(5, until: () => b.CursorTarget is not null);
        Assert.NotNull(b.CursorTarget);
        button = true;
        sim.Run(0.05);
        Assert.Null(b.CursorTarget);
        Assert.NotEqual(PetActivity.CursorSteal, b.Activity);
    }

    [Fact]
    public void Tricks_DisabledInSettingsMidway_StopRightAway()
    {
        var b = Brain();
        b.BeginTrick(PetActivity.Away, Ctx(Start));
        var sim = new Sim(b) { Context = t => Ctx(t, tricks: new TrickSettings()) };
        sim.Run(30, until: () => !b.IsVisible);
        sim.Context = t => Ctx(t, tricks: TrickSettings.AllOff with { WallClimb = true });
        sim.Run(1);
        Assert.True(b.IsVisible);
        sim.Run(10, until: () => b.Activity != PetActivity.Away);
        Assert.NotEqual(PetActivity.Away, b.Activity);

        var b2 = Brain(x: 300);
        b2.BeginTrick(PetActivity.WebHang, Ctx(Start));
        var sim2 = new Sim(b2) { Context = t => Ctx(t, tricks: new TrickSettings()) };
        sim2.Run(12);
        sim2.Context = t => Ctx(t, tricks: new TrickSettings { WebHang = false });
        sim2.Run(5, until: () => b2.Activity is not (PetActivity.WebHang or PetActivity.Fall or PetActivity.Land));
        Assert.Equal(Surface.Ground, b2.Surface);
        Assert.Equal(700, b2.Position.Y);
    }

    [Fact]
    public void Call_WhileWalkingBack_Hurries()
    {
        var b = Brain();
        b.BeginTrick(PetActivity.Away, Ctx(Start));
        var sim = new Sim(b);
        sim.Run(PetBrain.AwayMaxSeconds + 60, dt: 0.25, until: () => b.Trick == TrickPhase.Return);
        sim.Run(0.1);
        b.Call();
        var x0 = b.Position.X;
        sim.Run(0.5);
        Assert.True(Math.Abs(b.Position.X - x0) >= PetBrain.ReturnRunSpeed * 0.5 - 2 || b.Activity != PetActivity.Away);
    }
}
