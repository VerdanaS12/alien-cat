namespace AlienCat.Core.Notion;

public enum NotionErrorKind { Unauthorized, NotFound, RateLimited, Network, Schema, Unknown }

public sealed class NotionException(NotionErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public NotionErrorKind Kind { get; } = kind;
}

public sealed record SchemaCheck(bool Ok, IReadOnlyList<string> MissingFields);
