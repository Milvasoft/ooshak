namespace Ooshak.Core;

/// <summary>
/// Understands a spoken answer to an open permission request or question: yes or no, an option
/// by its label, or an option by its position ("the second one", "ikincisi").
/// </summary>
public static class VoiceDecisions
{
    private const int MaxDecisionTokens = 4;

    // Only words that answer a question. "izin" or "çalıştır" alone also appear in Ooshak's own
    // announcement, so an echo of it must never count as a yes.
    private static readonly HashSet<string> TurkishYes = ["evet", "onayla", "onayliyorum", "onaylarim", "tamam", "olur", "kabul"];
    private static readonly HashSet<string> TurkishNo = ["hayir", "reddet", "olmaz", "iptal", "yapma", "durdur", "vazgec"];
    private static readonly HashSet<string> EnglishYes = ["yes", "yeah", "yep", "approve", "approved", "allow", "ok", "okay", "sure"];
    private static readonly HashSet<string> EnglishNo = ["no", "nope", "deny", "reject", "cancel", "dont", "stop"];

    private static readonly string[][] TurkishOrdinals =
    [
        ["birinci", "birincisi", "ilk", "ilki", "bir"],
        ["ikinci", "ikincisi", "iki"],
        ["ucuncu", "ucuncusu", "uc"],
        ["dorduncu", "dorduncusu", "dort"],
    ];

    private static readonly string[][] EnglishOrdinals =
    [
        ["first", "one"],
        ["second", "two"],
        ["third", "three"],
        ["fourth", "four"],
    ];

    /// <summary>True to allow, false to deny, null when the sentence is not a short yes or no.</summary>
    public static bool? Approval(string text, IEnumerable<string> wakeNames, bool english)
    {
        var keys = Keys(text, wakeNames);
        if (keys.Length is 0 or > MaxDecisionTokens)
            return null;

        var no = keys.Any((english ? EnglishNo : TurkishNo).Contains);
        var yes = keys.Any((english ? EnglishYes : TurkishYes).Contains)
            || (!english && string.Join(' ', keys).Contains("izin ver", StringComparison.Ordinal));

        // "izin verme" is a no even though it holds "izin".
        if (!english && keys.Any(k => k is "verme" or "calistirma" or "yapma"))
            return false;

        return no ? false : yes ? true : null;
    }

    /// <summary>The option the sentence names, by label or by position, or null.</summary>
    public static string? Option(string text, AskedQuestion question, IEnumerable<string> wakeNames, bool english)
    {
        var keys = Keys(text, wakeNames);
        if (keys.Length == 0)
            return null;

        var spoken = string.Join(' ', keys);
        var byLabel = question.Options
            .Select(o => (o.Label, Key: string.Join(' ', Keys(o.Label, []))))
            .Where(o => o.Key.Length > 0 && (spoken == o.Key || $" {spoken} ".Contains($" {o.Key} ", StringComparison.Ordinal)))
            .OrderByDescending(o => o.Key.Length)
            .Select(o => o.Label)
            .FirstOrDefault();
        if (byLabel is not null)
            return byLabel;

        var ordinals = english ? EnglishOrdinals : TurkishOrdinals;
        for (var i = 0; i < Math.Min(ordinals.Length, question.Options.Count); i++)
        {
            if (keys.Length <= MaxDecisionTokens && keys.Any(ordinals[i].Contains))
                return question.Options[i].Label;
        }

        return null;
    }

    private static string[] Keys(string text, IEnumerable<string> wakeNames)
    {
        var wake = wakeNames.Select(VoiceRouter.Key).ToHashSet();
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(VoiceRouter.Key)
            .Where(k => k.Length > 0 && !wake.Contains(k))
            .ToArray();
    }
}
