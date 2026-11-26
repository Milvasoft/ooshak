using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ooshak.Core;

/// <summary>Why a session is waiting, most urgent first.</summary>
public enum WaitKind
{
    Permission,
    Question,
    Waiting,
}

/// <summary>One session that needs the user: a permission, a question it asked, or a finished turn.</summary>
public sealed record WaitingSession(
    string SessionId,
    WaitKind Kind,
    string Title,
    string Detail,
    string Cwd,
    DateTimeOffset Since,
    string? LocalId,
    string Tool = "")
{
    /// <summary>The folder name of the working directory, which groups sessions by project.</summary>
    public string Project => Cwd.Length == 0 ? "" : Path.GetFileName(Cwd.TrimEnd('/', '\\'));

    /// <summary>Sessions of the desktop app can be opened there; the others ran in a terminal.</summary>
    public bool InApp => LocalId is not null;

    public string ResumeCommand => $"claude --resume {SessionId}";

    /// <summary>The open permission question, while the popup still waits for an answer.</summary>
    [JsonIgnore]
    public PermissionRequest? Request { get; init; }
}

/// <summary>
/// Keeps the sessions waiting on the user, fed by Claude Code's hooks. A newer event of the same
/// session replaces the older one, and any sign of the session working again clears it.
/// </summary>
public sealed class SessionBoard
{
    public const int MaxDetailChars = 400;

    private static readonly TimeSpan Expiry = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _gate = new();
    private readonly Dictionary<string, WaitingSession> _items = [];
    private readonly string? _path;
    private readonly Func<IReadOnlyList<SessionInfo>> _catalog;

    /// <param name="path">Where the list is kept between runs, or null to keep it in memory only.</param>
    /// <param name="catalog">The desktop app's sessions, for titles and links.</param>
    public SessionBoard(string? path, Func<IReadOnlyList<SessionInfo>>? catalog = null)
    {
        _path = path;
        _catalog = catalog ?? SessionCatalog.Load;
        Load();
    }

    public static string DefaultPath => Path.Combine(OoshakConfig.AppDataDir, "waiting.json");

    public event Action? Changed;

    /// <summary>Most urgent first, then the longest waiting.</summary>
    public IReadOnlyList<WaitingSession> Items
    {
        get
        {
            lock (_gate)
            {
                var cutoff = DateTimeOffset.UtcNow - Expiry;
                return _items.Values
                    .Where(i => i.Kind != WaitKind.Waiting || i.Since >= cutoff)
                    .OrderBy(i => i.Kind)
                    .ThenBy(i => i.Since)
                    .ToList();
            }
        }
    }

    public int Count => Items.Count;

    /// <summary>Takes in one hook event. Routes are the hook names in lower case.</summary>
    public void OnHook(string route, JsonElement payload)
    {
        var sessionId = Text(payload, "session_id");
        if (sessionId.Length == 0)
            return;

        switch (route)
        {
            case "stop":
                Put(payload, WaitKind.Waiting, ReplyExtractor.FromStopPayload(payload) ?? "");
                break;

            case "notification":
                // Idle reminders repeat what Stop already said; only an approval adds something.
                var message = Text(payload, "message");
                if (message.Contains("permission", StringComparison.OrdinalIgnoreCase) && !Has(sessionId, WaitKind.Permission))
                    Put(payload, WaitKind.Permission, message);
                break;

            // The session moved on: the user typed or the session closed.
            case "userpromptsubmit":
            case "sessionend":
                Remove(sessionId);
                break;

            // A tool ran. Parallel tools can finish while another one still waits for approval,
            // so a permission card only goes when the tool it asked about has run.
            case "posttooluse":
                RemoveIf(sessionId, item => item.Kind != WaitKind.Permission || item.Tool == Text(payload, "tool_name"));
                break;
        }
    }

    /// <summary>Records a permission request; a question tool is shown as the question it asks.</summary>
    public void OnPermission(JsonElement payload, PermissionRequest? request)
    {
        var tool = Text(payload, "tool_name");
        var input = payload.TryGetProperty("tool_input", out var value) ? value : default;

        if (tool == PermissionText.QuestionTool)
            Put(payload, WaitKind.Question, Question(input) ?? tool, tool, request);
        else if (tool == "ExitPlanMode")
            Put(payload, WaitKind.Question, L.T("Planı onaylamanı bekliyor."), tool);
        else
            Put(payload, WaitKind.Permission, PermissionText.Detail(tool, input), tool, request);
    }

