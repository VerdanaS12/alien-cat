using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace AlienCat.Core.Storage;

/// <summary>Loads and saves one JSON file; a corrupt file is moved to .bak and defaults are used.</summary>
public sealed class JsonFileStore<T>(string path, Action<string> log) where T : new()
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    public T Load()
    {
        if (!File.Exists(Path)) return new T();
        try
        {
            var text = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<T>(text, Options) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
        {
            var bak = Path + ".bak";
            try
            {
                File.Move(Path, bak, overwrite: true);
            }
            catch (IOException) { }
            log($"Файл {Path} повреждён и переименован в {bak}: {ex.Message}");
            return new T();
        }
    }

    public void Save(T value)
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, Path, overwrite: true);
    }
}
