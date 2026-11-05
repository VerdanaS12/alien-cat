using System.Windows;
using System.Windows.Input;
using AlienCat.Core.Pet;

namespace AlienCat.App.Windows;

/// <summary>The cat itself: handles dragging, clicks and double-clicks.</summary>
internal sealed class PetWindow : SpriteWindow
{
    const double ClickSlop = 4;
    Point _downScreen;
    Vec _grabOffset;
    bool _down;
    bool _dragging;

    public PetWindow(int scale) : base(64, scale) => Cursor = Cursors.Hand;

    public event Action<Vec>? DragStarted;
    public event Action<Vec>? DragMoved;
    public event Action? DragEnded;
    public event Action? Clicked;
    public event Action? DoubleClicked;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            _down = false;
            DoubleClicked?.Invoke();
            return;
        }
        _down = true;
        _dragging = false;
        _downScreen = ScreenPoint(e);
        var bottomCenter = new Vec(Left + Size / 2, Top + Size);
        _grabOffset = new Vec(bottomCenter.X - _downScreen.X, bottomCenter.Y - _downScreen.Y);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_down) return;
        var p = ScreenPoint(e);
        var target = new Vec(p.X + _grabOffset.X, p.Y + _grabOffset.Y);
        if (!_dragging && (Math.Abs(p.X - _downScreen.X) > ClickSlop || Math.Abs(p.Y - _downScreen.Y) > ClickSlop))
        {
            _dragging = true;
            DragStarted?.Invoke(target);
        }
        if (_dragging) DragMoved?.Invoke(target);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_down) return;
        _down = false;
        ReleaseMouseCapture();
        if (_dragging) DragEnded?.Invoke();
        else Clicked?.Invoke();
        _dragging = false;
    }

    Point ScreenPoint(MouseEventArgs e)
    {
        var local = e.GetPosition(this);
        return new Point(Left + local.X, Top + local.Y);
    }
}
