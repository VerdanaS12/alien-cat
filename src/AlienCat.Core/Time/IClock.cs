namespace AlienCat.Core.Time;

public interface IClock
{
    DateTimeOffset Now { get; }
    TimeZoneInfo Zone { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
    public TimeZoneInfo Zone => TimeZoneInfo.Local;
}
