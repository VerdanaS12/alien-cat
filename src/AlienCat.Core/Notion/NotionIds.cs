using System.Text.RegularExpressions;

namespace AlienCat.Core.Notion;

public static partial class NotionIds
{
    [GeneratedRegex("[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}")]
    private static partial Regex IdPattern();

    /// <summary>Extracts a database id from a Notion link or raw id; returns a dashed lower-case UUID.</summary>
    public static string ParseDatabaseId(string urlOrId)
    {
        var text = (urlOrId ?? "").Trim();
        var q = text.IndexOfAny(['?', '#']);
        if (q >= 0) text = text[..q];
        var matches = IdPattern().Matches(text);
        if (matches.Count == 0) throw new ArgumentException("Не удалось найти идентификатор базы в ссылке.", nameof(urlOrId));
        var hex = matches[^1].Value.Replace("-", "").ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }
}
