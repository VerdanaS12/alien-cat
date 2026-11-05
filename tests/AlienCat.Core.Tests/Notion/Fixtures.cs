namespace AlienCat.Core.Tests.Notion;

internal static class Fixtures
{
    public static string Page(string id = "11111111-2222-3333-4444-555555555555", string title = "Позвонить маме",
        string? date = "\"2026-11-05T10:00:00.000+02:00\"", bool done = false, string? priority = "\"Важный\"",
        string? repeat = "\"Каждый день\"", string? kind = "\"Задача\"", bool trashed = false)
    {
        string Select(string? name) => name is null ? "null" : $"{{\"id\":\"x\",\"name\":{name},\"color\":\"blue\"}}";
        var dateJson = date is null ? "null" : $"{{\"start\":{date},\"end\":null,\"time_zone\":null}}";
        return $$"""
        {
          "object": "page",
          "id": "{{id}}",
          "in_trash": {{(trashed ? "true" : "false")}},
          "properties": {
            "Название": {"id":"title","type":"title","title":[{"type":"text","text":{"content":"{{title}}"},"plain_text":"{{title}}"}]},
            "Напомнить": {"id":"d","type":"date","date":{{dateJson}}},
            "Готово": {"id":"c","type":"checkbox","checkbox":{{(done ? "true" : "false")}}},
            "Приоритет": {"id":"p","type":"select","select":{{Select(priority)}}},
            "Повтор": {"id":"r","type":"select","select":{{Select(repeat)}}},
            "Тип": {"id":"t","type":"select","select":{{Select(kind)}}}
          }
        }
        """;
    }

    public static string QueryResult(string nextCursor, params string[] pages) =>
        "{\"object\":\"list\",\"results\":[" + string.Join(",", pages) + "],\"next_cursor\":" + nextCursor +
        ",\"has_more\":" + (nextCursor == "null" ? "false" : "true") + "}";

    public const string Database = """
        {"object":"database","id":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","data_sources":[{"id":"a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d","name":"Задачи котика"}]}
        """;

    const string DsHead = """
        {"object":"data_source","id":"a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d","properties":{
          "Название":{"id":"title","name":"Название","type":"title","title":{}},
          "Напомнить":{"id":"d","name":"Напомнить","type":"date","date":{}},
          "Приоритет":{"id":"p","name":"Приоритет","type":"select","select":{"options":[]}},
          "Тип":{"id":"t","name":"Тип","type":"select","select":{"options":[]}},
        """;
    const string DoneOk = """ "Готово":{"id":"c","name":"Готово","type":"checkbox","checkbox":{}}, """;
    const string DoneWrong = """ "Готово":{"id":"c","name":"Готово","type":"rich_text","rich_text":{}}, """;
    const string RepeatOk = """ "Повтор":{"id":"r","name":"Повтор","type":"select","select":{"options":[]}}, """;
    const string Tail = """ "Заметки":{"id":"z","name":"Заметки","type":"rich_text","rich_text":{}} }} """;

    public static string DataSource(bool complete = true) =>
        DsHead + (complete ? DoneOk + RepeatOk : DoneWrong) + Tail;

    public const string Error401 = """{"object":"error","status":401,"code":"unauthorized","message":"API token is invalid."}""";
    public const string Error404 = """{"object":"error","status":404,"code":"object_not_found","message":"Could not find database."}""";
    public const string Error429 = """{"object":"error","status":429,"code":"rate_limited","message":"Rate limited."}""";
    public const string CreatedPage = """{"object":"page","id":"99999999-8888-7777-6666-555555555555"}""";
}
