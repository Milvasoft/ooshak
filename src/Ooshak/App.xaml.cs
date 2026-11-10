using System.Threading;
using System.Windows;
using Ooshak.Core;

namespace Ooshak;

public partial class App : Application
{
    private const string MutexName = "Ooshak.SingleInstance";
    private const string ShowEventName = "Ooshak.ShowMainWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private AppController? _controller;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Run by the uninstaller: undo what Ooshak changed outside its own folder.
        if (e.Args.Contains("--uninstall"))
        {
            RemoveIntegration();
            Shutdown();
            return;
        }

        bool isFirst;
        try
        {
            _mutex = new Mutex(true, MutexName, out isFirst);
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        }
        catch (UnauthorizedAccessException)
        {
            // An instance running as administrator owns these handles, so one is already up.
            Shutdown();
            return;
        }

        if (!isFirst)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        var log = new LogBook();
        DispatcherUnhandledException += (_, args) =>
        {
            log.Add($"Beklenmeyen hata: {args.Exception.Message}");
            args.Handled = true;
        };

        _controller = new AppController(log);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _controller.ShowMain()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        await _controller.StartAsync(hidden: e.Args.Contains("--hidden"));
    }

    private static void RemoveIntegration()
    {
        try
        {
            if (HookInstaller.HasAny())
                HookInstaller.Remove(OoshakConfig.Load().Port);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // The settings file is the user's; leave it alone rather than fail the uninstall.
        }

        AppController.StartsWithWindows = false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        _controller?.Dispose();
        _showEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