    /// <summary>
    /// The popup's question ended. An answer means the session goes on, so the card goes; without
    /// one the question moved to Claude's own dialog and the card stays, without buttons.
    /// </summary>
    public void Answered(string sessionId, bool? answer)
    {
        if (answer is not null)
        {
            RemoveIf(sessionId, item => item.Kind is WaitKind.Permission || item.Request is not null);
            return;
        }

        lock (_gate)
        {
            if (!_items.TryGetValue(sessionId, out var item) || item.Request is null)
                return;
            _items[sessionId] = item with { Request = null };
        }

        Changed?.Invoke();
    }

    public void Dismiss(string sessionId) => Remove(sessionId);

    private bool Has(string sessionId, WaitKind kind)
    {
        lock (_gate)
            return _items.TryGetValue(sessionId, out var item) && item.Kind == kind;
    }

    private void Put(JsonElement payload, WaitKind kind, string detail, string tool = "", PermissionRequest? request = null)
    {
        var sessionId = Text(payload, "session_id");
        var cwd = Text(payload, "cwd");
        var session = _catalog().FirstOrDefault(s => s.CliSessionId == sessionId);
        var title = session?.Title ?? TitleFromTranscript(Text(payload, "transcript_path")) ?? Short(sessionId);

        var item = new WaitingSession(sessionId, kind, title, Trim(detail), cwd, DateTimeOffset.UtcNow, session?.LocalId, tool)
        {
            Request = request,
        };

        lock (_gate)
            _items[sessionId] = item;

        Save();
        Changed?.Invoke();
    }

    private void Remove(string sessionId) => RemoveIf(sessionId, _ => true);

    private void RemoveIf(string sessionId, Func<WaitingSession, bool> condition)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue(sessionId, out var item) || !condition(item))
                return;
            _items.Remove(sessionId);
        }

        Save();
        Changed?.Invoke();
    }

    private static string? Question(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("questions", out var questions)
            || questions.ValueKind != JsonValueKind.Array)
            return null;

        return questions.EnumerateArray()
            .Select(q => q.ValueKind == JsonValueKind.Object ? Text(q, "question") : "")
            .FirstOrDefault(q => q.Length > 0);
    }

    /// <summary>A terminal session has no title, so its first prompt stands in for one.</summary>
    public static string? TitleFromTranscript(string path)
    {
        const int MaxLines = 200;
        const int MaxTitleChars = 60;

        if (path.Length == 0 || !File.Exists(path))
            return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            for (var i = 0; i < MaxLines && reader.ReadLine() is { } line; i++)
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (Text(root, "type") != "user" || !root.TryGetProperty("message", out var message)
                    || !message.TryGetProperty("content", out var content))
                    continue;

                var text = content.ValueKind == JsonValueKind.String
                    ? content.GetString()
                    : content.ValueKind == JsonValueKind.Array
                        ? content.EnumerateArray().Select(c => Text(c, "text")).FirstOrDefault(t => t.Length > 0)
                        : null;

                text = ClaudeText.Normalize(text);
                if (text.Length == 0 || text.StartsWith('<'))
                    continue;

                return text.Length > MaxTitleChars ? text[..MaxTitleChars].TrimEnd() + "…" : text;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A transcript being written can be cut mid-line; the id is used instead.
        }

        return null;
    }

    private static string Short(string sessionId) => sessionId.Length > 8 ? sessionId[..8] : sessionId;

    private static string Trim(string text)
    {
        text = text.Trim();
        return text.Length > MaxDetailChars ? text[..MaxDetailChars].TrimEnd() + "…" : text;
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
            return;

        try
        {
            var items = JsonSerializer.Deserialize<List<WaitingSession>>(File.ReadAllText(_path), JsonOptions) ?? [];

            // A permission question cannot outlive a restart: the hook behind it has given up.
            foreach (var item in items.Where(i => i.Kind != WaitKind.Permission))
                _items[item.SessionId] = item;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged file only loses the list; the next hook rebuilds it.
        }
    }

    private void Save()
    {
        if (_path is null)
            return;

        try
        {
            List<WaitingSession> items;
            lock (_gate)
                items = _items.Values.ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(items, JsonOptions));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Keeping the list on disk is a convenience; the board works without it.
        }
    }
}
