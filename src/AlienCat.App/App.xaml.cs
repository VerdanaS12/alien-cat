using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace AlienCat.App;

public partial class App : Application
{
    Mutex? _single;
    AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, @"Local\AlienCat", out var first);
        if (!first)
        {
            MessageBox.Show("Космокот уже живёт на экране. Его значок — возле часов.", "Космокот");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            _controller?.LogError("Необработанная ошибка", ex.ExceptionObject as Exception ?? new Exception("unknown"));
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            _controller?.LogError("Ошибка в фоновой задаче", ex.Exception);
            ex.SetObserved();
        };

        var catalog = Rendering.SpriteSheets.LoadCatalog();
        _controller = new AppController(catalog);
        _controller.Start();
    }

    void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _controller?.LogError("Ошибка интерфейса", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
