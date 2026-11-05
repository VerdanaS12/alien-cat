using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AlienCat.App.Win32;

namespace AlienCat.App.Windows;

/// <summary>Borderless, transparent, always-on-top window that shows one sprite frame.</summary>
internal class SpriteWindow : Window
{
    readonly Image _image = new() { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
    readonly ScaleTransform _flip = new(1, 1);
    BitmapSource? _frame;
    bool _mirrored;

    public SpriteWindow(int cell, int scale, bool clickThrough = false)
    {
        Cell = cell;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        ClickThrough = clickThrough;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        _image.RenderTransformOrigin = new Point(0.5, 0.5);
        _image.RenderTransform = _flip;
        if (clickThrough) _image.IsHitTestVisible = false;
        Content = _image;
        SetScale(scale);
    }

    public int Cell { get; }
    public int Scale { get; private set; }
    public bool ClickThrough { get; }
    public double Size => Cell * Scale;

    public void SetScale(int scale)
    {
        Scale = Math.Clamp(scale, 1, 3);
        Width = Height = Size;
    }

    public void ShowFrame(BitmapSource frame, bool mirrored)
    {
        if (!ReferenceEquals(frame, _frame)) { _image.Source = frame; _frame = frame; }
        if (mirrored != _mirrored) { _flip.ScaleX = mirrored ? -1 : 1; _mirrored = mirrored; }
    }

    /// <summary>Moves the window so that the given point is its bottom centre, with <paramref name="footOffset"/> cell pixels below the feet.</summary>
    public void MoveTo(double centerX, double bottomY, double footOffset = 0)
    {
        var left = Math.Round(centerX - Size / 2);
        var top = Math.Round(bottomY - Size + footOffset * Scale);
        if (Math.Abs(Left - left) > 0.1) Left = left;
        if (Math.Abs(Top - top) > 0.1) Top = top;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        if (ClickThrough) ex |= 0x00000020; // WS_EX_TRANSPARENT
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
    }
}
