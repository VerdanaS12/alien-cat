using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using AlienCat.Core.Animation;

namespace AlienCat.App.Rendering;

/// <summary>Loads sprite sheets from the app resources and hands out cached, frozen frames.</summary>
internal sealed class SpriteSheets
{
    readonly AnimationCatalog _catalog;
    readonly Dictionary<string, BitmapSource> _sheets = new();
    readonly Dictionary<(string, int), BitmapSource> _frames = new();

    public SpriteSheets(AnimationCatalog catalog) => _catalog = catalog;

    public BitmapSource Frame(AnimationClip clip, int frameIndex)
    {
        var key = (clip.Sheet, frameIndex);
        if (_frames.TryGetValue(key, out var cached)) return cached;

        var sheet = Sheet(clip.Sheet);
        var info = _catalog.Sheets[clip.Sheet];
        var (x, y) = clip.CellOrigin(frameIndex, info);
        var frame = new CroppedBitmap(sheet, new Int32Rect(x, y, info.Cell, info.Cell));
        frame.Freeze();
        _frames[key] = frame;
        return frame;
    }

    BitmapSource Sheet(string name)
    {
        if (_sheets.TryGetValue(name, out var s)) return s;
        var img = new BitmapImage();
        img.BeginInit();
        img.UriSource = new Uri($"pack://application:,,,/Assets/{name}", UriKind.Absolute);
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        _sheets[name] = img;
        return img;
    }

    public static AnimationCatalog LoadCatalog()
    {
        using var stream = typeof(SpriteSheets).Assembly.GetManifestResourceStream("animations.json")
            ?? throw new InvalidOperationException("animations.json resource is missing");
        using var reader = new StreamReader(stream);
        return AnimationCatalog.Parse(reader.ReadToEnd());
    }
}
