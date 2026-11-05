using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AlienCat.App.Rendering;
using AlienCat.App.Win32;
using AlienCat.App.Windows;
using AlienCat.Core.Animation;
using AlienCat.Core.Pet;
using AlienCat.Core.Storage;

namespace AlienCat.App;

/// <summary>Drives the brain at ~30 fps and keeps the cat, toy and effect windows in sync with it.</summary>
internal sealed class PetRunner
{
    const double FootOffset = 2;        // transparent rows under the cat's feet in a 64px cell
    const double ToyFootOffset = 1;

    readonly PetBrain _brain;
    readonly AnimationCatalog _catalog;
    readonly SpriteSheets _sprites;
    readonly Func<AppSettings> _settings;
    readonly Func<bool> _hasOverdue;
    readonly SpriteWindow _toy;
    readonly SpriteWindow _fx;
    readonly DispatcherTimer _timer;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _lastTick;
    string _clipName = "";
    double _clipTime;
    double _fxTime;
    Vec? _lastCursor;
    double _cursorStill;
    bool _fullscreen;
    double _fullscreenCheck;

    public PetRunner(PetBrain brain, AnimationCatalog catalog, Func<AppSettings> settings, Func<bool> hasOverdue)
    {
        _brain = brain;
        _catalog = catalog;
        _sprites = new SpriteSheets(catalog);
        _settings = settings;
        _hasOverdue = hasOverdue;
        var scale = settings().Scale;
        Pet = new PetWindow(scale);
        _toy = new SpriteWindow(24, scale, clickThrough: true);
        _fx = new SpriteWindow(32, scale, clickThrough: true);

        Pet.DragStarted += p => _brain.BeginDrag(p);
        Pet.DragMoved += p => _brain.DragTo(p);
        Pet.DragEnded += () => _brain.EndDrag(Context());
        Pet.Clicked += () => _brain.Click();
        Pet.DoubleClicked += () => QuickNoteRequested?.Invoke();

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
    }

    public PetWindow Pet { get; }
    public PetBrain Brain => _brain;
    public event Action? QuickNoteRequested;

    public void Start()
    {
        var area = ScreenInfo.WorkArea();
        if (_brain.Position.X <= 0) _brain.Position = new Vec((area.Left + area.Right) / 2, area.Bottom);
        Render();
        Pet.Show();
        _lastTick = _clock.Elapsed.TotalSeconds;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Pet.Close();
        _toy.Close();
        _fx.Close();
    }

    public void SetScale(int scale)
    {
        Pet.SetScale(scale);
        _toy.SetScale(scale);
        _fx.SetScale(scale);
    }

    public PetContext Context()
    {
        var s = _settings();
        var dpi = VisualTreeHelper.GetDpi(Pet);
        return new PetContext(DateTimeOffset.Now, TimeZoneInfo.Local, ScreenInfo.WorkArea(), Pet.Size, s.Activity,
            s.CursorHunt, ScreenInfo.Cursor(dpi.DpiScaleX, dpi.DpiScaleY), _cursorStill, _fullscreen, _hasOverdue());
    }

    void Tick()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - _lastTick, 0, 0.25);
        _lastTick = now;

        _fullscreenCheck -= dt;
        if (_fullscreenCheck <= 0)
        {
            _fullscreenCheck = 1;
            try { _fullscreen = ScreenInfo.IsOtherAppFullscreen(); } catch { _fullscreen = false; }
        }

        var dpi = VisualTreeHelper.GetDpi(Pet);
        var cursor = ScreenInfo.Cursor(dpi.DpiScaleX, dpi.DpiScaleY);
        if (cursor is { } c && _lastCursor is { } l && c.DistanceTo(l) <= 2) _cursorStill += dt;
        else _cursorStill = 0;
        _lastCursor = cursor;

        _brain.Update(Context(), dt);
        _clipTime += dt;
        _fxTime += dt;
        Render();
    }

    void Render()
    {
        if (_brain.Activity == PetActivity.Hidden)
        {
            if (Pet.IsVisible) Pet.Hide();
            if (_toy.IsVisible) _toy.Hide();
            if (_fx.IsVisible) _fx.Hide();
            return;
        }
        if (!Pet.IsVisible) Pet.Show();

        var clip = _catalog.ForActivity(_brain.Activity, _brain.Sad, _brain.Nagging, _brain.Phase);
        if (clip.Name != _clipName)
        {
            _clipName = clip.Name;
            _clipTime = 0;
        }
        var mirrored = _brain.Facing < 0;
        Pet.ShowFrame(_sprites.Frame(clip, clip.FrameAt(_clipTime)), mirrored);
        Pet.MoveTo(_brain.Position.X, _brain.Position.Y, FootOffset);

        var toyClip = _catalog.ToyFor(_brain.Activity);
        if (toyClip is not null && _brain.ToyPosition is { } toyPos)
        {
            var toyMirrored = _brain.Activity == PetActivity.MouseChase && _brain.Facing < 0;
            _toy.ShowFrame(_sprites.Frame(toyClip, toyClip.FrameAt(_fxTime)), toyMirrored);
            _toy.MoveTo(toyPos.X, toyPos.Y, ToyFootOffset);
            if (!_toy.IsVisible) _toy.Show();
        }
        else if (_toy.IsVisible) _toy.Hide();

        var fxClip = _catalog.OverlayFor(_brain.Activity);
        if (fxClip is not null)
        {
            var s = Pet.Scale;
            _fx.ShowFrame(_sprites.Frame(fxClip, fxClip.FrameAt(_fxTime)), false);
            _fx.MoveTo(_brain.Position.X + _brain.Facing * 14 * s, _brain.Position.Y - 34 * s);
            if (!_fx.IsVisible) _fx.Show();
        }
        else if (_fx.IsVisible) _fx.Hide();
    }
}
