using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Ooshak.Core;
using Ooshak.Core.Mac;

namespace Ooshak.Mac;

/// <summary>Owns the voice loop, the menu bar icon and the windows on macOS.</summary>
public sealed class MacController : IDisposable
{
    private const string AccessibilitySettings = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";
    private const string MicrophoneSettings = "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone";

    private static readonly VoiceSnapshot Offline = new(VoiceState.Muted, 0, null, null, null, 0);

    private readonly Application _app;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly DispatcherTimer _trayTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly TrayIcon _tray = new() { ToolTipText = "Ooshak" };
    private readonly WindowIcon _iconOn = LoadIcon("tray-on.png");
    private readonly WindowIcon _iconOff = LoadIcon("tray-off.png");
    private NativeMenuItem _muteItem = new();
    private MainWindow? _main;
    private PermissionWindow? _permissionWindow;
    private bool? _trayMuted;

    public MacController(Application app, IClassicDesktopStyleApplicationLifetime desktop, LogBook log)
    {
        _app = app;
        _desktop = desktop;
        Log = log;
        Config = OoshakConfig.Load();
    }

    public LogBook Log { get; }
    public OoshakConfig Config { get; }
    public PermissionBroker Permissions { get; } = new();
    public SessionBoard Board { get; } = new(SessionBoard.DefaultPath);
    public VoiceLoop? Loop { get; private set; }
    public string? StartError { get; private set; }
    public bool IsStarting { get; private set; } = true;
    public bool IsExiting { get; private set; }

    /// <summary>True until the speech models have been downloaded.</summary>
    public bool NeedsSetup { get; private set; }

    public static bool HasAccessibility => MacAccessibility.IsTrusted(prompt: false);

    public async Task StartAsync(bool hidden)
    {
        L.Language = Config.Language;
        UpgradeHooks();
        BuildTray();
        TrayIcon.SetIcons(_app, [_tray]);

        _main = new MainWindow(this);
        _permissionWindow = new PermissionWindow(Permissions);
        NeedsSetup = ModelCatalog.Missing(OoshakConfig.ModelsDir).Count > 0;

        if (!hidden || NeedsSetup || !HasAccessibility)
            ShowMain();

        _trayTimer.Tick += (_, _) => RefreshTray();
        _trayTimer.Start();

        if (NeedsSetup)
        {
            IsStarting = false;
            return;
        }

        await StartLoopAsync();
    }

    public async Task InstallModelsAsync(IProgress<ModelProgress> progress)
    {
        await ModelDownloader.DownloadAsync(ModelCatalog.Missing(OoshakConfig.ModelsDir), OoshakConfig.ModelsDir, progress, CancellationToken.None);
        NeedsSetup = false;
        IsStarting = true;
        await StartLoopAsync();
    }

    public VoiceSnapshot Snapshot() => Loop?.Snapshot() ?? Offline;

    public void ShowMain()
    {
        if (_main is null)
            return;

        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
            _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public void ShowWaiting()
    {
        ShowMain();
        _main?.ShowWaiting();
    }

    /// <summary>Adds the hooks a newer version needs to a settings file that an older one connected.</summary>
    private void UpgradeHooks()
    {
        try
        {
            if (HookInstaller.HasAny() && !HookInstaller.IsInstalled())
            {
                HookInstaller.Install(Config.Port);
                Log.Add(L.T("Claude Code bağlantısı güncellendi."));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Add(e.Message);
        }
    }

    public void ToggleMute()
    {
        if (Loop is not null)
            Loop.Muted = !Loop.Muted;
    }

    public void ToggleReading() => Loop?.ToggleReading();

    /// <summary>Asks macOS for the right to read and drive the Claude window.</summary>
    public static void RequestAccessibility()
    {
        if (!MacAccessibility.IsTrusted(prompt: true))
            Open(AccessibilitySettings);
    }

    public static void OpenMicrophoneSettings() => Open(MicrophoneSettings);

    public static void Open(string target)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { target } });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Nothing to open with; the window still says what is missing.
        }
    }

    /// <summary>Rebuilds the windows and menus in the new language and reloads speech for it.</summary>
    public async Task ChangeLanguageAsync()
    {
        L.Language = Config.Language;
        BuildTray();

        var oldMain = _main;
        _main = new MainWindow(this);
        _permissionWindow?.Retire();
        _permissionWindow = new PermissionWindow(Permissions);
        oldMain?.Retire();
        ShowMain();

        await ReloadAsync();
    }

    /// <summary>Restarts the microphone and models after a setting that they depend on changed.</summary>
    public async Task ReloadAsync()
    {
        var old = Loop;
        Loop = null;
        IsStarting = true;

        if (old is not null)
            await Task.Run(old.Dispose);

        await StartLoopAsync();
    }

    public void Exit()
    {
        IsExiting = true;
        Permissions.Release();
        _permissionWindow?.Retire();
        _main?.Retire();
        _desktop.Shutdown();
    }

    private async Task StartLoopAsync()
    {
        StartError = null;

        try
        {
            Loop = await Task.Run(() =>
            {
                var loop = new VoiceLoop(Config, Log, Permissions, Board);
                loop.Start();
                return loop;
            });
            Log.Add(L.T("Dinleme başladı, {0} oturum bulundu", SessionCatalog.LoadRecent().Count));
        }
        catch (Exception e)
        {
            StartError = e.Message;
            Log.Add(L.T("Başlatılamadı: {0}", e.Message));
        }
        finally
        {
            IsStarting = false;
        }
    }

    private void BuildTray()
    {
        var menu = new NativeMenu();

        var open = new NativeMenuItem(L.T("Ooshak'ı aç"));
        open.Click += (_, _) => ShowMain();
        menu.Add(open);

        var waiting = new NativeMenuItem(L.T("Bekleyen oturumlar"));
        waiting.Click += (_, _) => ShowWaiting();
        menu.Add(waiting);

        _muteItem = new NativeMenuItem();
        _muteItem.Click += (_, _) => ToggleMute();
        menu.Add(_muteItem);

        var reading = new NativeMenuItem(L.T("Okumayı durdur veya sürdür"));
        reading.Click += (_, _) => ToggleReading();
        menu.Add(reading);

        menu.Add(new NativeMenuItemSeparator());

        var quit = new NativeMenuItem(L.T("Çıkış"));
        quit.Click += (_, _) => Exit();
        menu.Add(quit);

        _tray.Menu = menu;
        _tray.IsVisible = true;
        _trayMuted = null;
        RefreshTray();
    }

    private void RefreshTray()
    {
        var failed = StartError is not null;
        var muted = Snapshot().State is VoiceState.Muted or VoiceState.Paused;
        if (_trayMuted == muted)
            return;

        _trayMuted = muted;
        _muteItem.Header = L.T(muted ? "Mikrofonu aç" : "Mikrofonu kapat");
        _tray.ToolTipText = L.T(failed ? "Ooshak: başlatılamadı" : muted ? "Ooshak: kapalı" : "Ooshak: dinliyor");
        _tray.Icon = muted || failed ? _iconOff : _iconOn;
    }

    private static WindowIcon LoadIcon(string name) =>
        new(AssetLoader.Open(new Uri($"avares://Ooshak/Assets/{name}")));

    public void Dispose()
    {
        _trayTimer.Stop();
        _tray.IsVisible = false;
        _tray.Dispose();
        Loop?.Dispose();
    }
}
