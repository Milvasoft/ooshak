using System.Text.Json;

namespace Ooshak.Core;

public sealed class PermissionRequest(string session, string detail)
{
    internal readonly TaskCompletionSource<bool?> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Session { get; } = session;
    public string Detail { get; } = detail;
}

/// <summary>Holds permission questions until the user answers them, oldest first.</summary>
public sealed class PermissionBroker
{
    private readonly Lock _gate = new();
    private readonly List<PermissionRequest> _pending = [];

    public event Action? Changed;

    public PermissionRequest? Current
    {
        get
        {
            lock (_gate)
                return _pending.FirstOrDefault();
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _pending.Count;
        }
    }

    /// <returns>True to allow, false to deny, null when nobody answered in time.</returns>
    public async Task<bool?> AskAsync(string session, string detail, TimeSpan timeout)
    {
        var request = new PermissionRequest(session, detail);
        lock (_gate)
            _pending.Add(request);

        Changed?.Invoke();

        var finished = await Task.WhenAny(request.Result.Task, Task.Delay(timeout));
        if (finished == request.Result.Task)
            return await request.Result.Task;

        Finish(request, null);
        return null;
    }

    public void Decide(PermissionRequest request, bool allow) => Finish(request, allow);

    /// <summary>Hands every open question back to Claude's own dialog.</summary>
    public void Release()
    {
        List<PermissionRequest> open;
        lock (_gate)
            open = _pending.ToList();

        foreach (var request in open)
            Finish(request, null);
    }

    private void Finish(PermissionRequest request, bool? answer)
    {
        lock (_gate)
        {
            if (!_pending.Remove(request))
                return;
        }

        request.Result.TrySetResult(answer);
        Changed?.Invoke();
    }
}

/// <summary>Turns a permission hook payload into text, and a decision into the hook's reply.</summary>
public static class PermissionText
{
    private const int MaxOtherInputChars = 600;

    // These tools ask the user a question of their own, so "allow" alone would answer nothing.
    private static readonly HashSet<string> AnsweredInTheApp = ["AskUserQuestion", "ExitPlanMode"];

    public static bool NeedsTheApp(string tool) => AnsweredInTheApp.Contains(tool);

    /// <summary>What the user is approving. A command is never shortened.</summary>
    public static string Detail(string tool, JsonElement input)
    {
        if (tool == "Bash" && Text(input, "command") is { } command)
            return command;

        if ((Text(input, "file_path") ?? Text(input, "notebook_path") ?? Text(input, "url")) is { } target)
            return $"{tool} · {target}";

        var raw = input.ValueKind == JsonValueKind.Undefined ? "" : input.GetRawText();
        return $"{tool} · {(raw.Length > MaxOtherInputChars ? raw[..MaxOtherInputChars] + "…" : raw)}";
    }

    public static string Reply(bool allow, string denyMessage)
    {
        object decision = allow ? new { behavior = "allow" } : new { behavior = "deny", message = denyMessage };
        return JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PermissionRequest", decision } });
    }

    private static string? Text(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
