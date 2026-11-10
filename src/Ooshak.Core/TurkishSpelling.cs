using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Ooshak.Core;

/// <summary>Finds how a Turkish voice should read an English word: first the curated terms, then CMUdict sounds.</summary>
public static partial class TurkishSpelling
{
    private const string TermsResource = "Ooshak.Core.SpeechTerms.txt";
    private const string SoundsResource = "Ooshak.Core.EnglishPronunciations.txt";
    private const string Vowels = "aeiouy";

    private static readonly Lazy<(Dictionary<string, string> Exact, Dictionary<string, string> AnyCase)> Terms = new(LoadTerms);
    private static readonly Lazy<Dictionary<string, string>> Sounds = new(LoadSounds);

    /// <summary>The spelling to read aloud, or null when the word should stay as written.</summary>
    public static string? For(string word)
    {
        // Any Turkish letter settles it, and only ASCII can match the English data below.
        if (!Ascii.IsValid(word))
            return null;

        return Known(word) ?? Parts(word);
    }

    /// <summary>Turns ARPAbet sounds into Turkish letters, using the written word to tell "docker" from "father".</summary>
    public static string FromArpabet(string word, string arpabet)
    {
        var phones = arpabet.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var vowelCount = phones.Count(IsVowel);
        var spelling = new StringBuilder();
        var position = 0;

        for (var i = 0; i < phones.Length; i++)
        {
            var phone = phones[i];
            var name = IsVowel(phone) ? phone[..^1] : phone;
            var stressed = IsVowel(phone) && phone[^1] != '0';
            var last = i == phones.Length - 1;
            var writtenO = IsVowel(phone) && NextVowelGroup(word, ref position).Contains('o');

            spelling.Append(name switch
            {
                // American English has "dakır" here, but Turkish speakers follow the written o.
                "AA" => writtenO ? "o" : "a",
                "AE" or "EH" => "e",
                "AH" when stressed || i == 0 || (last && vowelCount > 1) => "a",
                "AH" => "ı",
                "AO" or "OW" => "o",
                "AW" => "au",
                "AY" => "ay",
                "ER" => stressed ? "ör" : "ır",
                "EY" => "ey",
                "IH" or "IY" => "i",
                "OY" => "oy",
                "UH" or "UW" => "u",
                "B" => "b",
                "CH" => "ç",
                "D" or "DH" => "d",
                "F" => "f",
                "G" => "g",
                "HH" => "h",
                // Turkish words end in ç, not c, so that is how this sound is said at the end.
                "JH" => last ? "ç" : "c",
                "K" => "k",
                "L" => "l",
                "M" => "m",
                "N" => "n",
                "NG" => !last && phones[i + 1] is "K" or "G" ? "n" : "ng",
                "P" => "p",
                "R" => "r",
                "S" => "s",
                "SH" => "ş",
                "T" or "TH" => "t",
                "V" => "v",
                "W" => i > 0 && phones[i - 1] == "K" ? "u" : "v",
                "Y" => "y",
                "Z" => "z",
                "ZH" => "j",
                _ => "",
            });
        }

        return spelling.ToString();
    }

    private static string? Known(string word)
    {
        var (exact, anyCase) = Terms.Value;
        if (exact.TryGetValue(word, out var spoken) || anyCase.TryGetValue(word, out spoken))
            return spoken;

        var lower = word.ToLowerInvariant();
        return Sounds.Value.TryGetValue(lower, out var sounds) ? FromArpabet(lower, sounds) : null;
    }

    /// <summary>Reads SpeechLexicon as two words, but only when every part is known.</summary>
    private static string? Parts(string word)
    {
        var parts = Hump().Split(word);
        if (parts.Length < 2)
            return null;

        var spoken = parts.Select(Known).ToList();
        return spoken.Contains(null) ? null : string.Join(' ', spoken);
    }

    private static bool IsVowel(string phone) => char.IsDigit(phone[^1]);

    private static string NextVowelGroup(string word, ref int position)
    {
        var start = position;
        while (start < word.Length && (!Vowels.Contains(word[start]) || (word[start] == 'y' && start == 0)))
            start++;

        var end = start;
        while (end < word.Length && Vowels.Contains(word[end]))
            end++;

        position = end;
        return word[start..end];
    }

    private static (Dictionary<string, string>, Dictionary<string, string>) LoadTerms()
    {
        var exact = new Dictionary<string, string>(StringComparer.Ordinal);
        var anyCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in ReadResource(TermsResource).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (line[0] != '#' && parts.Length == 2)
                (parts[0].Any(char.IsLower) ? anyCase : exact)[parts[0]] = parts[1];
        }

        return (exact, anyCase);
    }

    private static Dictionary<string, string> LoadSounds()
    {
        var sounds = new Dictionary<string, string>(StringComparer.Ordinal);

        // Git may check the file out with CRLF endings; a stray '\r' would end up as a sound.
        foreach (var line in ReadResource(SoundsResource).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var space = line.IndexOf(' ');
            if (line[0] != ';' && space > 0)
                sounds[line[..space]] = line[(space + 1)..];
        }

        return sounds;
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])")]
    private static partial Regex Hump();
}
