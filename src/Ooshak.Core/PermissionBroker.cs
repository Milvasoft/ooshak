using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ooshak.Core;

public sealed record QuestionOption(string Label, string Description);

/// <summary>One question of an AskUserQuestion call, with the options Claude offers.</summary>
public sealed record AskedQuestion(string Question, string Header, IReadOnlyList<QuestionOption> Options, bool MultiSelect);

/// <param name="questions">Empty for a permission; the questions to answer for AskUserQuestion.</param>
public sealed class PermissionRequest(string session, string detail, IReadOnlyList<AskedQuestion>? questions = null)
{
    internal readonly TaskCompletionSource<bool?> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Session { get; } = session;
    public string Detail { get; } = detail;
    public IReadOnlyList<AskedQuestion> Questions { get; } = questions ?? [];
    public bool IsQuestion => Questions.Count > 0;

    /// <summary>Question text to the chosen labels, set when the user answered the questions.</summary>
    public IReadOnlyDictionary<string, string>? Answers { get; internal set; }
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
    /// <param name="created">Gets the request before it is shown, so other views can answer it too.</param>
    public async Task<bool?> AskAsync(string session, string detail, TimeSpan timeout, Action<PermissionRequest>? created = null,
        IReadOnlyList<AskedQuestion>? questions = null)
    {
        var request = new PermissionRequest(session, detail, questions);
        created?.Invoke(request);
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

    /// <summary>Answers a question request; it counts as allowed, carrying the chosen labels.</summary>
    public void AnswerQuestions(PermissionRequest request, IReadOnlyDictionary<string, string> answers)
    {
        request.Answers = answers;
        Finish(request, true);
    }

    /// <summary>Gives one question back to Claude's own dialog without answering it.</summary>
    public void HandOff(PermissionRequest request) => Finish(request, null);

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

    public const string QuestionTool = "AskUserQuestion";

    // These tools ask the user a question of their own, so "allow" alone would answer nothing.
    // AskUserQuestion is answered through Questions and AnswerReply instead.
    private static readonly HashSet<string> AnsweredInTheApp = [QuestionTool, "ExitPlanMode"];

    public static bool NeedsTheApp(string tool) => AnsweredInTheApp.Contains(tool);

    /// <summary>The questions of an AskUserQuestion call, or empty when any of them offers no options.</summary>
    public static IReadOnlyList<AskedQuestion> Questions(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("questions", out var list) || list.ValueKind != JsonValueKind.Array)
            return [];

        var questions = new List<AskedQuestion>();
        foreach (var item in list.EnumerateArray())
        {
            var options = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("options", out var raw) && raw.ValueKind == JsonValueKind.Array
                ? raw.EnumerateArray()
                    .Select(o => new QuestionOption(Text(o, "label") ?? "", Text(o, "description") ?? ""))
                    .Where(o => o.Label.Length > 0)
                    .ToList()
                : [];

            var question = Text(item, "question") ?? "";
            if (question.Length == 0 || options.Count == 0)
                return [];

            var multi = item.TryGetProperty("multiSelect", out var m) && m.ValueKind == JsonValueKind.True;
            questions.Add(new AskedQuestion(question, Text(item, "header") ?? "", options, multi));
        }

        return questions;
    }

    /// <summary>
    /// Allows the question tool with the answers filled in. The whole input goes back, because the
    /// hook's updatedInput replaces it; answers map question text to labels, joined by commas.
    /// </summary>
    public static string AnswerReply(JsonElement input, IReadOnlyDictionary<string, string> answers)
    {
        var updated = JsonNode.Parse(input.GetRawText())!.AsObject();
        var map = new JsonObject();
        foreach (var (question, answer) in answers)
            map[question] = answer;
        updated["answers"] = map;

        var reply = new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PermissionRequest",
                ["decision"] = new JsonObject { ["behavior"] = "allow", ["updatedInput"] = updated },
            },
        };

        return reply.ToJsonString();
    }

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
