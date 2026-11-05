// Minimal xUnit-compatible surface so the real test sources compile without NuGet.
// Only what the AlienCat tests use. CI runs the same sources on real xUnit.
#pragma warning disable CS8603, CS8604
using System.Collections;

namespace Xunit;

[AttributeUsage(AttributeTargets.Method)]
public class FactAttribute : Attribute
{
    public string? Skip { get; set; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class TheoryAttribute : FactAttribute { }

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class InlineDataAttribute(params object?[] data) : Attribute
{
    public object?[] Data { get; } = data;
}

public sealed class AssertException(string message) : Exception(message);

public static class Assert
{
    static void Fail(string msg) => throw new AssertException(msg);

    public static void Equal<T>(T expected, T actual)
    {
        if (expected is IEnumerable e && actual is IEnumerable a && expected is not string)
        {
            var x = e.Cast<object?>().ToList();
            var y = a.Cast<object?>().ToList();
            if (!x.SequenceEqual(y)) Fail($"Sequences differ. Expected [{string.Join(", ", x)}], actual [{string.Join(", ", y)}]");
            return;
        }
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Fail($"Expected: {expected}\nActual:   {actual}");
    }

    public static void Equal(double expected, double actual, int precision)
    {
        if (Math.Round(expected, precision) != Math.Round(actual, precision))
            Fail($"Expected: {expected}\nActual:   {actual}");
    }

    public static void NotEqual<T>(T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual)) Fail($"Did not expect: {actual}");
    }

    public static void True(bool condition, string? message = null) { if (!condition) Fail(message ?? "Expected true"); }
    public static void False(bool condition, string? message = null) { if (condition) Fail(message ?? "Expected false"); }
    public static void Null(object? value) { if (value is not null) Fail($"Expected null, got {value}"); }
    public static void NotNull(object? value) { if (value is null) Fail("Expected not null"); }

    public static void Empty(IEnumerable collection)
    {
        if (collection.Cast<object?>().Any()) Fail($"Expected empty, got {collection.Cast<object?>().Count()} items");
    }

    public static void NotEmpty(IEnumerable collection)
    {
        if (!collection.Cast<object?>().Any()) Fail("Expected not empty");
    }

    public static T Single<T>(IEnumerable<T> collection)
    {
        var list = collection.ToList();
        if (list.Count != 1) Fail($"Expected single item, got {list.Count}");
        return list[0];
    }

    public static T Single<T>(IEnumerable<T> collection, Predicate<T> predicate) =>
        Single(collection.Where(x => predicate(x)));

    public static void Contains<T>(T expected, IEnumerable<T> collection)
    {
        if (!collection.Contains(expected)) Fail($"Not found: {expected}");
    }

    public static void Contains(string expected, string? actual)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.Ordinal)) Fail($"'{expected}' not in '{actual}'");
    }

    public static void DoesNotContain<T>(T expected, IEnumerable<T> collection)
    {
        if (collection.Contains(expected)) Fail($"Unexpectedly found: {expected}");
    }

    public static void Contains<T>(IEnumerable<T> collection, Predicate<T> filter)
    {
        if (!collection.Any(x => filter(x))) Fail("No item matched");
    }

    public static void DoesNotContain<T>(IEnumerable<T> collection, Predicate<T> filter)
    {
        if (collection.Any(x => filter(x))) Fail("An item matched");
    }

    public static void All<T>(IEnumerable<T> collection, Action<T> action)
    {
        foreach (var item in collection) action(item);
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (Exception ex)
        {
            if (ex.GetType() == typeof(T)) return (T)ex;
            Fail($"Expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}");
        }
        Fail($"Expected {typeof(T).Name}, nothing thrown");
        return null!;
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (Exception ex)
        {
            if (ex.GetType() == typeof(T)) return (T)ex;
            Fail($"Expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}");
        }
        Fail($"Expected {typeof(T).Name}, nothing thrown");
        return null!;
    }

    public static void InRange<T>(T actual, T low, T high) where T : IComparable<T>
    {
        if (actual.CompareTo(low) < 0 || actual.CompareTo(high) > 0) Fail($"{actual} not in [{low}, {high}]");
    }

    public static T IsType<T>(object? value)
    {
        if (value is null || value.GetType() != typeof(T)) Fail($"Expected type {typeof(T).Name}, got {value?.GetType().Name}");
        return (T)value!;
    }
}
