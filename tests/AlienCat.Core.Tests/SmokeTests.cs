using AlienCat.Core.Tests.Time;
using Xunit;

namespace AlienCat.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void FakeClock_ReturnsSetTime()
    {
        var t = FakeClock.At(2026, 11, 5, 12, 0);
        var clock = new FakeClock(t, FakeClock.Kyiv);
        Assert.Equal(t, clock.Now);
        clock.Now = t.AddMinutes(5);
        Assert.Equal(t.AddMinutes(5), clock.Now);
    }
}
