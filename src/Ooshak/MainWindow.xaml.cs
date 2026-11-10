using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ooshak.Core;

namespace Ooshak;

public sealed class AliasRow
{
    public string Alias { get; set; } = "";
    public string? Title { get; set; }
    public required IReadOnlyList<string> Sessions { get; init; }
}

public partial class MainWindow : Window
{
    private const string MicOnGlyph = "";
    private const string MicOffGlyph = "";
    private const string DefaultDeviceName = "Windows varsayılanı";
    private const string NoGeneralSession = "Yok";
    private const int MinWakeNameLength = 3;
    private const string PreviewSentence = "Atlas: branch'i merge ettim, commit attım, deploy tamam.";
    private const string EnglishPreviewSentence = "Atlas: I merged the branch, committed, and the deploy is done.";

    private static string NoGeneral => L.T(NoGeneralSession);

    private readonly AppController _controller;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly ObservableCollection<LogEntry> _log = [];
    private readonly ObservableCollection<AliasRow> _aliases = [];
    private readonly List<string> _enrolled = [];
    private IReadOnlyList<string> _sessionTitles = [];
    private bool _retired;
    private string _loadedAddress = "";

    public MainWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;

        HeroLiquid.Source = controller.LiquidFrame;
        LogList.ItemsSource = _log;
        AliasList.ItemsSource = _aliases;

        foreach (var entry in controller.Log.Recent())
            _log.Add(entry);

        controller.Log.Added += OnLogAdded;
        controller.Log.Cleared += () => Dispatcher.BeginInvoke(() =>
        {
            _log.Clear();
            RefreshLogEmpty();
        });

        LoadSettings();
        RefreshLogEmpty();

        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();

