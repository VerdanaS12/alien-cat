using AlienCat.Core.Animation;
using AlienCat.Core.Pet;
using Xunit;

namespace AlienCat.Core.Tests.Animation;

public class AnimationCatalogTests
{
    static AnimationCatalog Real() =>
        AnimationCatalog.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "animations.json")));

    [Fact]
    public void Parse_RealFile_AllClipsHaveFramesInsideSheet()
    {
        var cat = Real();
        Assert.NotEmpty(cat.Clips);
        foreach (var clip in cat.Clips.Values)
        {
            var sheet = cat.Sheets[clip.Sheet];
            Assert.NotEmpty(clip.Frames);
            Assert.True(clip.FrameMs > 0, clip.Name);
            Assert.Equal(sheet.Cell, clip.FrameWidth);
            Assert.All(clip.Frames, f => Assert.InRange(f, 0, sheet.FrameCount - 1));
        }
    }

    [Fact]
    public void EveryActivity_HasClip()
    {
        var cat = Real();
        foreach (var a in Enum.GetValues<PetActivity>())
        {
            if (a == PetActivity.Hidden) continue;
            foreach (var phase in Enum.GetValues<HuntPhase>())
                Assert.NotNull(cat.ForActivity(a, sad: false, nagging: false, phase));
        }
    }

    [Fact]
    public void Mapping_UsesExpectedClips()
    {
        var cat = Real();
        Assert.Equal("sad", cat.ForActivity(PetActivity.Idle, true, false, HuntPhase.Crouch).Name);
        Assert.Equal("idle", cat.ForActivity(PetActivity.Idle, false, false, HuntPhase.Crouch).Name);
        Assert.Equal("remind_nag", cat.ForActivity(PetActivity.Remind, false, true, HuntPhase.Crouch).Name);
        Assert.Equal("hunt_pounce", cat.ForActivity(PetActivity.CursorHunt, false, false, HuntPhase.Pounce).Name);
        Assert.Equal("happy", cat.ForActivity(PetActivity.ButterflyHunt, false, false, HuntPhase.Caught).Name);
        Assert.Equal("walk", cat.ForActivity(PetActivity.MouseChase, false, false, HuntPhase.Crouch).Name);
        Assert.Equal("zzz", cat.OverlayFor(PetActivity.Sleep)!.Name);
        Assert.Equal("hearts", cat.OverlayFor(PetActivity.Purr)!.Name);
        Assert.Null(cat.OverlayFor(PetActivity.Walk));
        Assert.Equal("butterfly_fly", cat.ToyFor(PetActivity.ButterflyHunt)!.Name);
        Assert.Null(cat.ToyFor(PetActivity.Sit));
    }

    [Fact]
    public void FrameAt_LoopsOrHoldsLastFrame()
    {
        var json = """
            {"sheets":{"s.png":{"columns":4,"cell":64,"frameCount":8}},
             "clips":{"a":{"sheet":"s.png","frameWidth":64,"frameHeight":64,"frames":[1,2,3],"frameMs":100,"loop":true},
                      "b":{"sheet":"s.png","frameWidth":64,"frameHeight":64,"frames":[4,5],"frameMs":100,"loop":false}}}
            """;
        var cat = AnimationCatalog.Parse(json);
        Assert.Equal(1, cat.Get("a").FrameAt(0.05));
        Assert.Equal(3, cat.Get("a").FrameAt(0.25));
        Assert.Equal(1, cat.Get("a").FrameAt(0.31));
        Assert.Equal(5, cat.Get("b").FrameAt(10));
        Assert.Equal((128, 64), cat.Get("b").CellOrigin(6, cat.Sheets["s.png"]));
    }

    [Fact]
    public void Get_Unknown_Throws() =>
        Assert.Throws<KeyNotFoundException>(() => Real().Get("nope"));
}
