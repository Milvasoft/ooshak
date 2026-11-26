using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ooshak.Core;

namespace Ooshak.Mac;

/// <summary>Status, first-run setup, macOS permissions, settings and the log, in three tabs.</summary>
public sealed class MainWindow : Window
{
    private const string NoGeneralSession = "Yok";
    private const string DefaultDeviceName = "Varsayılan mikrofon";
    private const int MinWakeNameLength = 3;
    private const string PreviewSentence = "Atlas: branch'i merge ettim, commit attım, deploy tamam.";
    private const string EnglishPreviewSentence = "Atlas: I merged the branch, committed, and the deploy is done.";

    private readonly MacController _controller;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _waitingTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly ObservableCollection<string> _log = [];

    private readonly TextBlock _title = new() { FontSize = 24, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _subtitle = new() { Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _level = new() { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _target = new() { Foreground = Ui.Text };
    private readonly TextBlock _lastCommand = new() { Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _awaiting = new() { Foreground = Ui.Text };
    private readonly Button _muteButton = new();
    private readonly Button _readingButton = new();

    private readonly Border _setupCard;
    private readonly Button _downloadButton = new();
    private readonly ProgressBar _downloadProgress = new() { Minimum = 0, Maximum = 1, Height = 6, IsVisible = false };
    private readonly TextBlock _downloadStatus = Ui.Note("");

    private readonly TextBlock _accessStatus = Ui.Note("");
    private readonly Button _accessButton = new();
    private readonly TextBlock _hookStatus = Ui.Note("");
    private readonly Button _hookButton = new();

    private readonly ComboBox _language = new() { ItemsSource = new[] { "Türkçe", "English" }, MinWidth = 220 };
    private readonly TextBox _wakeNames = new();
    private readonly ComboBox _microphone = new() { MinWidth = 320 };
    private readonly ComboBox _replyMode = new() { MinWidth = 320 };
    private readonly ComboBox _mediator = new() { MinWidth = 320 };
    private readonly StackPanel _mediatorPanel = new();
    private readonly ComboBox _general = new() { MinWidth = 320 };
    private readonly Slider _rate = new() { Minimum = Speaker.MinRate, Maximum = Speaker.MaxRate, Width = 260 };
    private readonly TextBlock _rateValue = new() { Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _permissionPopup = new();
    private readonly CheckBox _startAtLogin = new();
    private readonly TextBlock _saveStatus = Ui.Note("");
    private readonly StackPanel _waitingList = new();
    private readonly TabItem _waitingTab = new();
    private readonly WaitingCards _waitingCards;
    private TabControl _tabs = new();
    private List<string> _sessionTitles = [];
    private bool _retired;

    public MainWindow(MacController controller)
    {
        _controller = controller;
        Title = "Ooshak";
        Width = 640;
        Height = 720;
        MinWidth = 520;
        MinHeight = 480;
        Background = Ui.Background;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _setupCard = BuildSetup();
        _waitingCards = new WaitingCards(() => GetTopLevel(this), controller.Board, controller.Permissions, controller.Log.Add);
        _waitingTab.Content = Scroll(new StackPanel
        {
            Children =
            {
                Ui.Note(L.T("İzin isteyen, soru soran ve işini bitirip seni bekleyen oturumlar. Oturum yeniden çalışınca kart kendiliğinden kalkar.")),
                _waitingList,
            },
        });
        _tabs = new TabControl
        {
            Margin = new Thickness(12),
            ItemsSource = new[]
            {
                new TabItem { Header = L.T("Durum"), Content = Scroll(BuildStatus()) },
                _waitingTab,
                new TabItem { Header = L.T("Ayarlar"), Content = Scroll(BuildSettings()) },
                new TabItem { Header = L.T("Günlük"), Content = BuildLog() },
            },
        };
        Content = _tabs;
        _controller.Board.Changed += OnBoardChanged;
        RefreshWaiting();

        foreach (var entry in _controller.Log.Recent())
            _log.Insert(0, Format(entry));
        _controller.Log.Added += OnLogAdded;
        _controller.Log.Cleared += OnLogCleared;

        _statusTimer.Tick += (_, _) => RefreshStatus();
        _waitingTimer.Tick += (_, _) => RefreshWaiting();
        _waitingTimer.Start();
        _statusTimer.Start();
        RefreshStatus();
        LoadSettings();

        // The red button hides the window; Ooshak keeps listening from the menu bar.
        Closing += (_, e) =>
        {
            if (!_controller.IsExiting && !_retired)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public void Retire()
    {
        _statusTimer.Stop();
        _waitingTimer.Stop();
        _controller.Board.Changed -= OnBoardChanged;
        _controller.Log.Added -= OnLogAdded;
        _controller.Log.Cleared -= OnLogCleared;
        _retired = true;
        Close();
    }

    public void ShowWaiting() => _tabs.SelectedItem = _waitingTab;

    private void OnBoardChanged() => Dispatcher.UIThread.Post(RefreshWaiting);

    private void RefreshWaiting()
    {
        var count = _controller.Board.Count;
        _waitingTab.Header = count == 0 ? L.T("Bekleyenler") : $"{L.T("Bekleyenler")} · {count}";
        _waitingCards.Fill(_waitingList);
    }

    private static ScrollViewer Scroll(Control content) => new() { Content = content, Padding = new Thickness(4, 12, 12, 12) };

    private StackPanel BuildStatus()
    {
        _muteButton.Click += (_, _) => _controller.ToggleMute();
        _readingButton.Click += (_, _) => _controller.ToggleReading();
        _accessButton.Content = L.T("İzin ver");
        _accessButton.Click += (_, _) => MacController.RequestAccessibility();
        _hookButton.Click += (_, _) => ToggleHooks();

        var hero = Ui.Section(
            _title,
            _subtitle,
            _level,
            new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                Margin = new Thickness(0, 12, 0, 4),
                Children =
                {
                    Cell(Ui.Label(L.T("Hedef oturum")), 0, 0), Cell(_target, 0, 1),
                    Cell(Ui.Label(L.T("Son komut")), 1, 0), Cell(_lastCommand, 1, 1),
                    Cell(Ui.Label(L.T("Bekleyen cevap")), 2, 0), Cell(_awaiting, 2, 1),
                },
            },
            Ui.Row(_muteButton, _readingButton));

        var permissions = Ui.Section(
            Ui.Heading(L.T("Erişilebilirlik izni")),
            _accessStatus,
            _accessButton,
            Ui.Heading(L.T("Mikrofon izni")),
            Ui.Note(L.T("macOS ilk dinlemede mikrofon izni sorar. Reddettiysen Sistem Ayarları'ndan aç.")),
            Ui.Button(L.T("Mikrofon ayarlarını aç"), (_, _) => MacController.OpenMicrophoneSettings()));

        var connection = Ui.Section(Ui.Heading(L.T("Claude Code bağlantısı")), _hookStatus, _hookButton);

        var page = new StackPanel();
        page.Children.AddRange([_setupCard, hero, permissions, connection]);
        return page;
    }

    private static Control Cell(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        control.Margin = new Thickness(column == 0 ? 0 : 16, 4, 0, 4);
        return control;
    }

    private Border BuildSetup()
    {
        _downloadButton.Content = L.T("Modelleri indir");
        _downloadButton.Click += async (_, _) => await InstallModelsAsync();

        return Ui.Section(
            Ui.Heading(L.T("Hoş geldin")),
            Ui.Note(L.T("Ooshak konuşmanı bu Mac'te tanır ve cevapları burada okur, sesin dışarı gönderilmez. Bunun için bir kez yaklaşık 1,6 GB model indirilecek: Whisper ve Silero. Cevaplar macOS'un kendi sesleriyle okunur.")),
            _downloadButton,
            _downloadProgress,
            _downloadStatus);
    }

    private StackPanel BuildSettings()
    {
        _replyMode.ItemsSource = new[]
        {
            L.T("Sadece sesle başlattıklarım"),
            L.T("Ekranda açık olan oturum"),
            L.T("Tüm oturumlar"),
            L.T("Seçtiğim oturum (aracı)"),
        };
        _replyMode.SelectionChanged += (_, _) =>
            _mediatorPanel.IsVisible = _replyMode.SelectedIndex == (int)ReplyReadMode.SelectedSession;
        _mediatorPanel.Children.AddRange([Ui.Label(L.T("Aracı oturum")), _mediator]);

        _rate.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
                _rateValue.Text = $"{_rate.Value:0.00}x";
        };

        _permissionPopup.Content = L.T("İzinleri popup ile sor");
        _startAtLogin.Content = L.T("Oturum açınca başlat");

        var speech = Ui.Section(
            Ui.Label(L.T("Dil")), _language,
            Ui.Label(L.T("Çağrı adları")), _wakeNames,
            Ui.Note(L.T("Ooshak'a böyle seslenirsin. Virgülle birden fazla yazım ekleyebilirsin. En iyisi sesinle öğretmek.")),
            Ui.Label(L.T("Mikrofon")), _microphone,
            Ui.Label(L.T("Okuma hızı")), Ui.Row(_rate, _rateValue),
            Ui.Button(L.T("Örneği dinle"), (_, _) => Preview()));

        var sessions = Ui.Section(
            Ui.Label(L.T("Hangi cevaplar okunsun")), _replyMode,
            _mediatorPanel,
            Ui.Label(L.T("Genel oturum")), _general,
            Ui.Note(L.T("Oturum adı söylemediğin sohbetler buraya gider. “Genele geç” dersen hemen buna dönersin.")));

        var app = Ui.Section(
            _permissionPopup,
            _startAtLogin,
            Ui.Note(L.T("Takma adlar, okunuşlar ve teknik kelimeler ayar dosyasında: ~/.ooshak/config.json")),
            Ui.Row(
                Ui.Button(L.T("Klasörü aç"), (_, _) => MacController.Open(OoshakConfig.AppDataDir)),
                Ui.Button(L.T("Kaydet"), async (_, _) => await SaveAsync())),
            _saveStatus);

        var page = new StackPanel();
        page.Children.AddRange([speech, sessions, app]);
        return page;
    }

    private Control BuildLog()
    {
        var list = new ListBox { ItemsSource = _log, Background = Ui.Card };
        var clear = Ui.Button(L.T("Temizle"), (_, _) => _controller.Log.Clear());
        var panel = new DockPanel { Margin = new Thickness(4, 12, 12, 12) };
        DockPanel.SetDock(clear, Dock.Top);
        panel.Children.Add(clear);
        panel.Children.Add(list);
        return panel;
    }

    private void RefreshStatus()
    {
        var snapshot = _controller.Snapshot();
        var (title, color) = Describe(snapshot);
        _title.Text = title;
        _title.Foreground = color;
        _subtitle.Text = Subtitle(snapshot);
        _level.Value = snapshot.Level;
        _level.Foreground = color;

        _target.Text = snapshot.Target ?? L.T("Seçili değil");
        _lastCommand.Text = snapshot.LastCommand ?? L.T("Henüz yok");
        _awaiting.Text = snapshot.AwaitingReplies.ToString();
        _muteButton.Content = L.T(snapshot.State is VoiceState.Muted or VoiceState.Paused ? "Mikrofonu aç" : "Mikrofonu kapat");
        _readingButton.Content = L.T(_controller.Loop?.ReadingPaused == true ? "Devam et" : "Okumayı durdur");
        _setupCard.IsVisible = _controller.NeedsSetup;

        var trusted = MacController.HasAccessibility;
        _accessStatus.Text = trusted
            ? L.T("Verildi. Ooshak Claude penceresine yazabilir.")
            : L.T("Gerekli. Ooshak bu izin olmadan Claude penceresine yazamaz.");
        _accessButton.IsVisible = !trusted;

        var installed = HookInstaller.IsInstalled();
        _hookStatus.Text = installed
            ? L.T("Bağlı. Sesle konuştuğun oturumların cevapları okunur.")
            : L.T("Bağlı değil. Mesaj gider ama cevaplar okunmaz.");
        _hookButton.Content = L.T(installed ? "Bağlantıyı kaldır" : "Bağla");
    }

    private (string Title, IBrush Color) Describe(VoiceSnapshot snapshot)
    {
        if (_controller.StartError is not null)
            return (L.T("Başlatılamadı"), Ui.Coral);
        if (_controller.NeedsSetup)
            return (L.T("Kurulum gerekli"), Ui.Muted);
        if (_controller.IsStarting)
            return (L.T("Başlıyor"), Ui.Muted);

        return snapshot.State switch
        {
            VoiceState.Muted => (L.T("Kapalı"), Ui.Muted),
            VoiceState.Paused => (L.T("Duraklatıldı"), Ui.Muted),
            VoiceState.Speaking => (L.T("Okuyorum"), Ui.Teal),
            VoiceState.Working => (L.T("İletiyorum"), Ui.Brass),
            VoiceState.Hearing => (L.T("Duyuyorum"), Ui.Brass),
            _ => (L.T("Dinliyorum"), Ui.Brass),
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
        if (snapshot.Note is { } note)
            return note;

        var name = _controller.Config.WakeNames.FirstOrDefault() ?? "Ooshak";
        return L.T("“{0}, Atlas'a söyle testleri çalıştırsın” diye seslen.", Capitalize(name));
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + text[1..];

    private async Task InstallModelsAsync()
    {
        _downloadButton.IsEnabled = false;
        _downloadProgress.IsVisible = true;

        var progress = new Progress<ModelProgress>(p =>
        {
            _downloadProgress.Value = p.Total == 0 ? 0 : (double)p.Done / p.Total;
            _downloadStatus.Text = L.T("{0} indiriliyor · {1} / {2} MB", p.File, p.Done >> 20, p.Total >> 20);
        });

        try
        {
            await _controller.InstallModelsAsync(progress);
            _downloadStatus.Text = L.T("Modeller hazır. Başlatılıyor.");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            _downloadStatus.Text = L.T("İndirme başarısız: {0}", e.Message);
            _downloadButton.IsEnabled = true;
        }
    }

    private void ToggleHooks()
    {
        if (HookInstaller.IsInstalled())
            HookInstaller.Remove(_controller.Config.Port);
        else
            HookInstaller.Install(_controller.Config.Port);

        RefreshStatus();
    }

    private void LoadSettings()
    {
        var config = _controller.Config;
        _sessionTitles = SessionCatalog.LoadRecent().Select(s => s.Title).Distinct().ToList();

        _language.SelectedIndex = config.Language == L.English ? 1 : 0;
        _wakeNames.Text = string.Join(", ", config.WakeNames);

        var devices = new List<string> { L.T(DefaultDeviceName) };
        try
        {
            devices.AddRange(AudioListener.DeviceNames());
        }
        catch (Exception e) when (e is DllNotFoundException or InvalidOperationException or TypeInitializationException)
        {
            _controller.Log.Add(e.Message);
        }

        _microphone.ItemsSource = devices;
        _microphone.SelectedIndex = Math.Clamp(config.MicrophoneDevice + 1, 0, devices.Count - 1);

        _replyMode.SelectedIndex = (int)config.ReplyMode;
        var mediator = string.IsNullOrWhiteSpace(config.MediatorSessionTitle) ? config.GeneralSessionTitle : config.MediatorSessionTitle;
        _mediator.ItemsSource = Including(mediator);
        _mediator.SelectedItem = string.IsNullOrWhiteSpace(mediator) ? null : mediator;
        _mediatorPanel.IsVisible = config.ReplyMode == ReplyReadMode.SelectedSession;

        var none = L.T(NoGeneralSession);
        _general.ItemsSource = new[] { none }.Concat(Including(config.GeneralSessionTitle)).ToList();
        _general.SelectedItem = string.IsNullOrWhiteSpace(config.GeneralSessionTitle) ? none : config.GeneralSessionTitle;

        _rate.Value = config.SpeechRate;
        _rateValue.Text = $"{config.SpeechRate:0.00}x";
        _permissionPopup.IsChecked = config.PermissionPopup;
        _startAtLogin.IsChecked = LoginItem.Enabled;
        _saveStatus.Text = "";
    }

    private List<string> Including(string title) =>
        string.IsNullOrWhiteSpace(title) || _sessionTitles.Contains(title) ? _sessionTitles : [title, .. _sessionTitles];

    private async Task SaveAsync()
    {
        var names = (_wakeNames.Text ?? "")
            .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToArray();

        if (names.Length == 0 || names.Any(n => VoiceRouter.Key(n).Length < MinWakeNameLength))
        {
            _saveStatus.Text = L.T("Çağrı adı en az 3 harf olmalı.");
            return;
        }

        if (_replyMode.SelectedIndex == (int)ReplyReadMode.SelectedSession && _mediator.SelectedItem is null)
        {
            _saveStatus.Text = L.T("Aracı oturumu seç.");
            return;
        }

        var config = _controller.Config;
        var language = _language.SelectedIndex == 1 ? L.English : L.Turkish;
        var languageChanged = language != config.Language;
        var device = _microphone.SelectedIndex - 1;
        var needsReload = device != config.MicrophoneDevice;

        config.WakeNames = names;
        config.MicrophoneDevice = device;
        var previousMediator = config.ActiveMediator;
        config.ReplyMode = (ReplyReadMode)Math.Max(0, _replyMode.SelectedIndex);
        config.MediatorSessionTitle = _mediator.SelectedItem as string ?? "";
        if (config.ActiveMediator != previousMediator)
            _controller.Loop?.ForgetInstructions();

        var general = _general.SelectedItem as string;
        config.GeneralSessionTitle = general is not null && general != L.T(NoGeneralSession) ? general : "";
        config.SpeechRate = _rate.Value;
        config.PermissionPopup = _permissionPopup.IsChecked == true;
        config.Language = language;
        config.Save();

        try
        {
            LoginItem.Enabled = _startAtLogin.IsChecked == true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _controller.Log.Add(e.Message);
        }

        if (languageChanged)
        {
            await _controller.ChangeLanguageAsync();
            return;
        }

        _controller.Loop?.ApplyNames();

        if (needsReload)
        {
            _saveStatus.Text = L.T("Kaydedildi, mikrofon yeniden başlıyor.");
            await _controller.ReloadAsync();
        }

        _saveStatus.Text = L.T("Kaydedildi.");
    }

    private void Preview() =>
        _controller.Loop?.Preview(L.IsEnglish ? EnglishPreviewSentence : PreviewSentence, _rate.Value, _controller.Config.Pronunciations);

    private static string Format(LogEntry entry) => $"{entry.Time:HH:mm:ss}  {entry.Message}";

    private void OnLogAdded(LogEntry entry) => Dispatcher.UIThread.Post(() =>
    {
        _log.Insert(0, Format(entry));
        while (_log.Count > 500)
            _log.RemoveAt(_log.Count - 1);
    });

    private void OnLogCleared() => Dispatcher.UIThread.Post(_log.Clear);
}