        StateChanged += (_, _) => _controller.UpdateIsland();
        IsVisibleChanged += (_, _) => _controller.UpdateIsland();
    }

    public void RefreshIslandSetting() => IslandSwitch.IsChecked = _controller.Config.IslandEnabled;

    /// <summary>Closes this window for good, because a language change builds a new one.</summary>
    public void Retire()
    {
        _retired = true;
        _statusTimer.Stop();
        _controller.Log.Added -= OnLogAdded;
        Close();
    }

    private void RefreshStatus()
    {
        var snapshot = _controller.Snapshot();
        var (title, color) = Describe(snapshot);

        HeroTitle.Text = title;
        HeroSubtitle.Text = Subtitle(snapshot);
        SideStatus.Text = title;
        SideDot.Fill = color;
        MicButton.Content = snapshot.State is VoiceState.Muted or VoiceState.Paused ? MicOffGlyph : MicOnGlyph;

        TargetText.Text = snapshot.Target ?? L.T("Seçili değil");
        LastCommandText.Text = snapshot.LastCommand ?? L.T("Henüz yok");
        LastCommandText.ToolTip = snapshot.LastCommand;
        AwaitingText.Text = snapshot.AwaitingReplies.ToString();
        ReadingButton.Content = L.T(_controller.Loop?.ReadingPaused == true ? "Devam et" : "Okumayı durdur");

        SetupCard.Visibility = _controller.NeedsSetup ? Visibility.Visible : Visibility.Collapsed;
        HeroCard.Visibility = _controller.NeedsSetup ? Visibility.Collapsed : Visibility.Visible;
        if (_controller.NeedsSetup && HookInstaller.IsInstalled())
        {
            SetupConnectButton.Content = L.T("Claude Code'a bağlı");
            SetupConnectButton.IsEnabled = false;
        }
    }

    private async void OnInstallModels(object sender, RoutedEventArgs e)
    {
        SetupButton.IsEnabled = false;
        SetupProgress.Visibility = Visibility.Visible;

        var progress = new Progress<ModelProgress>(p =>
        {
            SetupProgress.Value = p.Total == 0 ? 0 : (double)p.Done / p.Total;
            SetupStatus.Text = L.T("{0} indiriliyor · {1} / {2} MB", p.File, p.Done >> 20, p.Total >> 20);
        });

        try
        {
            await _controller.InstallModelsAsync(progress);
            SetupStatus.Text = L.T("Modeller hazır. Başlatılıyor.");
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            SetupStatus.Text = L.T("İndirme başarısız: {0}", exception.Message);
            SetupButton.IsEnabled = true;
        }
    }

    private void OnSetupConnect(object sender, RoutedEventArgs e)
    {
        HookInstaller.Install(_controller.Config.Port);
        RefreshHookStatus();
    }

    private (string Title, Brush Color) Describe(VoiceSnapshot snapshot)
    {
        if (_controller.StartError is not null)
            return (L.T("Başlatılamadı"), (Brush)FindResource("Coral"));

        if (_controller.NeedsSetup)
            return (L.T("Kurulum gerekli"), (Brush)FindResource("Muted"));

        if (_controller.IsStarting)
            return (L.T("Başlıyor"), (Brush)FindResource("Muted"));

        return snapshot.State switch
        {
            VoiceState.Muted => (L.T("Kapalı"), (Brush)FindResource("Muted")),
            VoiceState.Paused => (L.T("Duraklatıldı"), (Brush)FindResource("Muted")),
            VoiceState.Speaking => (L.T("Okuyorum"), (Brush)FindResource("Teal")),
            VoiceState.Working => (L.T("İletiyorum"), (Brush)FindResource("Brass")),
            VoiceState.Hearing => (L.T("Duyuyorum"), (Brush)FindResource("Brass")),
            _ => (L.T("Dinliyorum"), (Brush)FindResource("Brass")),
        };
    }

    private string Subtitle(VoiceSnapshot snapshot)
    {
        if (_controller.StartError is { } error)
            return error;

        if (_controller.IsStarting)
            return L.T("Modeller yükleniyor.");

        if (snapshot.State == VoiceState.Muted)
            return L.T("Mikrofon kapalı, hiçbir şey dinlenmiyor.");

        if (snapshot.State == VoiceState.Paused)
            return L.T("Tam ekran bir uygulama açık. Kapanınca kendiliğinden devam edeceğim.");

        if (snapshot.Note is { } note)
            return note;

        var name = _controller.Config.WakeNames.FirstOrDefault() ?? "Ooshak";
        return L.T("“{0}, Atlas'a söyle testleri çalıştırsın” diye seslen.", Capitalize(name));
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + text[1..];

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (PageHome is null)
            return;

        PageHome.Visibility = NavHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageLog.Visibility = NavLog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (NavSettings.IsChecked == true)
            LoadSettings();
        else
            CancelEnrollment();
    }

    private void LoadSettings()
    {
        var config = _controller.Config;
        _sessionTitles = SessionCatalog.LoadRecent().Select(s => s.Title).Distinct().ToList();

        WakeNamesBox.Text = string.Join(", ", config.WakeNames);
        VocabularyBox.Text = string.Join(", ", config.Vocabulary);
        SilenceSlider.Value = config.SilenceMs;
        ReplyModeBox.SelectedIndex = (int)config.ReplyMode;

        var mediator = string.IsNullOrWhiteSpace(config.MediatorSessionTitle) ? config.GeneralSessionTitle : config.MediatorSessionTitle;
        MediatorBox.ItemsSource = string.IsNullOrWhiteSpace(mediator) ? _sessionTitles : SessionsIncluding(mediator);
        MediatorBox.SelectedItem = mediator;

        AssistantStyleBox.Text = config.ReplyInstruction();
        LanguageBox.SelectedIndex = config.Language == L.English ? 1 : 0;
        AddressBox.Text = config.AddressAs;
        _loadedAddress = config.AddressAs;
        PronunciationPanel.Visibility = L.IsEnglish ? Visibility.Collapsed : Visibility.Visible;
        FollowUpSlider.Value = config.FollowUpSeconds;
        StickySlider.Value = config.StickyMinutes;

        var generalChoices = string.IsNullOrWhiteSpace(config.GeneralSessionTitle)
            ? _sessionTitles
            : SessionsIncluding(config.GeneralSessionTitle);
        GeneralSessionBox.ItemsSource = new[] { NoGeneral }.Concat(generalChoices).ToList();
        GeneralSessionBox.SelectedItem = string.IsNullOrWhiteSpace(config.GeneralSessionTitle)
            ? NoGeneral
            : config.GeneralSessionTitle;
        RateSlider.Value = config.SpeechRate;
        PronunciationBox.Text = SpeechLexicon.Format(config.Pronunciations);
        IslandSwitch.IsChecked = config.IslandEnabled;
        AutoPauseSwitch.IsChecked = config.AutoPauseFullscreen;
        PermissionSwitch.IsChecked = config.PermissionPopup;
        AutostartSwitch.IsChecked = AppController.StartsWithWindows;
        SaveStatus.Text = "";

        var devices = new List<string> { L.T(DefaultDeviceName) };
        devices.AddRange(AudioListener.DeviceNames());
        MicrophoneBox.ItemsSource = devices;
        MicrophoneBox.SelectedIndex = Math.Clamp(config.MicrophoneDevice + 1, 0, devices.Count - 1);

        _aliases.Clear();
        foreach (var (alias, title) in config.Aliases)
            _aliases.Add(new AliasRow { Alias = alias, Title = title, Sessions = SessionsIncluding(title) });

        RefreshHookStatus();
    }

    private IReadOnlyList<string> SessionsIncluding(string title) =>
        _sessionTitles.Contains(title) ? _sessionTitles : [title, .. _sessionTitles];

    private void RefreshHookStatus()
    {
        var installed = HookInstaller.IsInstalled();
        HookStatus.Text = installed
            ? L.T("Bağlı. Sesle konuştuğun oturumların cevapları okunur.")
            : L.T("Bağlı değil. Mesaj gider ama cevaplar okunmaz.");
        HookButton.Content = L.T(installed ? "Bağlantıyı kaldır" : "Bağla");
    }

    private async void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        var names = SplitList(WakeNamesBox.Text);
        if (names.Length == 0 || names.Any(n => VoiceRouter.Key(n).Length < MinWakeNameLength))
        {
            SaveStatus.Text = L.T("Çağrı adı en az 3 harf olmalı.");
            WakeNamesBox.Focus();
            return;
        }

        if (ReplyModeBox.SelectedIndex == (int)ReplyReadMode.SelectedSession && MediatorBox.SelectedItem is null)
        {
            SaveStatus.Text = L.T("Aracı oturumu seç.");
            return;
        }

        var config = _controller.Config;
        var device = MicrophoneBox.SelectedIndex - 1;
        var silence = (int)SilenceSlider.Value;
        var language = LanguageBox.SelectedIndex == 1 ? L.English : L.Turkish;
        var languageChanged = language != config.Language;
        var needsReload = device != config.MicrophoneDevice || silence != config.SilenceMs;

        config.WakeNames = names;
        config.Vocabulary = SplitList(VocabularyBox.Text);
        config.MicrophoneDevice = device;
        config.SilenceMs = silence;
        var previousMediator = config.ActiveMediator;
        config.ReplyMode = (ReplyReadMode)ReplyModeBox.SelectedIndex;
        config.MediatorSessionTitle = MediatorBox.SelectedItem as string ?? "";
        if (config.ActiveMediator != previousMediator)
            _controller.Loop?.ForgetInstructions();

        config.VoiceReplyInstruction = AssistantStyleBox.Text.Trim();
        config.FollowUpSeconds = (int)FollowUpSlider.Value;
        config.StickyMinutes = (int)StickySlider.Value;
        config.GeneralSessionTitle = GeneralSessionBox.SelectedItem as string is { } general && general != NoGeneral ? general : "";
        config.SpeechRate = RateSlider.Value;
        config.Pronunciations = SpeechLexicon.Parse(PronunciationBox.Text);
        config.Aliases = _aliases
            .Where(a => !string.IsNullOrWhiteSpace(a.Alias) && !string.IsNullOrWhiteSpace(a.Title))
            .GroupBy(a => a.Alias.Trim())
            .ToDictionary(g => g.Key, g => g.First().Title!);
        config.IslandEnabled = IslandSwitch.IsChecked == true;
        config.AutoPauseFullscreen = AutoPauseSwitch.IsChecked == true;
        config.PermissionPopup = PermissionSwitch.IsChecked == true;
        // Compared with what was shown, so an open page cannot undo a name set by voice meanwhile.
        var address = VoiceRouter.NormalizeAddress(AddressBox.Text.Trim());
        AddressBox.Text = address;
        if (address != _loadedAddress)
        {
            config.AddressAs = address;
            _loadedAddress = address;
            _controller.Loop?.ForgetInstructions();
        }

        config.Language = language;
        config.Save();

        AppController.StartsWithWindows = AutostartSwitch.IsChecked == true;

        if (languageChanged)
        {
            await _controller.ChangeLanguageAsync();
            return;
        }

        _controller.UpdateIsland();
        _controller.Loop?.ApplyNames();

        if (needsReload)
        {
            SaveStatus.Text = L.T("Kaydedildi, mikrofon yeniden başlıyor.");
            await _controller.ReloadAsync();
        }

        SaveStatus.Text = L.T("Kaydedildi.");
    }

    private static string[] SplitList(string text) =>
        text.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToArray();

    private void OnSilenceChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        SilenceValue.Text = $"{e.NewValue / 1000:0.00} {L.T("sn")}";

    private void OnReplyModeChanged(object sender, SelectionChangedEventArgs e) =>
        MediatorPanel.Visibility = ReplyModeBox.SelectedIndex == (int)ReplyReadMode.SelectedSession ? Visibility.Visible : Visibility.Collapsed;

    private void OnStickyChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        StickyValue.Text = $"{e.NewValue:0} {L.T("dk")}";

    private void OnFollowUpChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        FollowUpValue.Text = e.NewValue == 0 ? L.T("Kapalı") : $"{e.NewValue:0} {L.T("sn")}";

    private void OnRateChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        RateValue.Text = $"{e.NewValue:0.00}x";

    private void OnPreviewSpeech(object sender, RoutedEventArgs e) =>
        _controller.Loop?.Preview(L.IsEnglish ? EnglishPreviewSentence : PreviewSentence, RateSlider.Value, SpeechLexicon.Parse(PronunciationBox.Text));

    private void OnAddAlias(object sender, RoutedEventArgs e) =>
        _aliases.Add(new AliasRow { Sessions = _sessionTitles });

    private void OnRemoveAlias(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AliasRow row })
            _aliases.Remove(row);
    }

    private void OnToggleHooks(object sender, RoutedEventArgs e)
    {
        if (HookInstaller.IsInstalled())
            HookInstaller.Remove(_controller.Config.Port);
        else
            HookInstaller.Install(_controller.Config.Port);

        RefreshHookStatus();
    }

    private void OnStartEnrollment(object sender, RoutedEventArgs e)
    {
        if (_controller.Loop is not { } loop)
            return;

        loop.Muted = false;
        EnrollPanel.Visibility = Visibility.Visible;
        ResetEnrollment();
        loop.BeginEnrollment(OnEnrollmentTake);
    }

    private void OnEnrollmentTake(string? name) => Dispatcher.BeginInvoke(() => AddEnrollmentTake(name));

    private void AddEnrollmentTake(string? name)
    {
        if (EnrollPanel.Visibility != Visibility.Visible || _enrolled.Count >= WakeEnrollment.RequiredTakes)
            return;

        if (name is null)
        {
            EnrollStatus.Text = L.T("Anlayamadım. Yalnızca çağrı adını söyle.");
            return;
        }

        _enrolled.Add(name);
        ShowEnrollmentProgress();
    }

    private void ShowEnrollmentProgress()
    {
        var dots = new Ellipse[] { EnrollDot1, EnrollDot2, EnrollDot3 };
        for (var i = 0; i < dots.Length; i++)
            dots[i].Fill = (Brush)FindResource(i < _enrolled.Count ? "Brass" : "Ink600");

        EnrollHeard.Text = string.Join(" · ", _enrolled);
        EnrollSave.IsEnabled = _enrolled.Count >= WakeEnrollment.RequiredTakes;
        EnrollStatus.Text = EnrollSave.IsEnabled
            ? L.T("Tamam. Duyulanlar doğruysa kullan.")
            : L.T("Çağrı adını {0} kez ayrı ayrı söyle. Her seferinde bip duyacaksın.", WakeEnrollment.RequiredTakes);
    }

    private void ResetEnrollment()
    {
        _enrolled.Clear();
        ShowEnrollmentProgress();
    }

    private void OnRestartEnrollment(object sender, RoutedEventArgs e) => ResetEnrollment();

    private void OnCancelEnrollment(object sender, RoutedEventArgs e) => CancelEnrollment();

    private void CancelEnrollment()
    {
        _controller.Loop?.EndEnrollment();
        EnrollPanel.Visibility = Visibility.Collapsed;
    }

    private void OnSaveEnrollment(object sender, RoutedEventArgs e)
    {
        var config = _controller.Config;
        config.WakeNames = WakeEnrollment.Variants(_enrolled);
        config.Save();
        _controller.Loop?.ApplyNames();
        WakeNamesBox.Text = string.Join(", ", config.WakeNames);
        _controller.Log.Add(L.T("Çağrı adı: {0}", WakeNamesBox.Text));
        CancelEnrollment();
        SaveStatus.Text = L.T("Çağrı adı kaydedildi.");
    }

    private void OnLogAdded(LogEntry entry) => Dispatcher.BeginInvoke(() =>
    {
        _log.Add(entry);
        RefreshLogEmpty();
        if (PageLog.IsVisible)
            LogList.ScrollIntoView(entry);
    });

    private void RefreshLogEmpty() =>
        LogEmpty.Visibility = _log.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnClearLog(object sender, RoutedEventArgs e) => _controller.Log.Clear();

    private void OnOpenLogFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(OoshakConfig.AppDataDir);
        Process.Start("explorer.exe", $"\"{OoshakConfig.AppDataDir}\"");
    }

    private void OnToggleMute(object sender, RoutedEventArgs e) => _controller.ToggleMute();

    private void OnStopSpeaking(object sender, RoutedEventArgs e) => _controller.ToggleReading();

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // Closing only hides the window. The app keeps listening until Exit in the tray menu.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_controller.IsExiting && !_retired)
        {
            e.Cancel = true;
            CancelEnrollment();
            Hide();
        }

        base.OnClosing(e);
    }
}
