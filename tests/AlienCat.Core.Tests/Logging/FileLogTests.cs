using AlienCat.Core.Logging;
using AlienCat.Core.Tests.Time;
using Xunit;

namespace AlienCat.Core.Tests.Logging;

public class FileLogTests : IDisposable
{
    readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void FileLog_WritesToDailyFile()
    {
        var clock = new FakeClock(FakeClock.At(2026, 11, 5, 10), FakeClock.Kyiv);
        var log = new FileLog(_dir.Path, clock);
        log.Info("привет");
        log.Error("сломалось", new InvalidOperationException("boom"));
        var text = File.ReadAllText(Path.Combine(_dir.Path, "2026-11-05.log"));
        Assert.Contains("INFO", text);
        Assert.Contains("привет", text);
        Assert.Contains("ERROR", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void FileLog_DeletesFilesOlderThan7Days()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "2026-10-28.log"), "old");
        File.WriteAllText(Path.Combine(_dir.Path, "2026-10-30.log"), "recent");
        File.WriteAllText(Path.Combine(_dir.Path, "notes.txt"), "keep");
        _ = new FileLog(_dir.Path, new FakeClock(FakeClock.At(2026, 11, 5, 10), FakeClock.Kyiv));
        Assert.False(File.Exists(Path.Combine(_dir.Path, "2026-10-28.log")));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "2026-10-30.log")));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "notes.txt")));
    }
}
