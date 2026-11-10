using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Ooshak.Core;

/// <summary>Tells whether the foreground window is a game or video filling its monitor.</summary>
public static class FullscreenDetector
{
    private const int MonitorDefaultToNearest = 2;
    private const int StateD3dFullscreen = 3;
    private const int StatePresentation = 5;

    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];
    private static readonly HashSet<string> WorkApps = ["claude", "ooshak"];

    public static bool IsActive()
    {
        if (SHQueryUserNotificationState(out var state) == 0 && state is StateD3dFullscreen or StatePresentation)
            return true;

        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || IsShellOrWorkApp(window) || !GetWindowRect(window, out var bounds))
            return false;

        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfoW(MonitorFromWindow(window, MonitorDefaultToNearest), ref monitor)
            && Covers(bounds.ToTuple(), monitor.Bounds.ToTuple());
    }

    /// <summary>True when the window reaches every monitor edge. A maximized window stops at the taskbar.</summary>
    public static bool Covers((int Left, int Top, int Right, int Bottom) window, (int Left, int Top, int Right, int Bottom) monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    private static bool IsShellOrWorkApp(IntPtr window)
    {
        var name = new StringBuilder(64);
        if (GetClassNameW(window, name, name.Capacity) > 0 && ShellClasses.Contains(name.ToString()))
            return true;

        GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById(processId);
            return WorkApps.Contains(process.ProcessName.ToLowerInvariant());
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly (int, int, int, int) ToTuple() => (Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Bounds;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect bounds);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
