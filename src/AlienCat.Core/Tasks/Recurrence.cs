namespace AlienCat.Core.Tasks;

public static class Recurrence
{
    /// <summary>First occurrence strictly after <paramref name="now"/>, keeping the local time of day.</summary>
    public static DateTimeOffset NextOccurrence(DateTimeOffset due, Repeat repeat, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (repeat == Repeat.None) throw new ArgumentException("Task does not repeat", nameof(repeat));

        var local = TimeZoneInfo.ConvertTime(due, zone).DateTime;
        var anchorDay = local.Day;
        var monthsAdded = 0;
        var candidate = local;
        for (var guard = 0; guard < 10_000; guard++)
        {
            switch (repeat)
            {
                case Repeat.Daily:
                    candidate = candidate.AddDays(1);
                    break;
                case Repeat.Weekdays:
                    do candidate = candidate.AddDays(1);
                    while (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
                    break;
                case Repeat.Weekly:
                    candidate = candidate.AddDays(7);
                    break;
                case Repeat.Monthly:
                    monthsAdded++;
                    var month = new DateTime(local.Year, local.Month, 1).AddMonths(monthsAdded);
                    var day = Math.Min(anchorDay, DateTime.DaysInMonth(month.Year, month.Month));
                    candidate = new DateTime(month.Year, month.Month, day, local.Hour, local.Minute, local.Second);
                    break;
            }

            var result = ToOffset(candidate, zone);
            if (result > now) return result;
        }
        throw new InvalidOperationException("No next occurrence found");
    }

    internal static DateTimeOffset ToOffset(DateTime localWallClock, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1); // skipped by DST
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }
}
