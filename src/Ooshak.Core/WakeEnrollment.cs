using System.Globalization;

namespace Ooshak.Core;

/// <summary>Learns a wake name from how Whisper actually hears the user say it.</summary>
public static class WakeEnrollment
{
    public const int RequiredTakes = 3;

    private const int MaxTokens = 3;
    private const int MinKeyLength = 3;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly char[] Punctuation = ['.', ',', '!', '?', ':', ';', '"', '\'', '…'];

    /// <summary>
    /// Returns the name in one short take, or null when the take is unusable. The last word is
    /// used so that "hey jarvis" yields "jarvis", which the router finds in the first few words.
    /// </summary>
    public static string? Extract(string transcript)
    {
        var tokens = transcript
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim(Punctuation))
            .Where(t => VoiceRouter.Key(t).Length > 0)
            .ToArray();

        if (tokens.Length is 0 or > MaxTokens)
            return null;

        var name = tokens[^1];
        return VoiceRouter.Key(name).Length >= MinKeyLength ? name.ToLower(Turkish) : null;
    }

    /// <summary>Keeps one spelling per distinct spoken form.</summary>
    public static string[] Variants(IEnumerable<string> names) =>
        names.DistinctBy(VoiceRouter.Key).ToArray();
}
