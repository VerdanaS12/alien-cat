using System.Text.Json;
using AlienCat.Core.Notion;
using AlienCat.Core.Tasks;
using Xunit;
using static AlienCat.Core.Tests.Time.FakeClock;

namespace AlienCat.Core.Tests.Notion;

public class NotionMapperTests
{
    static CatTask? Map(string json) => NotionMapper.FromPage(JsonDocument.Parse(json).RootElement, Kyiv);

    [Theory]
    [InlineData("https://www.notion.so/myspace/56d000dc94664d21813832c9a254a201?v=0123456789abcdef0123456789abcdef")]
    [InlineData("https://app.notion.com/p/56d000dc94664d21813832c9a254a201")]
    [InlineData("https://www.notion.so/Zadachi-kotika-56d000dc94664d21813832c9a254a201")]
    [InlineData("56d000dc-9466-4d21-8138-32c9a254a201")]
    [InlineData("  56D000DC94664D21813832C9A254A201 ")]
    public void ParseDatabaseId_AcceptsUrlsAndIds(string input) =>
        Assert.Equal("56d000dc-9466-4d21-8138-32c9a254a201", NotionIds.ParseDatabaseId(input));

    [Theory]
    [InlineData("")]
    [InlineData("https://www.notion.so/")]
    [InlineData("hello world")]
    public void ParseDatabaseId_Garbage_Throws(string input) =>
        Assert.Throws<ArgumentException>(() => NotionIds.ParseDatabaseId(input));

    [Fact]
    public void FromPage_FullTask_MapsAllFields()
    {
        var t = Map(Fixtures.Page())!;
        Assert.Equal("11111111-2222-3333-4444-555555555555", t.Id);
        Assert.Equal("Позвонить маме", t.Title);
        Assert.Equal(At(2026, 11, 5, 10), t.Due);
        Assert.False(t.Done);
        Assert.Equal(Priority.Important, t.Priority);
        Assert.Equal(Repeat.Daily, t.Repeat);
        Assert.Equal(ItemKind.Task, t.Kind);
    }

    [Fact]
    public void FromPage_AllSelectValues()
    {
        Assert.Equal(Priority.Urgent, Map(Fixtures.Page(priority: "\"Срочный\""))!.Priority);
        Assert.Equal(Repeat.Weekdays, Map(Fixtures.Page(repeat: "\"По будням\""))!.Repeat);
        Assert.Equal(Repeat.Weekly, Map(Fixtures.Page(repeat: "\"Каждую неделю\""))!.Repeat);
        Assert.Equal(Repeat.Monthly, Map(Fixtures.Page(repeat: "\"Каждый месяц\""))!.Repeat);
        Assert.Equal(Repeat.None, Map(Fixtures.Page(repeat: "\"Нет\""))!.Repeat);
        Assert.Equal(ItemKind.Note, Map(Fixtures.Page(kind: "\"Заметка\""))!.Kind);
        Assert.True(Map(Fixtures.Page(done: true))!.Done);
    }

    [Fact]
    public void FromPage_EmptySelects_UseDefaults()
    {
        var t = Map(Fixtures.Page(priority: null, repeat: null, kind: null, date: null))!;
        Assert.Equal(Priority.Normal, t.Priority);
        Assert.Equal(Repeat.None, t.Repeat);
        Assert.Equal(ItemKind.Task, t.Kind);
        Assert.Null(t.Due);
    }

    [Fact]
    public void FromPage_DateWithoutTime_Means0900Local() =>
        Assert.Equal(At(2026, 11, 5, 9), Map(Fixtures.Page(date: "\"2026-11-05\""))!.Due);

    [Fact]
    public void FromPage_DateWithOffset_KeepsInstant() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero),
            Map(Fixtures.Page(date: "\"2026-11-05T10:00:00.000+00:00\""))!.Due);

    [Fact]
    public void FromPage_NaiveDateTime_IsLocal() =>
        Assert.Equal(At(2026, 11, 5, 10), Map(Fixtures.Page(date: "\"2026-11-05T10:00:00\""))!.Due);

    [Fact]
    public void FromPage_Archived_ReturnsNull()
    {
        Assert.Null(Map(Fixtures.Page(trashed: true)));
        Assert.Null(Map(Fixtures.Page().Replace("\"in_trash\": false", "\"archived\": true")));
    }

    [Fact]
    public void FromPage_EmptyTitle_IsEmptyString() =>
        Assert.Equal("", Map(Fixtures.Page(title: ""))!.Title);
}
