using AlienCat.Core.Pet;
using AlienCat.Core.Tasks;

namespace AlienCat.Core.Storage;

public enum ActivityLevel { Calm, Normal, Playful }

public sealed record AppSettings
{
    public int Scale { get; init; } = 2;
    public ActivityLevel Activity { get; init; } = ActivityLevel.Normal;
    public bool CursorHunt { get; init; } = true;
    public QuietHours Quiet { get; init; } = new(false, new TimeOnly(23, 0), new TimeOnly(8, 0));
    public bool Autostart { get; init; }
    public string? ProtectedToken { get; init; }
    public string? DatabaseUrl { get; init; }
    public OutfitMode Outfit { get; init; } = OutfitMode.Auto;
    public TrickSettings Tricks { get; init; } = new();

    public bool HasNotion => !string.IsNullOrWhiteSpace(ProtectedToken) && !string.IsNullOrWhiteSpace(DatabaseUrl);
}
