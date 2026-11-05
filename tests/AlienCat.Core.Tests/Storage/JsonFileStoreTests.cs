using AlienCat.Core.Storage;
using AlienCat.Core.Tasks;
using Xunit;
using static AlienCat.Core.Tests.Time.FakeClock;

namespace AlienCat.Core.Tests.Storage;

public class JsonFileStoreTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly List<string> _log = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = new JsonFileStore<AppSettings>(_dir.File("settings.json"), _log.Add).Load();
        Assert.Equal(2, s.Scale);
        Assert.Equal(ActivityLevel.Normal, s.Activity);
        Assert.True(s.CursorHunt);
        Assert.False(s.Quiet.Enabled);
        Assert.Equal(new TimeOnly(23, 0), s.Quiet.Start);
        Assert.Equal(new TimeOnly(8, 0), s.Quiet.End);
        Assert.False(s.Autostart);
        Assert.Null(s.ProtectedToken);
        Assert.Null(s.DatabaseUrl);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_Settings()
    {
        var store = new JsonFileStore<AppSettings>(_dir.File("settings.json"), _log.Add);
        var s = new AppSettings
        {
            Scale = 3, Activity = ActivityLevel.Playful, CursorHunt = false,
            Quiet = new QuietHours(true, new TimeOnly(22, 30), new TimeOnly(7, 15)),
            Autostart = true, ProtectedToken = "abc", DatabaseUrl = "https://www.notion.so/x",
        };
        store.Save(s);
        Assert.Equal(s, store.Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_State()
    {
        var store = new JsonFileStore<AppState>(_dir.File("state.json"), _log.Add);
        var st = new AppState { LastMorning = new DateOnly(2026, 11, 5) };
        st.Snoozes["a"] = At(2026, 11, 5, 10);
        st.HandledKeys.Add("a|Due|x");
        st.Queue.Add(new PendingOp(PendingOpKind.SetDue, "p1", At(2026, 11, 6, 10), null));
        st.Queue.Add(new PendingOp(PendingOpKind.CreateNote, null, null, "заметка"));
        store.Save(st);
        var back = store.Load();
        Assert.Equal(new DateOnly(2026, 11, 5), back.LastMorning);
        Assert.Equal(At(2026, 11, 5, 10), back.Snoozes["a"]);
        Assert.Contains("a|Due|x", back.HandledKeys);
        Assert.Equal(st.Queue, back.Queue);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_TaskList()
    {
        var store = new JsonFileStore<List<CatTask>>(_dir.File("cache.json"), _log.Add);
        var list = new List<CatTask> { new("id", "Название", At(2026, 11, 5, 10), false, Priority.Urgent, Repeat.Weekdays, ItemKind.Task) };
        store.Save(list);
        Assert.Equal(list, store.Load());
    }

    [Fact]
    public void Load_CorruptFile_RenamesToBakAndReturnsDefaults()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, "{ not json");
        var s = new JsonFileStore<AppSettings>(path, _log.Add).Load();
        Assert.Equal(2, s.Scale);
        Assert.True(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path));
        Assert.Single(_log);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var path = Path.Combine(_dir.Path, "nested", "state.json");
        new JsonFileStore<AppState>(path, _log.Add).Save(new AppState());
        Assert.True(File.Exists(path));
    }
}
