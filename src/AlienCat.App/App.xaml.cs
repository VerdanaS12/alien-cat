using System.Windows;
using AlienCat.Core.Pet;
using AlienCat.Core.Storage;

namespace AlienCat.App;

public partial class App : Application
{
    PetRunner? _runner;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var catalog = Rendering.SpriteSheets.LoadCatalog();
        var settings = new AppSettings();
        var brain = new PetBrain(new SystemRandom(), DateTimeOffset.Now, new Vec(0, 0));
        _runner = new PetRunner(brain, catalog, () => settings, () => false);
        _runner.Start();
    }
}
