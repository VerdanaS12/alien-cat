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

    /// <summary>Seconds since the user last touched the keyboard or mouse.</summary>
    public static double UserIdleSeconds()
    {
        var info = new Native.LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.LASTINPUTINFO>() };
        if (!Native.GetLastInputInfo(ref info)) return 0;
        return unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
    }

    /// <summary>True while any mouse button is held down (the cat must never move the cursor then: it would drag things).</summary>
    public static bool AnyMouseButtonDown()
    {
        foreach (var vk in new[] { 0x01, 0x02, 0x04, 0x05, 0x06 })   // left, right, middle, X1, X2
            if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        return false;
    }

    /// <summary>Moves the real mouse cursor to a point given in device-independent units.</summary>
    public static void SetCursor(Vec p, double dpiScaleX, double dpiScaleY) =>
        Native.SetCursorPos((int)Math.Round(p.X * dpiScaleX), (int)Math.Round(p.Y * dpiScaleY));

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
