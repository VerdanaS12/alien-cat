using AlienCat.Core.Storage;

namespace AlienCat.Core.Pet;

/// <summary>Kinds of rare tricks; wall climbing and the web share one timer.</summary>
public enum TrickKind { Climb, Away, ScreenPounce, CursorSteal }

/// <summary>Which tricks the user allows (settings). Defaults: all on.</summary>
public sealed record TrickSettings
{
    public bool WallClimb { get; init; } = true;
    public bool WebHang { get; init; } = true;
    public bool Away { get; init; } = true;
    public bool ScreenPounce { get; init; } = true;
    public bool CursorSteal { get; init; } = true;

    public static TrickSettings AllOff { get; } = new()
    {
        WallClimb = false, WebHang = false, Away = false, ScreenPounce = false, CursorSteal = false,
    };

    public bool Enabled(TrickKind kind) => kind switch
    {
        TrickKind.Climb => WallClimb || WebHang,
        TrickKind.Away => Away,
        TrickKind.ScreenPounce => ScreenPounce,
        TrickKind.CursorSteal => CursorSteal,
        _ => false,
    };
}

/// <summary>When each trick is next due. Intervals scale with the cat's character.</summary>
public sealed class TrickSchedule
{
    readonly IRandom _rng;
    readonly Dictionary<TrickKind, DateTimeOffset> _next = new();

    public TrickSchedule(IRandom rng, DateTimeOffset start, ActivityLevel level)
    {
        _rng = rng;
        foreach (var kind in Enum.GetValues<TrickKind>()) _next[kind] = start + Interval(kind, level);
    }

    public static (double Min, double Max) BaseMinutes(TrickKind kind) => kind switch
    {
        TrickKind.Climb => (15, 30),
        TrickKind.Away => (60, 120),
        TrickKind.ScreenPounce => (60, 120),
        TrickKind.CursorSteal => (120, 180),
        _ => (60, 120),
    };

    public static double LevelFactor(ActivityLevel level) => level switch
    {
        ActivityLevel.Calm => 2,
        ActivityLevel.Playful => 0.5,
        _ => 1,
    };

    public DateTimeOffset NextAt(TrickKind kind) => _next[kind];

    public bool IsDue(TrickKind kind, DateTimeOffset now) => now >= _next[kind];

    public void Done(TrickKind kind, DateTimeOffset now, ActivityLevel level) => _next[kind] = now + Interval(kind, level);

    TimeSpan Interval(TrickKind kind, ActivityLevel level)
    {
        var (min, max) = BaseMinutes(kind);
        var f = LevelFactor(level);
        return TimeSpan.FromMinutes((min + (max - min) * _rng.NextDouble()) * f);
    }
}
