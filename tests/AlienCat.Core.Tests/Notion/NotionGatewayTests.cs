using System.Net;
using System.Text.Json;
using AlienCat.Core.Notion;
using Xunit;
using static AlienCat.Core.Tests.Time.FakeClock;

namespace AlienCat.Core.Tests.Notion;

public class NotionGatewayTests
{
    const string DbUrl = "https://app.notion.com/p/0f1e2d3c4b5a69788796a5b4c3d2e1f0";
    const string Ds = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d";
    readonly FakeHttpHandler _http = new();
    readonly List<TimeSpan> _delays = new();

    NotionGateway Gateway() => new(new HttpClient(_http), "secret_token", DbUrl, Kyiv,
        (d, _) => { _delays.Add(d); return Task.CompletedTask; });

    static readonly DateTimeOffset Until = At(2026, 11, 6, 0);

    [Fact]
    public async Task Query_SendsAuthAndVersionHeaders()
    {
        _http.Respond(HttpStatusCode.OK, Fixtures.Database).Respond(HttpStatusCode.OK, Fixtures.QueryResult("null", Fixtures.Page()));
        var tasks = await Gateway().QueryOpenTasksAsync(Until, default);
        Assert.Single(tasks);
        var q = _http.Requests[1];
        Assert.Equal("Bearer secret_token", q.Headers["Authorization"]);
        Assert.Equal(NotionGateway.ApiVersion, q.Headers["Notion-Version"]);
        Assert.Equal(HttpMethod.Post, q.Method);
        Assert.Equal($"https://api.notion.com/v1/data_sources/{Ds}/query", q.Url);
        Assert.Contains("Готово", q.Body);
        Assert.Contains("on_or_before", q.Body);
    }

    [Fact]
    public async Task Query_ResolvesDataSourceOnce()
    {
        _http.Respond(HttpStatusCode.OK, Fixtures.Database)
            .Respond(HttpStatusCode.OK, Fixtures.QueryResult("null"))
            .Respond(HttpStatusCode.OK, Fixtures.QueryResult("null"));
        var g = Gateway();
        await g.QueryOpenTasksAsync(Until, default);
        await g.QueryOpenTasksAsync(Until, default);
        Assert.Single(_http.Requests, r => r.Url.Contains("/v1/databases/"));
        Assert.Equal("https://api.notion.com/v1/databases/0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0", _http.Requests[0].Url);
    }

    [Fact]
    public async Task Query_FollowsCursorAcrossPages()
    {
        _http.Respond(HttpStatusCode.OK, Fixtures.Database)
            .Respond(HttpStatusCode.OK, Fixtures.QueryResult("\"cur-2\"", Fixtures.Page(id: "a")))
            .Respond(HttpStatusCode.OK, Fixtures.QueryResult("null", Fixtures.Page(id: "b"), Fixtures.Page(id: "c", trashed: true)));
        var tasks = await Gateway().QueryOpenTasksAsync(Until, default);
        Assert.Equal(new[] { "a", "b" }, tasks.Select(t => t.Id).ToArray());
        Assert.Contains("cur-2", _http.Requests[2].Body);
    }

    [Fact]
    public async Task Query_401_ThrowsUnauthorized()
    {
        _http.Respond(HttpStatusCode.Unauthorized, Fixtures.Error401);
        var ex = await Assert.ThrowsAsync<NotionException>(() => Gateway().QueryOpenTasksAsync(Until, default));
        Assert.Equal(NotionErrorKind.Unauthorized, ex.Kind);
    }

