using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using AlienCat.Core.Tasks;

namespace AlienCat.Core.Notion;

public interface INotionGateway
{
    Task<IReadOnlyList<CatTask>> QueryOpenTasksAsync(DateTimeOffset until, CancellationToken ct);
    Task MarkDoneAsync(string pageId, CancellationToken ct);
    Task SetDueAsync(string pageId, DateTimeOffset due, CancellationToken ct);
    Task<string> CreateNoteAsync(string text, CancellationToken ct);
    Task<SchemaCheck> CheckSchemaAsync(CancellationToken ct);
}

/// <summary>Notion REST client for the «Задачи котика» database (data-source API).</summary>
public sealed class NotionGateway : INotionGateway
{
    public const string ApiVersion = "2026-03-11";
    const string BaseUrl = "https://api.notion.com/v1/";
    const int MaxTitleLength = 2000;
    static readonly JsonSerializerOptions JsonOut = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    readonly HttpClient _http;
    readonly string _token;
    readonly string _databaseId;
    readonly TimeZoneInfo _zone;
    readonly Func<TimeSpan, CancellationToken, Task> _delay;
    string? _dataSourceId;

    public NotionGateway(HttpClient http, string token, string databaseUrlOrId, TimeZoneInfo zone,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _token = token.Trim();
        _databaseId = NotionIds.ParseDatabaseId(databaseUrlOrId);
        _zone = zone;
        _delay = delay ?? Task.Delay;
    }

    public async Task<IReadOnlyList<CatTask>> QueryOpenTasksAsync(DateTimeOffset until, CancellationToken ct)
    {
        var ds = await DataSourceIdAsync(ct);
        var tasks = new List<CatTask>();
        string? cursor = null;
        do
        {
            var body = new JsonObject
            {
                ["filter"] = new JsonObject
                {
                    ["and"] = new JsonArray(
                        new JsonObject { ["property"] = NotionFields.Done, ["checkbox"] = new JsonObject { ["equals"] = false } },
                        new JsonObject { ["property"] = NotionFields.Kind, ["select"] = new JsonObject { ["does_not_equal"] = NotionFields.KindNote } },
                        new JsonObject { ["property"] = NotionFields.Due, ["date"] = new JsonObject { ["on_or_before"] = Iso(until) } }),
                },
                ["sorts"] = new JsonArray(new JsonObject { ["property"] = NotionFields.Due, ["direction"] = "ascending" }),
                ["page_size"] = 100,
            };
            if (cursor is not null) body["start_cursor"] = cursor;

            using var doc = await SendAsync(HttpMethod.Post, $"data_sources/{ds}/query", body, ct);
            foreach (var page in doc.RootElement.GetProperty("results").EnumerateArray())
                if (NotionMapper.FromPage(page, _zone) is { } task) tasks.Add(task);

            cursor = doc.RootElement.TryGetProperty("next_cursor", out var nc) && nc.ValueKind == JsonValueKind.String
                ? nc.GetString()
                : null;
        } while (cursor is not null);
        return tasks;
    }

    public async Task MarkDoneAsync(string pageId, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["properties"] = new JsonObject { [NotionFields.Done] = new JsonObject { ["checkbox"] = true } },
        };
        using var _ = await SendAsync(HttpMethod.Patch, $"pages/{pageId}", body, ct);
    }

    public async Task SetDueAsync(string pageId, DateTimeOffset due, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                [NotionFields.Due] = new JsonObject { ["date"] = new JsonObject { ["start"] = Iso(due) } },
            },
        };
        using var _ = await SendAsync(HttpMethod.Patch, $"pages/{pageId}", body, ct);
    }

    public async Task<string> CreateNoteAsync(string text, CancellationToken ct)
    {
        var ds = await DataSourceIdAsync(ct);
        var title = text.Trim();
        if (title.Length > MaxTitleLength) title = title[..MaxTitleLength];
        var body = new JsonObject
        {
            ["parent"] = new JsonObject { ["type"] = "data_source_id", ["data_source_id"] = ds },
            ["properties"] = new JsonObject
            {
                [NotionFields.Title] = new JsonObject
                {
                    ["title"] = new JsonArray(new JsonObject { ["text"] = new JsonObject { ["content"] = title } }),
                },
                [NotionFields.Kind] = new JsonObject { ["select"] = new JsonObject { ["name"] = NotionFields.KindNote } },
            },
        };
        using var doc = await SendAsync(HttpMethod.Post, "pages", body, ct);
        return doc.RootElement.GetProperty("id").GetString() ?? "";
    }

    public async Task<SchemaCheck> CheckSchemaAsync(CancellationToken ct)
    {
        var ds = await DataSourceIdAsync(ct);
        using var doc = await SendAsync(HttpMethod.Get, $"data_sources/{ds}", null, ct);
        var props = doc.RootElement.GetProperty("properties");
        var missing = new List<string>();
        foreach (var (name, type) in NotionFields.Required)
        {
            var ok = props.TryGetProperty(name, out var p) && p.TryGetProperty("type", out var t) && t.GetString() == type;
            if (!ok) missing.Add(name);
        }
        return new SchemaCheck(missing.Count == 0, missing);
    }

    async Task<string> DataSourceIdAsync(CancellationToken ct)
    {
        if (_dataSourceId is not null) return _dataSourceId;
        using var doc = await SendAsync(HttpMethod.Get, $"databases/{_databaseId}", null, ct);
        if (!doc.RootElement.TryGetProperty("data_sources", out var list) || list.GetArrayLength() == 0)
            throw new NotionException(NotionErrorKind.Schema, "В базе нет источника данных.");
        _dataSourceId = list[0].GetProperty("id").GetString();
        return _dataSourceId!;
    }

    async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            request.Headers.Add("Notion-Version", ApiVersion);
            if (body is not null)
                request.Content = new StringContent(body.ToJsonString(JsonOut), Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new NotionException(NotionErrorKind.Network, "Нет связи с Notion.", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new NotionException(NotionErrorKind.Network, "Notion не отвечает.", ex);
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                    return JsonDocument.Parse(text);

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    if (wait > TimeSpan.FromSeconds(30)) wait = TimeSpan.FromSeconds(30);
                    await _delay(wait, ct);
                    continue;
                }
                throw ToException(response.StatusCode, text);
            }
        }
    }

    static NotionException ToException(HttpStatusCode status, string body)
    {
        var message = TryMessage(body) ?? status.ToString();
        return status switch
        {
            HttpStatusCode.Unauthorized => new NotionException(NotionErrorKind.Unauthorized, message),
            HttpStatusCode.Forbidden or HttpStatusCode.NotFound => new NotionException(NotionErrorKind.NotFound, message),
            HttpStatusCode.TooManyRequests => new NotionException(NotionErrorKind.RateLimited, message),
            HttpStatusCode.BadRequest => new NotionException(NotionErrorKind.Schema, message),
            >= HttpStatusCode.InternalServerError => new NotionException(NotionErrorKind.Network, message),
            _ => new NotionException(NotionErrorKind.Unknown, message),
        };
    }

    static string? TryMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string Iso(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
}
