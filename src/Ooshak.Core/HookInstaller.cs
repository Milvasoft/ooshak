using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ooshak.Core;

/// <summary>Adds or removes Ooshak's hooks in the user's Claude Code settings, keeping a backup.</summary>
public static class HookInstaller
{
    private const string Marker = "X-Ooshak";
    private const string PermissionEvent = "PermissionRequest";
    private const int PermissionWaitSeconds = 60;
    private const int PermissionHookTimeoutSeconds = 65;
    private static readonly string[] Events = ["Stop", "Notification", PermissionEvent];

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static string Command(int port, string route) =>
        $"curl.exe -s -m 2 -X POST -H \"{Marker}: 1\" --data-binary @- http://localhost:{port}/{route}";

    // Waits for the answer and prints it. "exit 0" works in Git Bash and PowerShell alike, and keeps
    // an unreachable Ooshak from counting as a failed hook, so Claude's own dialog simply appears.
    public static string PermissionCommand(int port) =>
        $"curl.exe -s -m {PermissionWaitSeconds} -X POST -H \"{Marker}: 1\" --data-binary \"@-\" http://localhost:{port}/{HookServer.PermissionRoute}; exit 0";

    public static bool IsInstalled() =>
        File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Contains($"/{HookServer.PermissionRoute}", StringComparison.Ordinal);

    /// <summary>True when any Ooshak hook, from this or an older version, is in the settings.</summary>
    public static bool HasAny() =>
        File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Contains(Marker, StringComparison.Ordinal);

    public static string Install(int port) => Apply(port, install: true);

    public static string Remove(int port) => Apply(port, install: false);

    private static string Apply(int port, bool install)
    {
        var path = SettingsPath;
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var hooks = root["hooks"]?.AsObject() ?? [];

        foreach (var name in Events)
        {
            var groups = hooks[name]?.AsArray() ?? [];
            RemoveOurs(groups);

            if (install)
            {
                groups.Add(new JsonObject { ["hooks"] = new JsonArray(Hook(port, name)) });
            }

            hooks.Remove(name);
            if (groups.Count > 0)
                hooks[name] = groups;
        }

        root.Remove("hooks");
        if (hooks.Count > 0)
            root["hooks"] = hooks;

        var backup = $"{path}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Copy(path, backup);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return backup;
    }

    // Only the permission hook blocks: Claude has to wait for its answer.
    private static JsonObject Hook(int port, string name) => name == PermissionEvent
        ? new JsonObject
        {
            ["type"] = "command",
            ["command"] = PermissionCommand(port),
            ["timeout"] = PermissionHookTimeoutSeconds,
        }
        : new JsonObject
        {
            ["type"] = "command",
            ["command"] = Command(port, name.ToLowerInvariant()),
            ["timeout"] = 5,
            ["async"] = true,
        };

    private static void RemoveOurs(JsonArray groups)
    {
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var isOurs = groups[i]?["hooks"]?.AsArray()
                .Any(h => (h?["command"]?.GetValue<string>() ?? "").Contains(Marker, StringComparison.Ordinal)) ?? false;

            if (isOurs)
                groups.RemoveAt(i);
        }
    }
}
