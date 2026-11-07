using AlienCat.Core.Pet;
using Xunit;
using static AlienCat.Core.Tests.Time.FakeClock;

namespace AlienCat.Core.Tests.Pet;

public class OutfitPickerTests
{
    static readonly DateTimeOffset Nov = At(2026, 11, 7, 12);
    static readonly DateTimeOffset Jan = At(2027, 1, 15, 12);

    [Fact]
    public void IsWinter_DecemberToFebruary()
    {
        Assert.True(OutfitPicker.IsWinter(At(2026, 12, 1), Kyiv));
        Assert.True(OutfitPicker.IsWinter(At(2027, 2, 28, 23), Kyiv));
        Assert.False(OutfitPicker.IsWinter(At(2027, 3, 1), Kyiv));
        Assert.False(OutfitPicker.IsWinter(Nov, Kyiv));
    }

    [Fact]
    public void Auto_PicksAtStartThenAfterInterval()
    {
        var p = new OutfitPicker(new ScriptedRandom { Fallback = 0.0 }, Nov);
        var first = p.Desired(Nov, Kyiv, OutfitMode.Auto);
        Assert.NotNull(first);
        p.Wear(first!.Value);
        Assert.Null(p.Desired(Nov.AddMinutes(59), Kyiv, OutfitMode.Auto));
        var second = p.Desired(Nov.AddMinutes(60), Kyiv, OutfitMode.Auto);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Auto_NeverWinterOutsideSeason()
    {
        var rng = new ScriptedRandom();
        for (var i = 0; i < 200; i++)
        {
            rng.Push(i / 200.0);
            Assert.NotEqual(Outfit.Winter, new OutfitPicker(rng, Nov).Pick(Nov, Kyiv));
        }
    }

    [Fact]
    public void Auto_WinterSixtyPercentInSeason()
    {
        var winter = 0;
        for (var i = 0; i < 1000; i++)
        {
            var p = new OutfitPicker(new ScriptedRandom((i + 0.5) / 1000.0, 0.5), Jan);
            if (p.Pick(Jan, Kyiv) == Outfit.Winter) winter++;
        }
        Assert.InRange(winter, 590, 610);
    }

    [Fact]
    public void Pick_DoesNotRepeatCurrent()
    {
        var rng = new ScriptedRandom();
        var p = new OutfitPicker(rng, Nov);
        foreach (var current in new[] { Outfit.None, Outfit.Astronaut, Outfit.Pilot })
        {
            p.Wear(current);
            for (var i = 0; i < 50; i++)
            {
                rng.Push(i / 50.0);
                Assert.NotEqual(current, p.Pick(Nov, Kyiv));
            }
        }
    }

    [Fact]
    public void FixedModes_ReturnThatOutfitOnlyWhenDifferent()
    {
        var p = new OutfitPicker(new ScriptedRandom(), Nov);
        Assert.Null(p.Desired(Nov, Kyiv, OutfitMode.None));
        Assert.Equal(Outfit.Pilot, p.Desired(Nov, Kyiv, OutfitMode.Pilot));
        p.Wear(Outfit.Pilot);
        Assert.Null(p.Desired(Nov, Kyiv, OutfitMode.Pilot));
        Assert.Equal(Outfit.Winter, p.Desired(Nov, Kyiv, OutfitMode.Winter));
        Assert.Equal(Outfit.Astronaut, p.Desired(Nov, Kyiv, OutfitMode.Astronaut));
        Assert.Equal(Outfit.None, p.Desired(Nov, Kyiv, OutfitMode.None));
    }
}
