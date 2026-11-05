using System.Diagnostics;
using System.Text;
using System.Windows;
using AlienCat.Core.Pet;

namespace AlienCat.App.Win32;

internal static class ScreenInfo
{
    static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    /// <summary>Work area of the primary monitor in device-independent units (excludes the taskbar).</summary>
    public static Bounds WorkArea()
    {
        var r = SystemParameters.WorkArea;
        return new Bounds(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>Cursor position in device-independent units, using the given DPI scale.</summary>
    public static Vec? Cursor(double dpiScaleX, double dpiScaleY)
    {
        if (!Native.GetCursorPos(out var p)) return null;
        return new Vec(p.X / dpiScaleX, p.Y / dpiScaleY);
    }

    /// <summary>True when another application's window covers its whole monitor (game, video, presentation).</summary>
    public static bool IsOtherAppFullscreen()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == Native.GetShellWindow() || hwnd == Native.GetDesktopWindow()) return false;

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == OwnProcessId) return false;

        var cls = new StringBuilder(64);
        Native.GetClassName(hwnd, cls, cls.Capacity);
        var name = cls.ToString();
        if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        if (!Native.GetWindowRect(hwnd, out var rect)) return false;
        var monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return false;
        var m = info.rcMonitor;
        return rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
    }

    [Conditional("DEBUG")]
    public static void Trace(string message) => System.Diagnostics.Debug.WriteLine(message);
}
