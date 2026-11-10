using System.Text.RegularExpressions;

namespace Ooshak.Core;

/// <summary>Rewrites words into the spelling a Turkish voice reads correctly, such as code to kod.</summary>
public static partial class SpeechLexicon
{
    private const string Separator = "=";

    /// <summary>The user's own words win, then the built-in terms, then the general English rule.</summary>
    public static string Apply(string text, IReadOnlyDictionary<string, string> pronunciations)
    {
        var lookup = new Dictionary<string, string>(pronunciations, StringComparer.OrdinalIgnoreCase);

        return Word().Replace(text, match =>
        {
            if (IsSuffix(text, match.Index))
                return match.Value;

            return lookup.TryGetValue(match.Value, out var spoken) ? spoken : TurkishSpelling.For(match.Value) ?? match.Value;
        });
    }

    // A word ends at an apostrophe, so the "un" in "code'un" must not be read as English.
    // An apostrophe with nothing before it is a quote, as in 'code'.
    private static bool IsSuffix(string text, int index) =>
        index > 1 && text[index - 1] is '\'' or '’' && char.IsLetterOrDigit(text[index - 2]);

    public static string Format(IReadOnlyDictionary<string, string> pronunciations) =>
        string.Join(Environment.NewLine, pronunciations.Select(p => $"{p.Key} {Separator} {p.Value}"));

    /// <summary>Reads "word = spoken" lines and ignores anything else.</summary>
    public static Dictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(Separator, 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
                result[parts[0]] = parts[1];
        }

        return result;
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}
