using AlienCat.Core.Time;

namespace AlienCat.Core.Tests.Time;

public sealed class FakeClock(DateTimeOffset now, TimeZoneInfo zone) : IClock
{
    public DateTimeOffset Now { get; set; } = now;
    public TimeZoneInfo Zone { get; } = zone;

    public static TimeZoneInfo Kyiv { get; } = FindKyiv();

    static TimeZoneInfo FindKyiv()
    {
        foreach (var id in new[] { "Europe/Kyiv", "Europe/Kiev", "FLE Standard Time" })
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz)) return tz;
        throw new InvalidOperationException("Kyiv time zone not found");
    }

    /// <summary>Local time in Kyiv for the given wall-clock values.</summary>
    public static DateTimeOffset At(int y, int mo, int d, int h = 0, int mi = 0, int s = 0)
    {
        var local = new DateTime(y, mo, d, h, mi, s, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Kyiv.GetUtcOffset(local));
    }
}
