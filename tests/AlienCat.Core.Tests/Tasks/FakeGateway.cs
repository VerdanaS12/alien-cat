using AlienCat.Core.Notion;
using AlienCat.Core.Tasks;

namespace AlienCat.Core.Tests.Tasks;

public sealed class FakeGateway : INotionGateway
{
    public List<CatTask> Tasks { get; set; } = new();
    public List<string> Calls { get; } = new();
    /// <summary>Errors thrown by the next calls, in order (null = succeed).</summary>
    public Queue<NotionErrorKind?> Failures { get; } = new();
    public SchemaCheck Schema { get; set; } = new(true, []);

    void Step(string call)
    {
        Calls.Add(call);
        if (Failures.Count > 0 && Failures.Dequeue() is { } kind)
            throw new NotionException(kind, kind.ToString());
    }

    public Task<IReadOnlyList<CatTask>> QueryOpenTasksAsync(DateTimeOffset until, CancellationToken ct)
    {
        Step("query");
        return Task.FromResult<IReadOnlyList<CatTask>>(Tasks.ToList());
    }

    public Task MarkDoneAsync(string pageId, CancellationToken ct) { Step("done:" + pageId); return Task.CompletedTask; }

    public Task SetDueAsync(string pageId, DateTimeOffset due, CancellationToken ct)
    {
        Step($"due:{pageId}:{due:yyyy-MM-ddTHH:mm}");
        return Task.CompletedTask;
    }

    public Task<string> CreateNoteAsync(string text, CancellationToken ct) { Step("note:" + text); return Task.FromResult("new-id"); }

    public Task<SchemaCheck> CheckSchemaAsync(CancellationToken ct) { Calls.Add("schema"); return Task.FromResult(Schema); }
}
