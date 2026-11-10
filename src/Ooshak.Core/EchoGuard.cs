namespace Ooshak.Core;

/// <summary>Recognizes a transcript that is the app's own voice picked up by the microphone.</summary>
public static class EchoGuard
{
    private const double MatchingShare = 0.6;
    private const int MinTokens = 2;

    public static bool IsEcho(string transcript, string? spoken)
    {
        if (string.IsNullOrWhiteSpace(spoken))
            return false;

        var heard = Tokens(transcript);
        if (heard.Count < MinTokens)
            return false;

        var said = Tokens(spoken).ToHashSet();
        return heard.Count(said.Contains) >= heard.Count * MatchingShare;
    }

    private static List<string> Tokens(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(VoiceRouter.Key)
            .Where(k => k.Length > 0)
            .ToList();
}
