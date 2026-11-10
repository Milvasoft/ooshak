using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Ooshak.Core;
using Forms = System.Windows.Forms;

namespace Ooshak;

/// <summary>
/// Shows one permission question at a time. It never takes focus and its buttons accept only
/// the mouse, so typing elsewhere can never approve anything.
/// </summary>
public partial class PermissionWindow : Window
{
    private const int TopMarginPixels = 64;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    // A popup appearing under a moving mouse must not catch a click meant for something else.
    private static readonly TimeSpan ArmDelay = TimeSpan.FromMilliseconds(500);

    private readonly PermissionBroker _broker;
    private readonly DispatcherTimer _armTimer = new() { Interval = ArmDelay };
    private PermissionRequest? _shown;

    public PermissionWindow(PermissionBroker broker)
    {
        InitializeComponent();
        _broker = broker;

        SourceInitialized += (_, _) => IslandWindow.MakeNonActivating(this);
        _armTimer.Tick += (_, _) => Arm(true);
        _broker.Changed += OnChanged;
        Refresh();
    }

    public void Retire()
    {
        _broker.Changed -= OnChanged;
        _armTimer.Stop();
        Close();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var current = _broker.Current;
        if (current is null)
        {
            _shown = null;
            _armTimer.Stop();
            Hide();
            return;
        }

        var waiting = _broker.Count - 1;
        WaitingText.Text = waiting > 0 ? L.T("+{0} bekliyor", waiting) : "";

        if (ReferenceEquals(current, _shown))
            return;

        _shown = current;
        TitleText.Text = L.T("{0} izin istiyor", current.Session);
        DetailText.Text = current.Detail;
        Arm(false);
        _armTimer.Stop();
        _armTimer.Start();

        Show();

        // Placed once the size is known, in device pixels, so monitors with other scalings work too.
        Dispatcher.BeginInvoke(PlaceOnScreen, DispatcherPriority.Loaded);
    }

    private void Arm(bool enabled)
    {
        _armTimer.Stop();
        DenyButton.IsEnabled = enabled;
        AllowButton.IsEnabled = enabled;
    }

    private void PlaceOnScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds))
            return;

        var area = TargetScreen().WorkingArea;
        var width = bounds.Right - bounds.Left;
        SetWindowPos(handle, HwndTopmost, area.X + (area.Width - width) / 2, area.Y + TopMarginPixels, 0, 0, SwpNoSize | SwpNoActivate);
    }

    // Approvals are expected where the Claude window is. A screen taken by a full-screen game or
    // video is skipped, because a popup there would sit behind it or interrupt it.
    private static Forms.Screen TargetScreen()
    {
        var busy = FullscreenDetector.IsActive() ? Forms.Screen.FromHandle(GetForegroundWindow()) : null;
        var claude = Process.GetProcessesByName("claude")
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle == "Claude");
        var preferred = claude is null ? Forms.Screen.PrimaryScreen! : Forms.Screen.FromHandle(claude.MainWindowHandle);

        if (busy is null || preferred.DeviceName != busy.DeviceName)
            return preferred;

        return Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName != busy.DeviceName) ?? preferred;
    }

    private void OnAllow(object sender, RoutedEventArgs e) => Answer(true);

    private void OnDeny(object sender, RoutedEventArgs e) => Answer(false);

    private void Answer(bool allow)
    {
        if (_shown is { } request)
            _broker.Decide(request, allow);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, int flags);
}
