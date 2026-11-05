using AlienCat.Core.Notion;
using AlienCat.Core.Storage;
using AlienCat.Core.Time;

namespace AlienCat.Core.Tasks;

public enum SyncStatus { Online, Offline, Error }

public sealed record SyncResult(IReadOnlyList<CatTask> Tasks, SyncStatus Status, string? Message);

/// <summary>Keeps the task list in step with Notion; works from the cache and a queue of changes when offline.</summary>
public sealed class TaskSync
{
    public const string MsgUnauthorized = "Ключ Notion не подходит. Проверьте его в настройках.";
    public const string MsgNotFound = "Котик не видит базу. Дайте интеграции доступ к базе в Notion.";
    public const string MsgMissingFields = "В базе не хватает полей: {0}.";
    public const string MsgUnknown = "Notion ответил ошибкой: {0}";

    readonly INotionGateway _gateway;
    readonly JsonFileStore<List<CatTask>> _cache;
    readonly JsonFileStore<AppState> _stateStore;
    readonly IClock _clock;
    List<CatTask> _current;

    public TaskSync(INotionGateway gateway, JsonFileStore<List<CatTask>> cache, JsonFileStore<AppState> state, IClock clock)
    {
        _gateway = gateway;
        _cache = cache;
        _stateStore = state;
        _clock = clock;
        _current = cache.Load();
        State = state.Load();
    }

    /// <summary>Shared app state (snoozes, handled reminders, queue). Call <see cref="SaveState"/> after changing it.</summary>
    public AppState State { get; }

    public IReadOnlyList<CatTask> Current => _current;

    public void SaveState() => _stateStore.Save(State);

    public async Task<SyncResult> PollAsync(CancellationToken ct)
    {
        try
        {
            if (!await FlushQueueAsync(ct))
                return new SyncResult(_current, SyncStatus.Offline, null);

            var until = ReminderEngine.EndOfDay(_clock.Now, _clock.Zone);
            _current = (await _gateway.QueryOpenTasksAsync(until, ct)).ToList();
            _cache.Save(_current);
            return new SyncResult(_current, SyncStatus.Online, null);
        }
        catch (NotionException ex) when (ex.Kind is NotionErrorKind.Network or NotionErrorKind.RateLimited)
        {
            return new SyncResult(_current, SyncStatus.Offline, null);
        }
        catch (NotionException ex)
        {
            return new SyncResult(_current, SyncStatus.Error, await MessageForAsync(ex, ct));
        }
    }

    public async Task<SyncStatus> CompleteAsync(CatTask task, CancellationToken ct)
    {
        PendingOp op;
        if (task.Repeat == Repeat.None)
        {
            op = new PendingOp(PendingOpKind.MarkDone, task.Id, null, null);
            _current = _current.Where(t => t.Id != task.Id).ToList();
        }
        else
        {
            var next = Recurrence.NextOccurrence(task.Due ?? _clock.Now, task.Repeat, _clock.Now, _clock.Zone);
            op = new PendingOp(PendingOpKind.SetDue, task.Id, next, null);
            _current = _current.Select(t => t.Id == task.Id ? t with { Due = next } : t).ToList();
        }
        _cache.Save(_current);
        return await ApplyOrQueueAsync(op, ct);
    }

    public Task<SyncStatus> AddNoteAsync(string text, CancellationToken ct) =>
        ApplyOrQueueAsync(new PendingOp(PendingOpKind.CreateNote, null, null, text.Trim()), ct);

    async Task<SyncStatus> ApplyOrQueueAsync(PendingOp op, CancellationToken ct)
    {
        if (State.Queue.Count > 0)
        {
            State.Queue.Add(op); // keep order behind earlier offline changes
            SaveState();
            return SyncStatus.Offline;
        }
        try
        {
            await ApplyAsync(op, ct);
            return SyncStatus.Online;
        }
        catch (NotionException ex) when (ex.Kind is NotionErrorKind.Network or NotionErrorKind.RateLimited)
        {
            State.Queue.Add(op);
            SaveState();
            return SyncStatus.Offline;
        }
        catch (NotionException)
        {
            return SyncStatus.Error;
        }
    }

    /// <summary>Sends queued changes in order; false if the network failed part-way.</summary>
    async Task<bool> FlushQueueAsync(CancellationToken ct)
    {
        var changed = false;
        try
        {
            while (State.Queue.Count > 0)
            {
                try
                {
                    await ApplyAsync(State.Queue[0], ct);
                }
                catch (NotionException ex) when (ex.Kind is NotionErrorKind.NotFound or NotionErrorKind.Schema)
                {
                    // page deleted or change rejected: drop it so the queue cannot jam
                }
                State.Queue.RemoveAt(0);
                changed = true;
            }
            return true;
        }
        catch (NotionException ex) when (ex.Kind is NotionErrorKind.Network or NotionErrorKind.RateLimited)
        {
            return false;
        }
        finally
        {
            if (changed) SaveState();
        }
    }

    Task ApplyAsync(PendingOp op, CancellationToken ct) => op.Kind switch
    {
        PendingOpKind.MarkDone => _gateway.MarkDoneAsync(op.PageId!, ct),
        PendingOpKind.SetDue => _gateway.SetDueAsync(op.PageId!, op.NewDue!.Value, ct),
        PendingOpKind.CreateNote => _gateway.CreateNoteAsync(op.Text ?? "", ct),
        _ => Task.CompletedTask,
    };

    async Task<string> MessageForAsync(NotionException ex, CancellationToken ct)
    {
        switch (ex.Kind)
        {
            case NotionErrorKind.Unauthorized:
                return MsgUnauthorized;
            case NotionErrorKind.NotFound:
                return MsgNotFound;
            case NotionErrorKind.Schema:
                try
                {
                    var check = await _gateway.CheckSchemaAsync(ct);
                    if (!check.Ok) return string.Format(MsgMissingFields, string.Join(", ", check.MissingFields));
                }
                catch (NotionException) { }
                return string.Format(MsgUnknown, ex.Message);
            default:
                return string.Format(MsgUnknown, ex.Message);
        }
    }
}
