using AlienCat.Core.Storage;

namespace AlienCat.Core.Pet;

public static class ActivityWeights
{
    static readonly PetActivity[] Order =
        [PetActivity.Walk, PetActivity.Sit, PetActivity.Groom, PetActivity.Stretch, PetActivity.Yawn, PetActivity.Sleep, PetActivity.Fly];

    //                                   Walk Sit Groom Stretch Yawn Sleep Fly
    static readonly double[] Calm =    [2,   3,  2,    1,      1,   3,    0.5];
    static readonly double[] Normal =  [4,   2,  2,    1,      1,   1,    1];
    static readonly double[] Playful = [5,   1,  1,    1,      0.5, 0.5,  2];

    public static IReadOnlyDictionary<PetActivity, double> For(ActivityLevel level, bool isNight)
    {
        var row = level switch { ActivityLevel.Calm => Calm, ActivityLevel.Playful => Playful, _ => Normal };
        var dict = new Dictionary<PetActivity, double>();
        for (var i = 0; i < Order.Length; i++)
        {
            var w = row[i];
            if (isNight && Order[i] is PetActivity.Sleep or PetActivity.Yawn) w *= 3;
            dict[Order[i]] = w;
        }
        return dict;
    }

    public static bool IsNight(DateTimeOffset now, TimeZoneInfo zone)
    {
        var h = TimeZoneInfo.ConvertTime(now, zone).Hour;
        return h >= 22 || h < 7;
    }

    /// <summary>Picks an activity for a uniform random value r in [0, 1).</summary>
    public static PetActivity Pick(IReadOnlyDictionary<PetActivity, double> weights, double r)
    {
        var total = weights.Values.Sum();
        var x = r * total;
        var acc = 0.0;
        foreach (var a in Order)
        {
            if (!weights.TryGetValue(a, out var w)) continue;
            acc += w;
            if (x < acc) return a;
        }
        return Order[^1];
    }

    public static (double Min, double Max) GameIntervalMinutes(ActivityLevel level) => level switch
    {
        ActivityLevel.Calm => (20, 30),
        ActivityLevel.Playful => (5, 10),
        _ => (10, 20),
    };
}
