using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ooshak.Core;

/// <summary>Picks the short text worth reading aloud from a finished Claude Code turn.</summary>
public static partial class ReplyExtractor
{
    public const string SpokenMarker = "🔊";

    private const int MaxSpokenChars = 320;
    private const int TranscriptTailBytes = 4 * 1024 * 1024;

    public static string? FromStopPayload(JsonElement payload)
    {
        var last = payload.TryGetProperty("last_assistant_message", out var m) ? m.GetString() : null;
        if (SpokenLine(last) is { } spoken)
            return Clean(spoken);

        var path = payload.TryGetProperty("transcript_path", out var p) ? p.GetString() : null;
        var recap = path is not null && File.Exists(path) ? RecapFromTranscript(path) : null;
        if (!string.IsNullOrWhiteSpace(recap))
            return Clean(recap);

        return string.IsNullOrWhiteSpace(last) ? null : Clean(last);
    }

    /// <summary>Returns the text after the last spoken-reply marker, which sessions write for voice messages.</summary>
    public static string? SpokenLine(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return null;

        // Only the start of a line counts: a reply may mention the marker in a sentence.
        foreach (var raw in message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            var line = raw.TrimStart(' ', '\t', '*', '_', '>', '-');
            if (!line.StartsWith(SpokenMarker, StringComparison.Ordinal))
                continue;

            var spoken = line[SpokenMarker.Length..].Trim(' ', '\t', '*', '_');
            return spoken.Length > 0 ? spoken : null;
        }

        return null;
    }

    /// <summary>Returns the wrap-up recap of the latest turn, or null when that turn has none.</summary>
    public static string? RecapFromTranscript(string path)
    {
        foreach (var line in TailLines(path).Reverse())
        {
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (!root.TryGetProperty("message", out var message))
                    continue;

                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                var content = message.TryGetProperty("content", out var c) ? c : default;

                if (type == "user" && IsRealUserMessage(content))
                    return null;

                if (type == "assistant" && FindRecap(content) is { } recap)
                    return recap;
            }
            catch (JsonException)
            {
                // The tail can start mid-line, and the line being written can be incomplete.
            }
        }

        return null;
    }

    private static bool IsRealUserMessage(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return true;

        return content.ValueKind == JsonValueKind.Array && content.EnumerateArray()
            .All(b => !b.TryGetProperty("type", out var type) || type.GetString() != "tool_result");
    }

    private static string? FindRecap(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "tool_use"
                && block.TryGetProperty("name", out var name) && (name.GetString() ?? "").EndsWith("wrap_up", StringComparison.Ordinal)
                && block.TryGetProperty("input", out var input) && input.TryGetProperty("recap", out var recap))
            {
                return recap.GetString();
            }
        }

        return null;
    }

    private static IEnumerable<string> TailLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(Math.Max(0, stream.Length - TranscriptTailBytes), SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Removes what cannot be spoken and cuts at a sentence boundary.</summary>
    public static string Clean(string text)
    {
        var result = CodeBlock().Replace(text, " ");
        result = Link().Replace(result, "$1");
        result = Url().Replace(result, " ");
        result = Markup().Replace(result, "");
        result = Space().Replace(result, " ").Trim();

        if (result.Length <= MaxSpokenChars)
            return result;

        var cut = result.LastIndexOfAny(['.', '!', '?'], MaxSpokenChars - 1);
        return cut > 0 ? result[..(cut + 1)] : result[..MaxSpokenChars];
    }

    [GeneratedRegex("```.*?```", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Url();

    [GeneratedRegex(@"[*_`#>|]")]
    private static partial Regex Markup();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}
