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

/// <summary>Drives the brain at ~30 fps and keeps the cat, toy, effect, scratch and thread windows in sync with it.</summary>
internal sealed class PetRunner
{
    const double FootOffset = 2;        // transparent rows under the cat's feet in a 64px cell
    const double ToyFootOffset = 1;
    const double ThreadTopOffset = 1;   // cell pixels between the window top and the tassel holding the thread
    const double ScratchFadeSeconds = 8;
    const double UserMoveTolerance = 1.5; // device pixels; SetCursorPos lands exactly, so any larger gap is the user

    readonly PetBrain _brain;
    readonly AnimationCatalog _catalog;
    readonly SpriteSheets _sprites;
    readonly Func<AppSettings> _settings;
    readonly Func<bool> _hasOverdue;
    readonly SpriteWindow _toy;
    readonly SpriteWindow _fx;
    readonly LinesWindow _scratches;
    readonly LinesWindow _thread;
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
    double _idleSeconds;
    bool _buttonDown;
    Point? _cursorSetPx;
    List<(Point A, Point B)> _scratchLines = new();
    double _scratchFade = -1;

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
        _scratches = new LinesWindow(
            (Ui.Brush("#7A2B2F5C"), 2.6),     // dark groove
            (Ui.Brush("#E8FFFFFF"), 1.1));    // bright scratched edge
        _thread = new LinesWindow(
            (Ui.Brush("#557FF7FF"), 3.2),     // soft glow
            (Ui.Brush("#FFD6FDFF"), 1.0));    // the thread itself

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
        _scratches.Close();
        _thread.Close();
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
        return new PetContext(DateTimeOffset.Now, TimeZoneInfo.Local, ScreenInfo.WorkArea(), Pet.BaseSize, s.Activity,
            s.CursorHunt, ScreenInfo.Cursor(dpi.DpiScaleX, dpi.DpiScaleY), _cursorStill, _fullscreen, _hasOverdue(),
            _idleSeconds, s.Tricks ?? new TrickSettings(), s.Outfit, _buttonDown);
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
        try { _idleSeconds = ScreenInfo.UserIdleSeconds(); } catch { _idleSeconds = 0; }
        try { _buttonDown = ScreenInfo.AnyMouseButtonDown(); } catch { _buttonDown = true; }

        var dpi = VisualTreeHelper.GetDpi(Pet);
        var cursor = ScreenInfo.Cursor(dpi.DpiScaleX, dpi.DpiScaleY);

        // the user grabbed the mouse back while the cat was carrying the cursor → let go at once
        if (_cursorSetPx is { } set && cursor is { } actual)
        {
            var dx = actual.X * dpi.DpiScaleX - set.X;
            var dy = actual.Y * dpi.DpiScaleY - set.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > UserMoveTolerance) _brain.UserMovedCursor();
        }
        _cursorSetPx = null;

        if (cursor is { } c && _lastCursor is { } l && c.DistanceTo(l) <= 2) _cursorStill += dt;
        else _cursorStill = 0;
        _lastCursor = cursor;

        _brain.Update(Context(), dt);

        if (_brain.CursorTarget is { } target && !_fullscreen && !_buttonDown)
        {
            try
            {
                ScreenInfo.SetCursor(target, dpi.DpiScaleX, dpi.DpiScaleY);
                _cursorSetPx = new Point(Math.Round(target.X * dpi.DpiScaleX), Math.Round(target.Y * dpi.DpiScaleY));
            }
            catch { _brain.UserMovedCursor(); }
        }

        _clipTime += dt;
        _fxTime += dt;
        UpdateScratches(dt);
        Render();
    }

    void UpdateScratches(double dt)
    {
        if (_fullscreen && _scratchFade >= 0)
        {
            _scratchFade = -1;
            _scratchLines.Clear();
            if (_scratches.IsVisible) _scratches.Hide();
            return;
        }
        if (_brain.Surface == Surface.Glass && _brain.GlassStart is { } start
            && _brain.Trick is TrickPhase.Cling or TrickPhase.Slide)
        {
            var size = Pet.BaseSize * _brain.Boost;
            _scratchLines = PetBrain.ClawOffsets
                .Select(o => (new Point(start.X + o.X * size, start.Y + o.Y * size),
                              new Point(_brain.Position.X + o.X * size, _brain.Position.Y + o.Y * size)))
                .ToList();
            _scratchFade = 0;
            _scratches.Opacity = 1;
            _scratches.Draw(_scratchLines, Pet.Scale / 2.0 * PetBrain.PounceBoost);
            return;
        }
        if (_scratchFade < 0) return;
        _scratchFade += dt;
        if (_scratchFade >= ScratchFadeSeconds)
        {
            _scratchFade = -1;
            _scratchLines.Clear();
            if (_scratches.IsVisible) _scratches.Hide();
            return;
        }
        _scratches.Opacity = 1 - _scratchFade / ScratchFadeSeconds;
    }

    void Render()
    {
        if (!_brain.IsVisible)
        {
            if (Pet.IsVisible) Pet.Hide();
            if (_toy.IsVisible) _toy.Hide();
            if (_fx.IsVisible) _fx.Hide();
            if (_thread.IsVisible) _thread.Hide();
            return;
        }
        if (!Pet.IsVisible) Pet.Show();

        var clip = _catalog.ForActivity(_brain.Activity, _brain.Sad, _brain.Nagging, _brain.Phase, _brain.Trick);
        if (clip.Name != _clipName)
        {
            _clipName = clip.Name;
            _clipTime = 0;
        }
        var frame = _sprites.Frame(clip, clip.FrameAt(_clipTime), _catalog.SheetFor(clip, _brain.Outfit));
        var pos = _brain.Position;
        switch (_brain.Surface)
        {
            case Surface.LeftWall:
                Pet.SetBoost(1);
                Pet.ShowFrame(frame, mirrored: true, rotation: 90);
                Pet.MoveToCenter(pos.X, pos.Y);
                break;
            case Surface.RightWall:
                Pet.SetBoost(1);
                Pet.ShowFrame(frame, mirrored: false, rotation: -90);
                Pet.MoveToCenter(pos.X, pos.Y);
                break;
            case Surface.Glass:
                Pet.SetBoost(_brain.Boost);
                Pet.ShowFrame(frame, mirrored: false);
                Pet.MoveToCenter(pos.X, pos.Y);
                break;
            case Surface.Thread:
                Pet.SetBoost(1);
                Pet.ShowFrame(frame, mirrored: _brain.Facing < 0);
                Pet.MoveTopCenter(pos.X, pos.Y - ThreadTopOffset * Pet.Scale);
                break;
            default:
                Pet.SetBoost(1);
                Pet.ShowFrame(frame, _brain.Facing < 0);
                Pet.MoveTo(pos.X, pos.Y, FootOffset);
                break;
        }

        if (_brain.Surface == Surface.Thread && _brain.ThreadAnchor is { } anchor)
            _thread.Draw([(new Point(anchor.X, anchor.Y), new Point(pos.X, pos.Y + 1))], Pet.Scale / 2.0);
        else if (_thread.IsVisible) _thread.Hide();

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
            if (_brain.Activity == PetActivity.ChangeOutfit)
            {
                _fx.SetBoost(2);   // the puff covers the whole cat
                _fx.MoveToCenter(pos.X, pos.Y - 28 * s);
            }
            else
            {
                _fx.SetBoost(1);
                _fx.MoveTo(pos.X + _brain.Facing * 14 * s, pos.Y - 34 * s);
            }
            if (!_fx.IsVisible) _fx.Show();
        }
        else if (_fx.IsVisible) _fx.Hide();
    }
}
