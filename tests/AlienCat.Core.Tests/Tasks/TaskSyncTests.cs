using AlienCat.Core.Notion;
using AlienCat.Core.Storage;
using AlienCat.Core.Tasks;
using AlienCat.Core.Tests.Time;
using Xunit;
using static AlienCat.Core.Tests.Time.FakeClock;

namespace AlienCat.Core.Tests.Tasks;

public class TaskSyncTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly FakeGateway _gw = new();
    readonly FakeClock _clock = new(At(2026, 11, 5, 10, 5), Kyiv);
    JsonFileStore<List<CatTask>> Cache => new(_dir.File("cache.json"), _ => { });
    JsonFileStore<AppState> StateStore => new(_dir.File("state.json"), _ => { });
    public void Dispose() => _dir.Dispose();

    TaskSync Sync() => new(_gw, Cache, StateStore, _clock);

    static CatTask T(string id, Repeat repeat = Repeat.None) =>
        new(id, "Задача " + id, At(2026, 11, 5, 10), false, Priority.Normal, repeat, ItemKind.Task);

    [Fact]
    public async Task Poll_Online_ReturnsTasksAndSavesCache()
    {
        _gw.Tasks = [T("a"), T("b")];
        var r = await Sync().PollAsync(default);
        Assert.Equal(SyncStatus.Online, r.Status);
        Assert.Equal(2, r.Tasks.Count);
        Assert.Equal(2, Cache.Load().Count);
    }

    [Fact]
    public async Task Poll_Network_ReturnsCacheOffline()
    {
        Cache.Save([T("cached")]);
        _gw.Failures.Enqueue(NotionErrorKind.Network);
        var r = await Sync().PollAsync(default);
        Assert.Equal(SyncStatus.Offline, r.Status);
        Assert.Equal("cached", Assert.Single(r.Tasks).Id);
    }

    [Fact]
    public async Task Poll_RateLimited_IsOffline()
    {
        _gw.Failures.Enqueue(NotionErrorKind.RateLimited);
        Assert.Equal(SyncStatus.Offline, (await Sync().PollAsync(default)).Status);
    }

    [Fact]
    public async Task Poll_Unauthorized_ErrorWithRussianMessage()
    {
        _gw.Failures.Enqueue(NotionErrorKind.Unauthorized);
        var r = await Sync().PollAsync(default);
        Assert.Equal(SyncStatus.Error, r.Status);
        Assert.Equal("Ключ Notion не подходит. Проверьте его в настройках.", r.Message);
    }

    [Fact]
    public async Task Poll_NotFound_ErrorWithRussianMessage()
    {
        _gw.Failures.Enqueue(NotionErrorKind.NotFound);
        var r = await Sync().PollAsync(default);
        Assert.Equal(SyncStatus.Error, r.Status);
        Assert.Equal("Котик не видит базу. Дайте интеграции доступ к базе в Notion.", r.Message);
    }

    [Fact]
    public async Task Poll_Schema_ListsMissingFields()
    {
        _gw.Failures.Enqueue(NotionErrorKind.Schema);
        _gw.Schema = new SchemaCheck(false, ["Готово", "Повтор"]);
        var r = await Sync().PollAsync(default);
        Assert.Equal(SyncStatus.Error, r.Status);
        Assert.Equal("В базе не хватает полей: Готово, Повтор.", r.Message);
    }

    [Fact]
    public async Task Complete_NonRepeating_MarksDone()
    {
        _gw.Tasks = [T("a"), T("b")];
        var sync = Sync();
        await sync.PollAsync(default);
        Assert.Equal(SyncStatus.Online, await sync.CompleteAsync(sync.Current[0], default));
        Assert.Contains("done:a", _gw.Calls);
        Assert.DoesNotContain(sync.Current, t => t.Id == "a");
    }

    [Fact]
    public async Task Complete_Repeating_MovesDate()
    {
        _gw.Tasks = [T("a", Repeat.Daily)];
        var sync = Sync();
        await sync.PollAsync(default);
        await sync.CompleteAsync(sync.Current[0], default);
        Assert.Contains("due:a:2026-11-06T10:00", _gw.Calls);
        Assert.DoesNotContain(_gw.Calls, c => c.StartsWith("done:"));
        Assert.Equal(At(2026, 11, 6, 10), Assert.Single(sync.Current).Due);
    }

    [Fact]
    public async Task Complete_Offline_QueuesAndRemovesFromCurrent()
    {
        _gw.Tasks = [T("a")];
        var sync = Sync();
        await sync.PollAsync(default);
        _gw.Failures.Enqueue(NotionErrorKind.Network);
        Assert.Equal(SyncStatus.Offline, await sync.CompleteAsync(sync.Current[0], default));
        Assert.Empty(sync.Current);
        Assert.Equal(new PendingOp(PendingOpKind.MarkDone, "a", null, null), Assert.Single(StateStore.Load().Queue));
        Assert.Empty(Cache.Load());
    }

    [Fact]
    public async Task Poll_FlushesQueueBeforeQuery_InOrder()
    {
        var st = new AppState();
        st.Queue.Add(new PendingOp(PendingOpKind.MarkDone, "a", null, null));
        st.Queue.Add(new PendingOp(PendingOpKind.CreateNote, null, null, "идея"));
        StateStore.Save(st);
        await Sync().PollAsync(default);
        Assert.Equal(new[] { "done:a", "note:идея", "query" }, _gw.Calls.ToArray());
        Assert.Empty(StateStore.Load().Queue);
    }

    [Fact]
    public async Task Poll_QueueFlushFails_KeepsRemainingOps()
    {
        var st = new AppState();
        st.Queue.Add(new PendingOp(PendingOpKind.MarkDone, "a", null, null));
        st.Queue.Add(new PendingOp(PendingOpKind.MarkDone, "b", null, null));
        StateStore.Save(st);
        _gw.Failures.Enqueue(null);
        _gw.Failures.Enqueue(NotionErrorKind.Network);
        var r = await Sync().PollAsync(default);
        Assert.Equal(SyncStatus.Offline, r.Status);
        Assert.Equal("b", Assert.Single(StateStore.Load().Queue).PageId);
        Assert.DoesNotContain("query", _gw.Calls);
    }

    [Fact]
    public async Task Poll_QueuedOpForDeletedPage_IsDropped()
    {
        var st = new AppState();
        st.Queue.Add(new PendingOp(PendingOpKind.MarkDone, "gone", null, null));
        StateStore.Save(st);
        _gw.Failures.Enqueue(NotionErrorKind.NotFound);
        var r = await Sync().PollAsync(default);
        Assert.Equal(SyncStatus.Online, r.Status);
        Assert.Empty(StateStore.Load().Queue);
    }

    [Fact]
    public async Task AddNote_Offline_Queues()
    {
        _gw.Failures.Enqueue(NotionErrorKind.Network);
        var sync = Sync();
        Assert.Equal(SyncStatus.Offline, await sync.AddNoteAsync("купить корм", default));
        Assert.Equal(new PendingOp(PendingOpKind.CreateNote, null, null, "купить корм"), Assert.Single(StateStore.Load().Queue));
    }

    [Fact]
    public async Task AddNote_Online_Creates()
    {
        Assert.Equal(SyncStatus.Online, await Sync().AddNoteAsync("идея", default));
        Assert.Equal(new[] { "note:идея" }, _gw.Calls.ToArray());
    }

    [Fact]
    public async Task Complete_OfflineThenPoll_DoesNotResurrectTask()
    {
        _gw.Tasks = [T("a")];
        var sync = Sync();
        await sync.PollAsync(default);
        _gw.Failures.Enqueue(NotionErrorKind.Network);
        await sync.CompleteAsync(sync.Current[0], default);
        _gw.Failures.Enqueue(NotionErrorKind.Network);   // still offline on next poll
        var r = await sync.PollAsync(default);
        Assert.Empty(r.Tasks);
    }

    [Fact]
    public async Task AddNote_Unauthorized_KeepsNoteQueuedAndReportsError()
    {
        _gw.Failures.Enqueue(NotionErrorKind.Unauthorized);
        var sync = Sync();
        Assert.Equal(SyncStatus.Error, await sync.AddNoteAsync("идея", default));
        Assert.Equal("идея", Assert.Single(StateStore.Load().Queue).Text);
    }

    [Fact]
    public async Task Complete_UnknownError_QueuesInsteadOfLosing()
    {
        _gw.Tasks = [T("a")];
        var sync = Sync();
        await sync.PollAsync(default);
        _gw.Failures.Enqueue(NotionErrorKind.Unknown);
        Assert.Equal(SyncStatus.Offline, await sync.CompleteAsync(sync.Current[0], default));
        Assert.Equal("a", Assert.Single(StateStore.Load().Queue).PageId);
    }

    [Fact]
    public async Task Poll_FlushUnauthorized_KeepsQueueAndReportsError()
    {
        var st = new AppState();
        st.Queue.Add(new PendingOp(PendingOpKind.MarkDone, "a", null, null));
        StateStore.Save(st);
        _gw.Failures.Enqueue(NotionErrorKind.Unauthorized);
        var r = await Sync().PollAsync(default);
        Assert.Equal(SyncStatus.Error, r.Status);
        Assert.Equal(TaskSync.MsgUnauthorized, r.Message);
        Assert.Single(StateStore.Load().Queue);
    }

    [Fact]
    public async Task Poll_FlushNoteNotFound_KeepsNote()
    {
        var st = new AppState();
        st.Queue.Add(new PendingOp(PendingOpKind.CreateNote, null, null, "идея"));
        StateStore.Save(st);
        _gw.Failures.Enqueue(NotionErrorKind.NotFound);
        var r = await Sync().PollAsync(default);
        Assert.Equal(SyncStatus.Error, r.Status);
        Assert.Single(StateStore.Load().Queue);
    }
}
