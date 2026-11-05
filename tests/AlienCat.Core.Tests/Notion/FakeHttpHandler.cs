using System.Net;

namespace AlienCat.Core.Tests.Notion;

public sealed record RecordedRequest(HttpMethod Method, string Url, Dictionary<string, string> Headers, string? Body);

public sealed class FakeHttpHandler : HttpMessageHandler
{
    readonly Queue<Func<HttpResponseMessage>> _responses = new();
    public List<RecordedRequest> Requests { get; } = new();

    public FakeHttpHandler Respond(HttpStatusCode status, string body, (string, string)? header = null)
    {
        _responses.Enqueue(() =>
        {
            var r = new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            if (header is { } h) r.Headers.TryAddWithoutValidation(h.Item1, h.Item2);
            return r;
        });
        return this;
    }

    public FakeHttpHandler Throw(Exception ex)
    {
        _responses.Enqueue(() => throw ex);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.ToString(), headers, body));
        if (_responses.Count == 0) throw new InvalidOperationException("No fake response queued for " + request.RequestUri);
        return _responses.Dequeue()();
    }
}
