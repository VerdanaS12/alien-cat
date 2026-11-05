namespace AlienCat.Core.Storage;

public enum PendingOpKind { MarkDone, SetDue, CreateNote }

public sealed record PendingOp(PendingOpKind Kind, string? PageId, DateTimeOffset? NewDue, string? Text);

public sealed class AppState
{
    public Dictionary<string, DateTimeOffset> Snoozes { get; set; } = new();
    public List<PendingOp> Queue { get; set; } = new();
    public HashSet<string> HandledKeys { get; set; } = new();
    public DateOnly? LastMorning { get; set; }
}
