using System.Text.Json;
using System.Text.Json.Serialization;
using AlienCat.Core.Pet;

namespace AlienCat.Core.Animation;

public sealed record SpriteSheetInfo(int Columns, int Cell, int FrameCount);

public sealed record AnimationClip(string Name, string Sheet, int FrameWidth, int FrameHeight, IReadOnlyList<int> Frames, int FrameMs, bool Loop)
{
    /// <summary>Sheet frame index shown <paramref name="seconds"/> after the clip started.</summary>
    public int FrameAt(double seconds)
    {
        var step = (int)Math.Floor(seconds * 1000 / FrameMs);
        if (step < 0) step = 0;
        var i = Loop ? step % Frames.Count : Math.Min(step, Frames.Count - 1);
        return Frames[i];
    }

    public double DurationSeconds => Frames.Count * FrameMs / 1000.0;

    public (int X, int Y) CellOrigin(int frameIndex, SpriteSheetInfo sheet) =>
        ((frameIndex % sheet.Columns) * sheet.Cell, (frameIndex / sheet.Columns) * sheet.Cell);
}

public sealed class AnimationCatalog
{
    sealed record ClipDto(string Sheet, int FrameWidth, int FrameHeight, int[] Frames, int FrameMs, bool Loop);
    sealed record FileDto(Dictionary<string, SpriteSheetInfo> Sheets, Dictionary<string, ClipDto> Clips);

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.Strict };

    AnimationCatalog(IReadOnlyDictionary<string, SpriteSheetInfo> sheets, IReadOnlyDictionary<string, AnimationClip> clips)
    {
        Sheets = sheets;
        Clips = clips;
    }

    public IReadOnlyDictionary<string, SpriteSheetInfo> Sheets { get; }
    public IReadOnlyDictionary<string, AnimationClip> Clips { get; }

    public static AnimationCatalog Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<FileDto>(json, Options) ?? throw new InvalidDataException("animations.json is empty");
        var clips = dto.Clips.ToDictionary(
            kv => kv.Key,
            kv => new AnimationClip(kv.Key, kv.Value.Sheet, kv.Value.FrameWidth, kv.Value.FrameHeight, kv.Value.Frames, kv.Value.FrameMs, kv.Value.Loop));
        return new AnimationCatalog(dto.Sheets, clips);
    }

    public AnimationClip Get(string name) =>
        Clips.TryGetValue(name, out var c) ? c : throw new KeyNotFoundException($"No animation clip '{name}'");

    /// <summary>Sheet to draw a clip from: cat clips come from the sheet of the current outfit.</summary>
    public string SheetFor(AnimationClip clip, Outfit outfit) =>
        clip.Sheet == "cat.png" && outfit != Outfit.None ? $"cat_{outfit.ToString().ToLowerInvariant()}.png" : clip.Sheet;

    public AnimationClip ForActivity(PetActivity a, bool sad, bool nagging, HuntPhase phase, TrickPhase trick = TrickPhase.None) => Get(a switch
    {
        PetActivity.ScreenPounce => trick switch
        {
            TrickPhase.Leap => "pounce_leap",
            TrickPhase.Cling => "pounce_cling",
            _ => "pounce_slide",
        },
        PetActivity.WallClimb => trick == TrickPhase.Hold ? "idle" : "walk",
        PetActivity.WebHang => trick is TrickPhase.FlyUp or TrickPhase.Gone ? "fly" : "web_hang",
        PetActivity.CursorSteal => trick switch
        {
            TrickPhase.Grab => "hunt_pounce",
            TrickPhase.Carry => "carry",
            _ => "walk",
        },
        PetActivity.Away => trick == TrickPhase.FlyUp ? "fly" : "walk",
        PetActivity.ChangeOutfit => "sit",
        PetActivity.Idle => sad ? "sad" : "idle",
        PetActivity.Sit => sad ? "sad" : "sit",
        PetActivity.Walk => "walk",
        PetActivity.Groom => "groom",
        PetActivity.Stretch => "stretch",
        PetActivity.Yawn => "yawn",
        PetActivity.Sleep => "sleep",
        PetActivity.Fly => "fly",
        PetActivity.Dragged => "dragged",
        PetActivity.Fall => "fall",
        PetActivity.Land => "land",
        PetActivity.Purr => "purr",
        PetActivity.Remind => nagging ? "remind_nag" : "remind",
        PetActivity.Happy => "happy",
        PetActivity.YarnPlay => "yarn_play",
        PetActivity.MouseChase => phase == HuntPhase.Puzzled ? "hunt_puzzled" : "walk",
        PetActivity.ButterflyHunt or PetActivity.CursorHunt => phase switch
        {
            HuntPhase.Pounce => "hunt_pounce",
            HuntPhase.Puzzled => "hunt_puzzled",
            HuntPhase.Caught => "happy",
            _ => "hunt_crouch",
        },
        _ => "idle",
    });

    public AnimationClip? OverlayFor(PetActivity a) => a switch
    {
        PetActivity.Sleep => Get("zzz"),
        PetActivity.Purr => Get("hearts"),
        PetActivity.Happy => Get("sparkles"),
        PetActivity.ChangeOutfit => Get("puff"),
        _ => null,
    };

    public AnimationClip? ToyFor(PetActivity a) => a switch
    {
        PetActivity.YarnPlay => Get("yarn_roll"),
        PetActivity.ButterflyHunt => Get("butterfly_fly"),
        PetActivity.MouseChase => Get("toy_mouse_run"),
        _ => null,
    };
}
