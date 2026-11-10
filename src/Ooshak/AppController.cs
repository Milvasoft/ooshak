using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Ooshak.Core;
using Forms = System.Windows.Forms;

namespace Ooshak;

/// <summary>Owns the voice loop, the tray icon and the two windows.</summary>
public sealed class AppController : IDisposable
{
    private const int MuteHotkeyId = 1;
    private const int StopHotkeyId = 2;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Ooshak";
    private const double WorkingSlosh = 0.45;
    private const int FullscreenPollsToSwitch = 2;

    private static readonly Color BrassColor = Color.FromRgb(0xE9, 0xB4, 0x4C);
    private static readonly Color HoneyColor = Color.FromRgb(0xF6, 0xA8, 0x23);
    private static readonly Color TealColor = Color.FromRgb(0x43, 0xC6, 0xAC);
    private static readonly Color SlateColor = Color.FromRgb(0x5B, 0x57, 0x50);
    private static readonly VoiceSnapshot Offline = new(VoiceState.Muted, 0, null, null, null, 0);

    private readonly HotkeyWindow _hotkeys = new();
    private readonly Forms.NotifyIcon _tray = new() { Visible = true, Text = "Ooshak" };
    private Forms.ToolStripMenuItem _muteItem = new();
    private readonly DispatcherTimer _trayTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private MainWindow? _main;
    private IslandWindow? _island;
    private PermissionWindow? _permissionWindow;
    private bool? _trayMuted;
    private int _fullscreenStreak;

    public AppController(LogBook log)
    {
        Log = log;
        Config = OoshakConfig.Load();
    }

    public LogBook Log { get; }
    public OoshakConfig Config { get; }
    public PermissionBroker Permissions { get; } = new();
    public VoiceLoop? Loop { get; private set; }
    public string? StartError { get; private set; }
    public bool IsStarting { get; private set; } = true;
    public bool IsExiting { get; private set; }

    public async Task StartAsync(bool hidden)
    {
        L.Language = Config.Language;
        BuildTray();
        _tray.DoubleClick += (_, _) => ShowMain();

        _hotkeys.Pressed += id =>
        {
            if (id == MuteHotkeyId)
                ToggleMute();
            else if (id == StopHotkeyId)
                ToggleReading();
        };
        _hotkeys.Register(MuteHotkeyId, Forms.Keys.M);
        _hotkeys.Register(StopHotkeyId, Forms.Keys.S);

        _main = new MainWindow(this);
        _island = new IslandWindow(this);
        _permissionWindow = new PermissionWindow(Permissions);
        NeedsSetup = ModelCatalog.Missing(OoshakConfig.ModelsDir).Count > 0;

        // Started hidden at login before setup was finished, the window is the only way forward.
        if (!hidden || NeedsSetup)
            ShowMain();

        UpdateIsland();
        _trayTimer.Tick += (_, _) => RefreshTray();
        _trayTimer.Start();
        _fullscreenTimer.Tick += (_, _) => PollFullscreen();
        _fullscreenTimer.Start();

        if (NeedsSetup)
        {
            IsStarting = false;
            return;
        }

        await StartLoopAsync();
    }

    /// <summary>True until the speech models have been downloaded.</summary>
    public bool NeedsSetup { get; private set; }

    public async Task InstallModelsAsync(IProgress<ModelProgress> progress)
    {
        await ModelDownloader.DownloadAsync(ModelCatalog.Missing(OoshakConfig.ModelsDir), OoshakConfig.ModelsDir, progress, CancellationToken.None);
        NeedsSetup = false;
        IsStarting = true;
        await StartLoopAsync();
    }

    public VoiceSnapshot Snapshot() => Loop?.Snapshot() ?? Offline;

    public LiquidFrame LiquidFrame()
    {
        var snapshot = Snapshot();
        return snapshot.State switch
        {
            VoiceState.Muted or VoiceState.Paused => new(0, SlateColor),
            VoiceState.Speaking => new(snapshot.Level, TealColor),
            VoiceState.Working => new(WorkingSlosh, HoneyColor),
            VoiceState.Hearing => new(snapshot.Level, HoneyColor),
            _ => new(snapshot.Level * 0.5, BrassColor),
        };
    }

    public void ShowMain()
    {
        if (_main is null)
            return;

        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
            _main.WindowState = WindowState.Normal;

        _main.Activate();
        UpdateIsland();
    }

