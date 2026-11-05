namespace AlienCat.Core.Storage;

public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string protectedValue);
}

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlienCat");

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string State => Path.Combine(Root, "state.json");
    public static string Cache => Path.Combine(Root, "cache.json");
    public static string Logs => Path.Combine(Root, "logs");
}
