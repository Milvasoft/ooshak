using System.Security;

namespace Ooshak.Mac;

/// <summary>Starts Ooshak at login through a per-user LaunchAgent.</summary>
public static class LoginItem
{
    private const string Label = "com.milvasoft.ooshak";

    public static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");

    public static bool Enabled
    {
        get => File.Exists(PlistPath);
        set
        {
            if (!value)
            {
                File.Delete(PlistPath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
            File.WriteAllText(PlistPath, Plist(Environment.ProcessPath ?? "/Applications/Ooshak.app/Contents/MacOS/Ooshak"));
        }
    }

    public static string Plist(string executable) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key>
          <string>{Label}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{SecurityElement.Escape(executable)}</string>
            <string>--hidden</string>
          </array>
          <key>RunAtLoad</key>
          <true/>
          <key>ProcessType</key>
          <string>Interactive</string>
        </dict>
        </plist>

        """;
}
