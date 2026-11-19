using Avalonia;
using Ooshak.Core;

namespace Ooshak.Mac;

public static class Program
{
    private static FileStream? _lock;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test"))
            return SelfTest.RunAsync().GetAwaiter().GetResult();

        // A second copy started from Finder or at login quits; the first one keeps running in the menu bar.
        if (!TryLock())
            return 0;

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();

    private static bool TryLock()
    {
        Directory.CreateDirectory(OoshakConfig.AppDataDir);
        try
        {
            _lock = new FileStream(Path.Combine(OoshakConfig.AppDataDir, "ooshak.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