    /// <summary>The island stands in for the main window whenever that window is out of sight.</summary>
    public void UpdateIsland()
    {
        if (_island is null || _main is null)
            return;

        var mainOutOfSight = !_main.IsVisible || _main.WindowState == WindowState.Minimized;
        if (Config.IslandEnabled && mainOutOfSight && Loop?.AutoPaused != true)
            _island.Show();
        else
            _island.Hide();
    }

    public void SetIslandEnabled(bool enabled)
    {
        Config.IslandEnabled = enabled;
        Config.Save();
        UpdateIsland();
        _main?.RefreshIslandSetting();
    }

    public void SaveIslandPosition(double left, double top)
    {
        Config.IslandLeft = left;
        Config.IslandTop = top;
        Config.Save();
    }

    public void ToggleMute()
    {
        if (Loop is not null)
            Loop.Muted = !Loop.Muted;
    }

    public void ToggleReading() => Loop?.ToggleReading();

    /// <summary>Rebuilds the windows and menus in the new language and reloads speech for it.</summary>
    public async Task ChangeLanguageAsync()
    {
        L.Language = Config.Language;
        BuildTray();

        var oldMain = _main;
        var oldIsland = _island;
        _main = new MainWindow(this);
        _island = new IslandWindow(this);
        _permissionWindow?.Retire();
        _permissionWindow = new PermissionWindow(Permissions);
        oldMain?.Retire();
        oldIsland?.Close();
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

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is not null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --hidden");
            else
                key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    public void Exit()
    {
        IsExiting = true;
        Permissions.Release();
        _permissionWindow?.Retire();
        _main?.Close();
        _island?.Close();
        Application.Current.Shutdown();
    }

    private async Task StartLoopAsync()
    {
        StartError = null;

        try
        {
            Loop = await Task.Run(() =>
            {
                var loop = new VoiceLoop(Config, Log, Permissions);
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

    // Waiting for a repeat keeps a quick Alt+Tab from flipping the state.
    private void PollFullscreen()
    {
        if (Loop is not { } loop)
            return;

        var fullscreen = Config.AutoPauseFullscreen && FullscreenDetector.IsActive();
        _fullscreenStreak = fullscreen == loop.AutoPaused ? 0 : _fullscreenStreak + 1;
        if (_fullscreenStreak < FullscreenPollsToSwitch)
            return;

        _fullscreenStreak = 0;
        loop.AutoPaused = fullscreen;
        Log.Add(L.T(fullscreen ? "Tam ekran algılandı, duraklatıldı" : "Tam ekran bitti, devam ediliyor"));
        UpdateIsland();
    }

    private void BuildTray()
    {
        var previousMenu = _tray.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        _muteItem = new Forms.ToolStripMenuItem();
        menu.Items.Add(L.T("Ooshak'ı aç"), null, (_, _) => ShowMain());
        _muteItem.Click += (_, _) => ToggleMute();
        menu.Items.Add(_muteItem);
        menu.Items.Add(L.T("Okumayı durdur veya sürdür") + "  (Ctrl+Alt+S)", null, (_, _) => ToggleReading());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L.T("Çıkış"), null, (_, _) => Exit());
        _tray.ContextMenuStrip = menu;
        previousMenu?.Dispose();
        _trayMuted = null;
        RefreshTray();
    }

    private void RefreshTray()
    {
        var failed = StartError is not null;
        var muted = Snapshot().State is VoiceState.Muted or VoiceState.Paused;
        if (_trayMuted == muted && _tray.Icon is not null)
            return;

        _trayMuted = muted;
        _muteItem.Text = L.T(muted ? "Mikrofonu aç" : "Mikrofonu kapat") + "  (Ctrl+Alt+M)";
        _tray.Text = L.T(failed ? "Ooshak: başlatılamadı" : muted ? "Ooshak: kapalı" : "Ooshak: dinliyor");

        var wings = failed ? System.Drawing.Color.FromArgb(0xEF, 0x6F, 0x5C)
            : muted ? System.Drawing.Color.FromArgb(0x7A, 0x75, 0x6C)
            : System.Drawing.Color.FromArgb(0xE9, 0xB4, 0x4C);

        using var bitmap = LogoPainter.Render(32, wings, System.Drawing.Color.FromArgb(0xF4, 0xEE, 0xE3));
        var handle = bitmap.GetHicon();
        var previous = _tray.Icon;
        using (var temporary = System.Drawing.Icon.FromHandle(handle))
            _tray.Icon = (System.Drawing.Icon)temporary.Clone();

        NativeMethods.DestroyIcon(handle);
        previous?.Dispose();
    }

    public void Dispose()
    {
        _trayTimer.Stop();
        _fullscreenTimer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _hotkeys.Dispose();
        Loop?.Dispose();
    }
}
