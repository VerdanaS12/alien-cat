using AlienCat.Core.Tasks;
using Xunit;
using static AlienCat.Core.Tests.Time.FakeClock;

namespace AlienCat.Core.Tests.Tasks;

public class RecurrenceTests
{
    [Fact]
    public void Daily_NextDayAfterNow() =>
        Assert.Equal(At(2026, 11, 6, 10), Recurrence.NextOccurrence(At(2026, 11, 5, 10), Repeat.Daily, At(2026, 11, 5, 12), Kyiv));

    [Fact]
    public void Daily_SkipsManyMissedDays() =>
        Assert.Equal(At(2026, 11, 6, 10), Recurrence.NextOccurrence(At(2026, 11, 1, 10), Repeat.Daily, At(2026, 11, 5, 12), Kyiv));

    [Fact]
    public void Weekdays_FridaySkipsToMonday() =>
        Assert.Equal(At(2026, 11, 9, 9), Recurrence.NextOccurrence(At(2026, 11, 6, 9), Repeat.Weekdays, At(2026, 11, 6, 9, 5), Kyiv));

    [Fact]
    public void Weekly_SameWeekday() =>
        Assert.Equal(At(2026, 11, 12, 18), Recurrence.NextOccurrence(At(2026, 11, 5, 18), Repeat.Weekly, At(2026, 11, 5, 18, 1), Kyiv));

    [Fact]
    public void Monthly_31stToShortMonth() =>
        Assert.Equal(At(2026, 2, 28, 10), Recurrence.NextOccurrence(At(2026, 1, 31, 10), Repeat.Monthly, At(2026, 1, 31, 11), Kyiv));

    [Fact]
    public void Monthly_NormalDay() =>
        Assert.Equal(At(2026, 12, 5, 10), Recurrence.NextOccurrence(At(2026, 11, 5, 10), Repeat.Monthly, At(2026, 11, 5, 11), Kyiv));

    [Fact]
    public void KeepsLocalTimeAcrossDst()
    {
        var next = Recurrence.NextOccurrence(At(2026, 10, 24, 10), Repeat.Daily, At(2026, 10, 25, 8), Kyiv);
        Assert.Equal(At(2026, 10, 25, 10), next);
        Assert.Equal(TimeSpan.FromHours(2), next.Offset);
    }

    [Fact]
    public void None_Throws() =>
        Assert.Throws<ArgumentException>(() => Recurrence.NextOccurrence(At(2026, 11, 5, 10), Repeat.None, At(2026, 11, 5, 11), Kyiv));
}
