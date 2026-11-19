using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Ooshak.Core;

namespace Ooshak.Mac;

public sealed class App : Application
{
    private MacController? _controller;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
        Name = "Ooshak";
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var log = new LogBook();
            AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Add($"Beklenmeyen hata: {e.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                log.Add($"Beklenmeyen hata: {e.Exception.Message}");
                e.SetObserved();
            };

            _controller = new MacController(this, desktop, log);
            desktop.Exit += (_, _) => _controller.Dispose();
            _ = _controller.StartAsync(hidden: desktop.Args?.Contains("--hidden") == true);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
