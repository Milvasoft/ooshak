using System.Text.Json;

namespace Ooshak.Core;

public sealed record SessionInfo(string LocalId, string CliSessionId, string Title, DateTimeOffset LastActivity);

/// <summary>Reads the Claude desktop app's own session records.</summary>
public static class SessionCatalog
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(14);

    public static IReadOnlyList<SessionInfo> Load()
    {
        var sessions = new List<SessionInfo>();

        foreach (var root in Roots())
        {
            foreach (var file in Directory.EnumerateFiles(root, "local_*.json", SearchOption.AllDirectories))
            {
                var session = TryRead(file);
                if (session is not null)
                    sessions.Add(session);
            }
        }

        return sessions.OrderByDescending(s => s.LastActivity).ToList();
    }

    /// <summary>Sessions active lately. Old ones would only make spoken names ambiguous.</summary>
    public static IReadOnlyList<SessionInfo> LoadRecent() =>
        Load().Where(s => s.LastActivity >= DateTimeOffset.UtcNow - RecentWindow).ToList();

    private static IEnumerable<string> Roots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataRoot = Path.Combine(local, "Claude-Data");

        if (Directory.Exists(dataRoot))
        {
            foreach (var profile in Directory.EnumerateDirectories(dataRoot))
            {
                var path = Path.Combine(profile, "claude-code-sessions");
                if (Directory.Exists(path))
                    yield return path;
            }
        }

        var roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");
        if (Directory.Exists(roaming))
            yield return roaming;
    }

    private static SessionInfo? TryRead(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;

            if (root.TryGetProperty("isArchived", out var archived) && archived.ValueKind == JsonValueKind.True)
                return null;

            var title = ClaudeWindow.Normalize(GetString(root, "title"));
            var cliId = GetString(root, "cliSessionId");
            if (title.Length == 0 || cliId.Length == 0)
                return null;

            var activity = root.TryGetProperty("lastActivityAt", out var at) && at.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : DateTimeOffset.MinValue;

            return new SessionInfo(Path.GetFileNameWithoutExtension(file), cliId, title, activity);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null; // The app may be rewriting the file; it is picked up on the next load.
        }
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
