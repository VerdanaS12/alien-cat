using AlienCat.Core.Storage;

namespace AlienCat.Core.Pet;

public enum PetActivity
{
    Idle, Walk, Sit, Groom, Stretch, Yawn, Sleep, Fly, Dragged, Fall, Land, Purr, Remind, Happy,
    YarnPlay, ButterflyHunt, MouseChase, CursorHunt,
    ScreenPounce, WallClimb, WebHang, CursorSteal, Away, ChangeOutfit,
    Hidden,
}

/// <summary>What the cat is holding on to; decides how the sprite is placed and turned.</summary>
public enum Surface { Ground, LeftWall, RightWall, Glass, Thread }

/// <summary>Step inside a trick, used for animation choice.</summary>
public enum TrickPhase { None, Approach, Leap, Cling, Slide, Drop, Climb, Hold, Descend, FlyUp, Gone, Swing, Ascend, Grab, Carry, Leave, Return }

/// <summary>Sub-phase of hunting games, used to pick the animation.</summary>
public enum HuntPhase { Crouch, Pounce, Puzzled, Caught }

public readonly record struct Vec(double X, double Y)
{
    public double DistanceTo(Vec o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
}

public readonly record struct Bounds(double Left, double Top, double Right, double Bottom);

public sealed record PetContext(
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    Bounds WorkArea,
    double SpriteSize,
    ActivityLevel Level,
    bool CursorHuntEnabled,
    Vec? Cursor,
    double CursorStillSeconds,
    bool OtherAppFullscreen,
    bool HasOverdue,
    double UserIdleSeconds = 0,
    TrickSettings? Tricks = null,
    OutfitMode OutfitMode = OutfitMode.None,
    bool MouseButtonDown = false);

public interface IRandom
{
    double NextDouble();
}

public sealed class SystemRandom : IRandom
{
    public double NextDouble() => Random.Shared.NextDouble();
}
