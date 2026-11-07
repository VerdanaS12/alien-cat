using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using AlienCat.App.Win32;

namespace AlienCat.App.Windows;

/// <summary>Click-through transparent window that draws a few lines (claw scratches, the web thread).</summary>
internal sealed class LinesWindow : Window
{
    readonly Canvas _canvas = new() { IsHitTestVisible = false };
    readonly (Brush Brush, double Thickness)[] _strokes;

    public LinesWindow(params (Brush Brush, double Thickness)[] strokes)
    {
        _strokes = strokes;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        Content = _canvas;
    }

    /// <summary>Draws the lines (screen coordinates in DIPs); each stroke style is drawn over the previous one.</summary>
    public void Draw(IReadOnlyList<(Point A, Point B)> lines, double widthScale = 1)
    {
        if (lines.Count == 0) { if (IsVisible) Hide(); return; }
        var margin = 6 * widthScale;
        var minX = lines.Min(l => Math.Min(l.A.X, l.B.X)) - margin;
        var minY = lines.Min(l => Math.Min(l.A.Y, l.B.Y)) - margin;
        var maxX = lines.Max(l => Math.Max(l.A.X, l.B.X)) + margin;
        var maxY = lines.Max(l => Math.Max(l.A.Y, l.B.Y)) + margin;
        Left = Math.Round(minX);
        Top = Math.Round(minY);
        Width = Math.Max(1, Math.Ceiling(maxX - minX));
        Height = Math.Max(1, Math.Ceiling(maxY - minY));

        _canvas.Children.Clear();
        foreach (var (brush, thickness) in _strokes)
        {
            foreach (var (a, b) in lines)
            {
                _canvas.Children.Add(new Line
                {
                    X1 = a.X - Left, Y1 = a.Y - Top, X2 = b.X - Left, Y2 = b.Y - Top,
                    Stroke = brush,
                    StrokeThickness = thickness * widthScale,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                });
            }
        }
        if (!IsVisible) Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | 0x00000020; // WS_EX_TRANSPARENT
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
    }
}