    [Fact]
    public async Task Query_404_ThrowsNotFound()
    {
        _http.Respond(HttpStatusCode.NotFound, Fixtures.Error404);
        var ex = await Assert.ThrowsAsync<NotionException>(() => Gateway().QueryOpenTasksAsync(Until, default));
        Assert.Equal(NotionErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task Query_HttpRequestException_ThrowsNetwork()
    {
        _http.Throw(new HttpRequestException("no route"));
        var ex = await Assert.ThrowsAsync<NotionException>(() => Gateway().QueryOpenTasksAsync(Until, default));
        Assert.Equal(NotionErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task Query_Timeout_ThrowsNetwork()
    {
        _http.Throw(new TaskCanceledException("timeout"));
        var ex = await Assert.ThrowsAsync<NotionException>(() => Gateway().QueryOpenTasksAsync(Until, default));
        Assert.Equal(NotionErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task Query_500_ThrowsNetwork()
    {
        _http.Respond(HttpStatusCode.ServiceUnavailable, "{}");
        var ex = await Assert.ThrowsAsync<NotionException>(() => Gateway().QueryOpenTasksAsync(Until, default));
        Assert.Equal(NotionErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task Query_429_RetriesOnceThenRateLimited()
    {
        _http.Respond(HttpStatusCode.TooManyRequests, Fixtures.Error429, ("Retry-After", "2"))
            .Respond(HttpStatusCode.OK, Fixtures.Database)
            .Respond(HttpStatusCode.OK, Fixtures.QueryResult("null"));
        await Gateway().QueryOpenTasksAsync(Until, default);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, _delays.ToArray());

        var http2 = new FakeHttpHandler()
            .Respond(HttpStatusCode.TooManyRequests, Fixtures.Error429)
            .Respond(HttpStatusCode.TooManyRequests, Fixtures.Error429);
        var g2 = new NotionGateway(new HttpClient(http2), "t", DbUrl, Kyiv, (_, _) => Task.CompletedTask);
        var ex = await Assert.ThrowsAsync<NotionException>(() => g2.QueryOpenTasksAsync(Until, default));
        Assert.Equal(NotionErrorKind.RateLimited, ex.Kind);
    }

    [Fact]
    public async Task MarkDone_PatchesCheckbox()
    {
        _http.Respond(HttpStatusCode.OK, Fixtures.Page());
        await Gateway().MarkDoneAsync("page-1", default);
        var r = Assert.Single(_http.Requests);
        Assert.Equal(HttpMethod.Patch, r.Method);
        Assert.Equal("https://api.notion.com/v1/pages/page-1", r.Url);
        var props = JsonDocument.Parse(r.Body!).RootElement.GetProperty("properties");
        Assert.True(props.GetProperty("Готово").GetProperty("checkbox").GetBoolean());
    }

    [Fact]
    public async Task SetDue_PatchesDateWithOffset()
    {
        _http.Respond(HttpStatusCode.OK, Fixtures.Page());
        await Gateway().SetDueAsync("page-1", At(2026, 11, 6, 10), default);
        var props = JsonDocument.Parse(_http.Requests[0].Body!).RootElement.GetProperty("properties");
        Assert.Equal("2026-11-06T10:00:00+02:00", props.GetProperty("Напомнить").GetProperty("date").GetProperty("start").GetString());
    }

    [Fact]
    public async Task CreateNote_PostsTitleAndKind()
    {
        _http.Respond(HttpStatusCode.OK, Fixtures.Database).Respond(HttpStatusCode.OK, Fixtures.CreatedPage);
        var id = await Gateway().CreateNoteAsync("Купить корм", default);
        Assert.Equal("99999999-8888-7777-6666-555555555555", id);
        var r = _http.Requests[1];
        Assert.Equal("https://api.notion.com/v1/pages", r.Url);
        var root = JsonDocument.Parse(r.Body!).RootElement;
        Assert.Equal(Ds, root.GetProperty("parent").GetProperty("data_source_id").GetString());
        var props = root.GetProperty("properties");
        Assert.Equal("Купить корм", props.GetProperty("Название").GetProperty("title")[0].GetProperty("text").GetProperty("content").GetString());
        Assert.Equal("Заметка", props.GetProperty("Тип").GetProperty("select").GetProperty("name").GetString());
    }

    [Fact]
    public async Task CheckSchema_AllPresent_Ok()
    {
        _http.Respond(HttpStatusCode.OK, Fixtures.Database).Respond(HttpStatusCode.OK, Fixtures.DataSource());
        var check = await Gateway().CheckSchemaAsync(default);
        Assert.True(check.Ok);
        Assert.Empty(check.MissingFields);
    }

    [Fact]
    public async Task CheckSchema_ReportsMissingFields()
    {
        _http.Respond(HttpStatusCode.OK, Fixtures.Database).Respond(HttpStatusCode.OK, Fixtures.DataSource(complete: false));
        var check = await Gateway().CheckSchemaAsync(default);
        Assert.False(check.Ok);
        Assert.Equal(new[] { "Готово", "Повтор" }, check.MissingFields.ToArray());
    }

    [Fact]
    public async Task Patch_409Conflict_IsRetryableNetwork()
    {
        _http.Respond(HttpStatusCode.Conflict, "{\"object\":\"error\",\"status\":409,\"code\":\"conflict_error\",\"message\":\"Conflict\"}");
        var ex = await Assert.ThrowsAsync<NotionException>(() => Gateway().MarkDoneAsync("p", default));
        Assert.Equal(NotionErrorKind.Network, ex.Kind);
    }
}
