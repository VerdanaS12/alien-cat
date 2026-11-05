using System.Globalization;
using AlienCat.Core.Time;

namespace AlienCat.Core.Logging;

/// <summary>Daily log files (yyyy-MM-dd.log); files older than 7 days are removed on start.</summary>
public sealed class FileLog
{
    const int KeepDays = 7;
    readonly string _dir;
    readonly IClock _clock;
    readonly object _lock = new();

    public FileLog(string directory, IClock clock)
    {
        _dir = directory;
        _clock = clock;
        Directory.CreateDirectory(_dir);
        Cleanup();
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    void Write(string level, string message, Exception? ex)
    {
        var now = TimeZoneInfo.ConvertTime(_clock.Now, _clock.Zone);
        var line = $"{now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        if (ex is not null) line += Environment.NewLine + "    " + ex;
        lock (_lock)
        {
            try
            {
                File.AppendAllText(Path.Combine(_dir, $"{now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
            catch (IOException) { }
        }
    }

    void Cleanup()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.Now, _clock.Zone).DateTime);
        var oldest = today.AddDays(-KeepDays);
        foreach (var file in Directory.EnumerateFiles(_dir, "*.log"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d < oldest)
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }
}
