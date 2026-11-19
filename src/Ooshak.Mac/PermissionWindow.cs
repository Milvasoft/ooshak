using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ooshak.Core;

namespace Ooshak.Mac;

/// <summary>
/// Shows one permission question at a time in the top right corner. It does not take focus, and
/// its buttons only arm after a moment, so a click meant for something else cannot approve anything.
/// </summary>
public sealed class PermissionWindow : Window
{
    private const int EdgeMargin = 24;
    private static readonly TimeSpan ArmDelay = TimeSpan.FromMilliseconds(500);

    private readonly PermissionBroker _broker;
    private readonly DispatcherTimer _armTimer = new() { Interval = ArmDelay };
    private readonly TextBlock _heading = Ui.Heading("");
    private readonly TextBlock _detail = new()
    {
        FontFamily = new FontFamily("Menlo, monospace"),
        FontSize = 12,
        Foreground = Ui.Text,
        TextWrapping = TextWrapping.Wrap,
        MaxHeight = 220,
    };
    private readonly TextBlock _waiting = Ui.Note("");
    private readonly Button _allow = new() { Content = L.T("İzin ver"), IsEnabled = false };
    private readonly Button _deny = new() { Content = L.T("Reddet"), IsEnabled = false };
    private PermissionRequest? _shown;
    private bool _retired;

    public PermissionWindow(PermissionBroker broker)
    {
        _broker = broker;
        Title = "Ooshak";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        SystemDecorations = SystemDecorations.BorderOnly;
        Background = Ui.Card;

        _allow.Click += (_, _) => Answer(true);
        _deny.Click += (_, _) => Answer(false);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 6,
            Children =
            {
                _heading,
                new ScrollViewer { Content = _detail, MaxHeight = 220 },
                _waiting,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { _deny, _allow },
                },
            },
        };

        Closing += (_, e) =>
        {
            if (!_retired)
            {
                e.Cancel = true;
                Hide();
            }
        };

        _armTimer.Tick += (_, _) => Arm(true);
        _broker.Changed += OnChanged;
        Refresh();
    }

    public void Retire()
    {
        _broker.Changed -= OnChanged;
        _armTimer.Stop();
        _retired = true;
        Close();
    }

    private void OnChanged() => Dispatcher.UIThread.Post(Refresh);

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
        _waiting.Text = waiting > 0 ? L.T("+{0} bekliyor", waiting) : "";
        _waiting.IsVisible = waiting > 0;

        if (ReferenceEquals(current, _shown))
            return;

        _shown = current;
        _heading.Text = L.T("{0} izin istiyor", current.Session);
        _detail.Text = current.Detail;
        Arm(false);
        _armTimer.Stop();
        _armTimer.Start();

        if (!IsVisible)
            Show();
        PlaceTopRight();
    }

    private void Arm(bool armed)
    {
        _armTimer.Stop();
        _allow.IsEnabled = armed;
        _deny.IsEnabled = armed;
    }

    private void Answer(bool allow)
    {
        if (_shown is { } request)
            _broker.Decide(request, allow);
    }

    private void PlaceTopRight()
    {
        if (Screens.Primary is not { } screen)
            return;

        var area = screen.WorkingArea;
        var width = (int)(Width * screen.Scaling);
        Position = new PixelPoint(area.Right - width - EdgeMargin, area.Y + EdgeMargin);
    }
}
