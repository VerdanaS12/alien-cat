using System.Reflection;
using Xunit;

// Usage: dotnet run --project tests/LocalRunner -- [filter]
// filter matches "Class.Method" substrings (case-sensitive), e.g. RecurrenceTests
var filter = args.Length > 0 ? args[0] : null;
int passed = 0, failed = 0, skipped = 0;
var failures = new List<string>();

var types = typeof(Program).Assembly.GetTypes()
    .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && t.Namespace?.StartsWith("AlienCat") == true)
    .OrderBy(t => t.FullName);

foreach (var type in types)
{
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance).OrderBy(m => m.Name))
    {
        var fact = method.GetCustomAttribute<FactAttribute>();
        if (fact is null) continue;
        var name = $"{type.Name}.{method.Name}";
        if (filter is not null && !name.Contains(filter, StringComparison.Ordinal)) continue;
        if (fact.Skip is not null) { skipped++; continue; }

        var cases = method.GetCustomAttributes<InlineDataAttribute>().Select(a => a.Data).ToList();
        if (cases.Count == 0) cases.Add([]);
        foreach (var data in cases)
        {
            var label = data.Length == 0 ? name : $"{name}({string.Join(", ", data)})";
            object? instance = null;
            try
            {
                instance = Activator.CreateInstance(type);
                var result = method.Invoke(instance, data.Length == 0 ? null : data);
                if (result is Task task) task.GetAwaiter().GetResult();
                passed++;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
                failed++;
                failures.Add($"FAIL {label}\n     {inner.GetType().Name}: {inner.Message.Replace("\n", "\n     ")}");
            }
            finally
            {
                (instance as IDisposable)?.Dispose();
            }
        }
    }
}

foreach (var f in failures) Console.WriteLine(f);
Console.WriteLine($"Passed: {passed}, Failed: {failed}, Skipped: {skipped}");
return failed == 0 && passed > 0 ? 0 : 1;
