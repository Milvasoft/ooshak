using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Ooshak.Core;

namespace Ooshak;

/// <summary>The floating capsule. It morphs with a spring and never takes keyboard focus.</summary>
public partial class IslandWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;

    private const double IdleWidth = 118;
    private const double IdleHeight = 36;
    private const double ActiveWidth = 186;
    private const double ActiveHeight = 44;
    private const double NoteHeight = 46;
    private const double NoteChromeWidth = 64;
    private const double MaxNoteWidth = 420;
    private const double SpringStiffness = 210;
    private const double SpringDamping = 19;
    private const double TopMargin = 8;
    private const double MaxStep = 1.0 / 30;

    private readonly AppController _controller;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrame;
    private double _width = IdleWidth;
    private double _height = IdleHeight;
    private double _widthVelocity;
    private double _heightVelocity;
    private string? _shownNote;
    private bool _animating;

    public IslandWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        Liquid.Source = controller.LiquidFrame;
        Liquid.CornerRadius = IdleHeight / 2;

        SourceInitialized += (_, _) => MakeNonActivating(this);
        Loaded += (_, _) => PlaceOnScreen();
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        if (IsVisible == _animating)
            return;

        _animating = IsVisible;
        if (_animating)
            CompositionTarget.Rendering += OnFrame;
        else
            CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Min(now - _lastFrame, MaxStep);
        _lastFrame = now;

        var snapshot = _controller.Snapshot();
        var note = NoteFor(snapshot);
        ShowState(snapshot, note);

        var targetHeight = note is not null ? NoteHeight : snapshot.State == VoiceState.Hearing ? ActiveHeight : IdleHeight;
        var targetWidth = note is not null ? NoteWidth() : snapshot.State == VoiceState.Hearing ? ActiveWidth : IdleWidth;

        Spring(ref _width, ref _widthVelocity, targetWidth, dt);
        Spring(ref _height, ref _heightVelocity, targetHeight, dt);

        Capsule.Width = Math.Max(_width, IdleHeight);
        Capsule.Height = Math.Max(_height, 24);
        var radius = Capsule.Height / 2;
        Capsule.CornerRadius = new CornerRadius(radius);
        Liquid.CornerRadius = radius;

        var noteOpacity = note is null ? 0.0 : 1.0;
        NoteText.Opacity += (noteOpacity - NoteText.Opacity) * Math.Min(1, dt * 14);
    }

    private static string? NoteFor(VoiceSnapshot snapshot) => snapshot.State switch
    {
        VoiceState.Muted => L.T("Kapalı"),
        _ => snapshot.Note,
    };

    private void ShowState(VoiceSnapshot snapshot, string? note)
    {
        var muted = snapshot.State == VoiceState.Muted;
        Mark.Visibility = muted ? Visibility.Collapsed : Visibility.Visible;
        MutedIcon.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        MuteItem.Header = L.T(muted ? "Mikrofonu aç" : "Mikrofonu kapat");

        if (note is not null && note != _shownNote)
        {
            _shownNote = note;
            NoteText.Text = note;
        }
    }

    private double NoteWidth()
    {
        NoteText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Clamp(NoteText.DesiredSize.Width + NoteChromeWidth, IdleWidth, MaxNoteWidth);
    }

    private static void Spring(ref double value, ref double velocity, double target, double dt)
    {
        velocity += ((target - value) * SpringStiffness - velocity * SpringDamping) * dt;
        value += velocity * dt;
    }

    private void OnCapsuleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _controller.ShowMain();
            return;
        }

        DragMove();
        ClampToScreen();
        _controller.SaveIslandPosition(Left, Top);
    }

    private void PlaceOnScreen()
    {
        var config = _controller.Config;
        Left = config.IslandLeft ?? SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
        Top = config.IslandTop ?? SystemParameters.WorkArea.Top + TopMargin - (Height - IdleHeight) / 2;
        ClampToScreen();
    }

    // A saved position can point at a monitor that is no longer attached.
    private void ClampToScreen()
    {
        var margin = (Height - IdleHeight) / 2;
        Left = Math.Clamp(Left, SystemParameters.VirtualScreenLeft - Width / 3,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width * 2 / 3);
        Top = Math.Clamp(Top, SystemParameters.VirtualScreenTop - margin,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height + margin);
    }

    internal static void MakeNonActivating(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExToolWindow | WsExNoActivate);
    }

    private void OnOpen(object sender, RoutedEventArgs e) => _controller.ShowMain();

    private void OnToggleMute(object sender, RoutedEventArgs e) => _controller.ToggleMute();

    private void OnStopSpeaking(object sender, RoutedEventArgs e) => _controller.ToggleReading();

    private void OnHide(object sender, RoutedEventArgs e) => _controller.SetIslandEnabled(false);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);
}
