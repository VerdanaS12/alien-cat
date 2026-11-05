using System.Globalization;
using System.Text.Json;
using AlienCat.Core.Tasks;

namespace AlienCat.Core.Notion;

public static class NotionMapper
{
    public static readonly TimeOnly AllDayTime = new(9, 0);

    /// <summary>Maps a Notion page to a task; null for pages in the trash.</summary>
    public static CatTask? FromPage(JsonElement page, TimeZoneInfo zone)
    {
        if (Bool(page, "in_trash") || Bool(page, "archived")) return null;
        var id = page.GetProperty("id").GetString() ?? "";
        var props = page.GetProperty("properties");

        return new CatTask(
            id,
            ReadTitle(props),
            ReadDate(props, zone),
            props.TryGetProperty(NotionFields.Done, out var done) && done.TryGetProperty("checkbox", out var cb) && cb.ValueKind == JsonValueKind.True,
            NotionFields.ParsePriority(ReadSelect(props, NotionFields.Priority)),
            NotionFields.ParseRepeat(ReadSelect(props, NotionFields.Repeat)),
            NotionFields.ParseKind(ReadSelect(props, NotionFields.Kind)));
    }

    public static DateTimeOffset? ParseNotionDate(string? start, TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(start)) return null;
        if (start.Length == 10 && DateTime.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Recurrence.ToOffset(day.Add(AllDayTime.ToTimeSpan()), zone);

        var hasOffset = start.EndsWith('Z') || HasExplicitOffset(start);
        if (hasOffset)
            return DateTimeOffset.Parse(start, CultureInfo.InvariantCulture, DateTimeStyles.None);

        var naive = DateTime.Parse(start, CultureInfo.InvariantCulture, DateTimeStyles.None);
        return Recurrence.ToOffset(naive, zone);
    }

    static bool HasExplicitOffset(string s)
    {
        var t = s.IndexOf('T');
        if (t < 0) return false;
        var time = s[(t + 1)..];
        return time.Contains('+') || time.Contains('-');
    }

    static string ReadTitle(JsonElement props)
    {
        if (!props.TryGetProperty(NotionFields.Title, out var p) || !p.TryGetProperty("title", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return "";
        return string.Concat(arr.EnumerateArray().Select(x => x.TryGetProperty("plain_text", out var t) ? t.GetString() : ""));
    }

    static DateTimeOffset? ReadDate(JsonElement props, TimeZoneInfo zone)
    {
        if (!props.TryGetProperty(NotionFields.Due, out var p) || !p.TryGetProperty("date", out var d) || d.ValueKind != JsonValueKind.Object)
            return null;
        return d.TryGetProperty("start", out var s) ? ParseNotionDate(s.GetString(), zone) : null;
    }

    static string? ReadSelect(JsonElement props, string name)
    {
        if (!props.TryGetProperty(name, out var p) || !p.TryGetProperty("select", out var s) || s.ValueKind != JsonValueKind.Object)
            return null;
        return s.TryGetProperty("name", out var n) ? n.GetString() : null;
    }

    static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
