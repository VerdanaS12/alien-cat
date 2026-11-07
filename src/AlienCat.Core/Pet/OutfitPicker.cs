namespace AlienCat.Core.Pet;

public enum Outfit { None, Astronaut, Pilot, Winter }

/// <summary>Settings choice: let the cat pick, or always wear one thing.</summary>
public enum OutfitMode { Auto, None, Astronaut, Pilot, Winter }

/// <summary>Decides what the cat wears. In Auto mode it changes every 60–120 minutes.</summary>
public sealed class OutfitPicker(IRandom rng, DateTimeOffset start)
{
    public const double WinterChance = 0.6;
    static readonly Outfit[] Everyday = [Outfit.None, Outfit.Astronaut, Outfit.Pilot];
    DateTimeOffset _nextChange = start;   // pick right away on the first call

    public Outfit Current { get; private set; } = Outfit.None;

    public static bool IsWinter(DateTimeOffset now, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(now, zone).Month is 12 or 1 or 2;

    /// <summary>The outfit the cat should change into now, or null to keep the current one.</summary>
    public Outfit? Desired(DateTimeOffset now, TimeZoneInfo zone, OutfitMode mode)
    {
        if (mode != OutfitMode.Auto)
        {
            var fixedOutfit = mode switch
            {
                OutfitMode.Astronaut => Outfit.Astronaut,
                OutfitMode.Pilot => Outfit.Pilot,
                OutfitMode.Winter => Outfit.Winter,
                _ => Outfit.None,
            };
            return fixedOutfit == Current ? null : fixedOutfit;
        }
        if (now < _nextChange) return null;
        _nextChange = now + TimeSpan.FromMinutes(60 + 60 * rng.NextDouble());
        var pick = Pick(now, zone);
        return pick == Current ? null : pick;
    }

    /// <summary>A random outfit other than the current one; in winter the winter set wins 60% of the time.</summary>
    public Outfit Pick(DateTimeOffset now, TimeZoneInfo zone)
    {
        if (IsWinter(now, zone) && Current != Outfit.Winter)
        {
            if (rng.NextDouble() < WinterChance) return Outfit.Winter;
        }
        var options = Everyday.Where(o => o != Current).ToArray();
        var i = (int)Math.Floor(rng.NextDouble() * options.Length);
        return options[Math.Clamp(i, 0, options.Length - 1)];
    }

    public void Wear(Outfit outfit) => Current = outfit;
}
