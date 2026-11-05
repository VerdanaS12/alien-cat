using AlienCat.Core.Storage;

namespace AlienCat.Core.Pet;

public enum PetActivity
{
    Idle, Walk, Sit, Groom, Stretch, Yawn, Sleep, Fly, Dragged, Fall, Land, Purr, Remind, Happy,
    YarnPlay, ButterflyHunt, MouseChase, CursorHunt, Hidden,
}

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
    bool HasOverdue);

public interface IRandom
{
    double NextDouble();
}

public sealed class SystemRandom : IRandom
{
    public double NextDouble() => Random.Shared.NextDouble();
}
