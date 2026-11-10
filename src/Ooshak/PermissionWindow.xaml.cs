using System.Windows;
using System.Windows.Threading;
using Ooshak.Core;

namespace Ooshak;

/// <summary>
/// Shows one permission question at a time. It never takes focus and its buttons accept only
/// the mouse, so typing elsewhere can never approve anything.
/// </summary>
public partial class PermissionWindow : Window
{
    private const double TopMargin = 56;
    private const double EdgeMargin = 8;
    private const double EstimatedHeight = 300;

    // A popup appearing under a moving mouse must not catch a click meant for something else.
    private static readonly TimeSpan ArmDelay = TimeSpan.FromMilliseconds(500);

    private readonly OoshakConfig _config;
    private readonly PermissionBroker _broker;
    private readonly DispatcherTimer _armTimer = new() { Interval = ArmDelay };
    private PermissionRequest? _shown;

    public PermissionWindow(OoshakConfig config, PermissionBroker broker)
    {
        InitializeComponent();
        _config = config;
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

        PlaceOnScreen();
        Show();
    }

    private void Arm(bool enabled)
    {
        _armTimer.Stop();
        DenyButton.IsEnabled = enabled;
        AllowButton.IsEnabled = enabled;
    }

    private void PlaceOnScreen()
    {
        var work = SystemParameters.WorkArea;
        var centered = work.Left + (work.Width - Width) / 2;

        Left = _config.IslandLeft is { } left ? Math.Clamp(left, work.Left, work.Right - Width) : centered;
        Top = _config.IslandTop is { } top
            ? Math.Clamp(top + TopMargin + EdgeMargin * 3, work.Top + EdgeMargin, work.Bottom - EstimatedHeight)
            : work.Top + TopMargin;
    }

    private void OnAllow(object sender, RoutedEventArgs e) => Answer(true);

    private void OnDeny(object sender, RoutedEventArgs e) => Answer(false);

    private void Answer(bool allow)
    {
        if (_shown is { } request)
            _broker.Decide(request, allow);
    }
}
