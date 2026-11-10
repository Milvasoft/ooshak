using System.Globalization;
using System.Text;

namespace Ooshak.Core;

public abstract record RouteResult
{
    public sealed record Ignore : RouteResult;
    public sealed record StopSpeaking : RouteResult;
    public sealed record PauseSpeaking : RouteResult;
    public sealed record ResumeSpeaking : RouteResult;
    public sealed record WhereAmI(string? Title) : RouteResult;
    public sealed record NeedTarget : RouteResult;
    public sealed record SwitchTo(string Title) : RouteResult;
    public sealed record Send(string Title, string Text) : RouteResult;
    public sealed record SetAddress(string Name) : RouteResult;
}

/// <summary>Turns one transcribed sentence into an action. Holds the sticky target session.</summary>
public sealed class VoiceRouter
{
    private const int WakeSearchTokens = 3;
    private const int MaxStopTokens = 2;
    private const int MinAliasLength = 3;
    private const int MaxAddressTokens = 3;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly HashSet<string> Suffixes =
        ["a", "e", "ya", "ye", "na", "ne", "ta", "te", "da", "de", "i", "u", "yi", "yu", "nin", "nun", "in", "un", "la", "le", "yla", "yle"];
    private static readonly HashSet<string> SwitchVerbs = ["gec", "gecelim", "baglan", "bagla", "don", "switch"];
    private static readonly HashSet<string> SendVerbs = ["soyle", "yaz", "sor", "ilet", "de", "tell", "send", "ask"];
    private static readonly HashSet<string> StopWords = ["sus", "yeter", "kes", "stop"];
    private static readonly HashSet<string> PauseWords = ["dur", "bekle"];
    private static readonly HashSet<string> ResumeWords = ["devam", "surdur", "oku"];
    private static readonly HashSet<string> EnglishPauseWords = ["pause", "wait", "hold"];
    private static readonly HashSet<string> EnglishResumeWords = ["continue", "resume", "go"];
    private static readonly HashSet<string> WhereWords = ["neredesin", "neredeyiz", "hangisindesin"];
    private static readonly HashSet<string> NotAddressWords = ["merhaba", "selam", "gunaydin", "tamam", "evet", "hayir", "naber", "back", "later", "when", "if", "after", "maybe"];
    private static readonly HashSet<string> EnglishStopWords = ["stop", "quiet", "enough", "silence", "shush", "shut"];
    private static readonly HashSet<string> EnglishWherePhrases = ["where are you", "which session", "which session are you in", "what session are you in"];
    private static readonly HashSet<string> EnglishSendVerbs = ["tell", "ask", "send", "message"];
    private static readonly HashSet<string> EnglishSendFillers = ["to", "that", "please"];
    private static readonly HashSet<string> EnglishSwitchVerbs = ["switch", "go", "move", "back"];
    private static readonly HashSet<string> EnglishSwitchFillers = ["to", "back", "over", "the"];

    private HashSet<string> _wakeKeys;
    private readonly TimeSpan _conversationWindow;
    private readonly Func<DateTimeOffset> _clock;
    private List<(string[] Words, string Title)> _aliases = [];
    private DateTimeOffset _conversationUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _lastTargetAt = DateTimeOffset.MinValue;
    private bool _english;
    private string? _mediatorTitle;
    private string? _generalTitle;
    private TimeSpan _stickyWindow;

    public VoiceRouter(IEnumerable<string> wakeNames, TimeSpan conversationWindow, Func<DateTimeOffset>? clock = null)
    {
        _wakeKeys = wakeNames.Select(Key).ToHashSet();
        _conversationWindow = conversationWindow;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string? CurrentTitle { get; private set; }

    public void SetWakeNames(IEnumerable<string> wakeNames) => _wakeKeys = wakeNames.Select(Key).ToHashSet();

    public void SetEnglish(bool english) => _english = english;

    /// <summary>When set, everything said after the wake name goes to this one session, word for word.</summary>
    public void SetMediator(string? title) => _mediatorTitle = string.IsNullOrWhiteSpace(title) ? null : title;

    /// <summary>Sets where unnamed speech goes once the last session has gone quiet for the given time.</summary>
    public void SetGeneral(string? title, TimeSpan stickyWindow)
    {
        _generalTitle = string.IsNullOrWhiteSpace(title) ? null : title;
        _stickyWindow = stickyWindow;
    }

    /// <summary>Lets the next sentence reach the current session without the wake name.</summary>
    public void OpenConversation(TimeSpan window)
    {
        if (CurrentTitle is not null)
            _conversationUntil = _clock() + window;
    }

    // Whisper invents these closing lines from background noise, mostly on Turkish audio.
    private static bool IsNoise(string[] keys)
    {
        var text = string.Join(' ', keys);
        return text.Contains("altyazi", StringComparison.Ordinal)
            || text.Contains("izlediginiz icin", StringComparison.Ordinal)
            || text.Contains("abone ol", StringComparison.Ordinal);
    }

    /// <summary>Builds spoken aliases. Explicit ones win, and a derived alias two sessions share is dropped.</summary>
    public void SetTargets(IEnumerable<string> titles, IReadOnlyDictionary<string, string> explicitAliases)
    {
        var claims = new Dictionary<string, HashSet<string>>();

        foreach (var title in titles)
        {
            var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var candidates = new[] { title, words.FirstOrDefault() ?? "", string.Join(' ', words.Take(2)) };

            foreach (var candidate in candidates.Where(c => Key(c.Replace(" ", "")).Length >= MinAliasLength))
            {
                var key = KeyPhrase(candidate);
                if (!claims.TryGetValue(key, out var owners))
                    claims[key] = owners = [];
                owners.Add(title);
            }
        }

        var table = claims.Where(c => c.Value.Count == 1).ToDictionary(c => c.Key, c => c.Value.First());

        foreach (var (alias, title) in explicitAliases)
            table[KeyPhrase(alias)] = title;

        _aliases = table
            .Select(e => (Words: e.Key.Split(' '), Title: e.Value))
            .OrderByDescending(e => e.Words.Length)
            .ThenByDescending(e => e.Words.Sum(w => w.Length))
            .ToList();
    }

    /// <summary>
    /// Finds a stop or pause command anywhere in audio captured during playback, where the
    /// app's own voice surrounds it. Returns null when there is none.
    /// </summary>
    public RouteResult? FindCommandDuringSpeech(string transcript)
    {
        var tokens = transcript.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var keys = tokens.Select(Key).ToArray();

        for (var i = 0; i < keys.Length - 1; i++)
        {
            if (!_wakeKeys.Contains(keys[i]))
                continue;

            if ((_english ? EnglishStopWords : StopWords).Contains(keys[i + 1]))
                return new RouteResult.StopSpeaking();

            if ((_english ? EnglishPauseWords : PauseWords).Contains(keys[i + 1]))
                return new RouteResult.PauseSpeaking();
        }

        return null;
    }

    public RouteResult Route(string transcript, bool speaking = false, bool paused = false)
    {
        var tokens = transcript.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var keys = tokens.Select(Key).ToArray();
        var wakeIndex = Array.FindIndex(keys.Take(WakeSearchTokens).ToArray(), _wakeKeys.Contains);

        if (wakeIndex < 0)
        {
            if (speaking || CurrentTitle is null || _clock() >= _conversationUntil || tokens.Length == 0 || IsNoise(keys))
                return new RouteResult.Ignore();

            return Deliver(CurrentTitle, tokens, keys, 0);
        }

        var start = wakeIndex + 1;
        var remaining = keys.Length - start;

        if (remaining is > 0 and <= MaxStopTokens && (_english ? EnglishStopWords : StopWords).Contains(keys[start]))
            return new RouteResult.StopSpeaking();

        if (remaining is > 0 and <= MaxStopTokens && (_english ? EnglishPauseWords : PauseWords).Contains(keys[start]))
            return new RouteResult.PauseSpeaking();

        // Only while something is paused: otherwise "devam et" is a message for the session.
        if (paused && remaining is > 0 and <= MaxStopTokens && (_english ? EnglishResumeWords : ResumeWords).Contains(keys[start]))
            return new RouteResult.ResumeSpeaking();

        // Claude's own voice reaches the microphone, so only a stop command counts while it speaks.
        if (speaking || remaining == 0)
            return new RouteResult.Ignore();

        if (_english ? EnglishWherePhrases.Contains(string.Join(' ', keys[start..])) : remaining <= MaxStopTokens && WhereWords.Contains(keys[start]))
            return new RouteResult.WhereAmI(CurrentTitle);

        if ((_english ? EnglishAddressName(tokens, keys, start) : TurkishAddressName(tokens, keys, start)) is { } address)
            return new RouteResult.SetAddress(address);

        // The go-between decides what a sentence means, including which session it is about.
        if (_mediatorTitle is not null)
            return Deliver(_mediatorTitle, tokens, keys, start);

        if (_english && RouteEnglishVerbFirst(tokens, keys, start) is { } englishResult)
            return englishResult;

        var (title, consumed) = MatchAlias(keys, start);
        if (title is null)
            return UnnamedTarget() is { } target ? Deliver(target, tokens, keys, start) : new RouteResult.NeedTarget();

        var next = start + consumed;
        if (next >= keys.Length || (SwitchVerbs.Contains(keys[next]) && keys.Length - next <= MaxStopTokens))
        {
            CurrentTitle = title;
            _lastTargetAt = _clock();
            _conversationUntil = _clock() + _conversationWindow;
            return new RouteResult.SwitchTo(title);
        }

        if (!_english && SendVerbs.Contains(keys[next]) && next + 1 < keys.Length)
            next++;

        return Deliver(title, tokens, keys, next);
    }

    private RouteResult Deliver(string title, string[] tokens, string[] keys, int from)
    {
        var last = keys.Length;
        if (!_english && _mediatorTitle is null && last - from > 1 && SendVerbs.Contains(keys[last - 1]))
            last--;

        var text = string.Join(' ', tokens[from..last]).TrimStart(',', ':', ';', ' ').TrimEnd(',', ':', ';', ' ');
        CurrentTitle = title;
        _lastTargetAt = _clock();
        _conversationUntil = _clock() + _conversationWindow;
        return new RouteResult.Send(title, text);
    }

    // English puts the verb before the session: "tell Atlas to run the tests", "switch to Atlas".
    private RouteResult? RouteEnglishVerbFirst(string[] tokens, string[] keys, int start)
    {
        if (EnglishSendVerbs.Contains(keys[start]))
        {
            var (title, consumed) = MatchAlias(keys, start + 1);
            if (title is null)
                return null;

            var next = start + 1 + consumed;
            while (next < keys.Length && EnglishSendFillers.Contains(keys[next]))
                next++;

            return next >= keys.Length ? Switch(title) : Deliver(title, tokens, keys, next);
        }

        if (!EnglishSwitchVerbs.Contains(keys[start]))
            return null;

        var index = start + 1;
        while (index < keys.Length && EnglishSwitchFillers.Contains(keys[index]))
            index++;

        var (target, used) = MatchAlias(keys, index);
        var isOnlySession = index + used == keys.Length || (index + used + 1 == keys.Length && keys[^1] == "session");
        return target is not null && isOnlySession ? Switch(target) : null;
    }

    /// <summary>Takes the name out of a whole sentence typed into the settings, and leaves a plain name as it is.</summary>
    public static string NormalizeAddress(string typed)
    {
        var tokens = typed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return typed;

        var keys = tokens.Select(Key).ToArray();
        return TurkishAddressName(tokens, keys, 0) ?? EnglishAddressName(tokens, keys, 0) ?? typed;
    }

    // "bana efendim de", "bana Deniz diye hitap et", "bana patron diye seslen".
    private static string? TurkishAddressName(string[] tokens, string[] keys, int start)
    {
        if (keys[start] != "bana" || keys.Length - start < 3)
            return null;

        var bareDe = keys[^1] == "de";
        var end = keys[^1] == "seslen" || bareDe ? keys.Length - 1
            : keys[^2] == "hitap" && keys[^1] == "et" ? keys.Length - 2
            : -1;
        if (end < 0)
            return null;

        var named = keys[end - 1] == "diye";
        if (named)
            end--;

        // Without "diye", "bana bir film oner de" and "bana merhaba de" are requests, not names.
        if (bareDe && !named && (end - start != 2 || NotAddressWords.Contains(keys[start + 1])))
            return null;

        return AddressFrom(tokens, start + 1, end);
    }

    // "call me boss", "address me as sir".
    private static string? EnglishAddressName(string[] tokens, string[] keys, int start)
    {
        if (keys.Length - start >= 3 && keys[start] == "call" && keys[start + 1] == "me" && !NotAddressWords.Contains(keys[start + 2]))
            return AddressFrom(tokens, start + 2, keys.Length);

        return keys.Length - start >= 4 && keys[start] == "address" && keys[start + 1] == "me" && keys[start + 2] == "as"
            ? AddressFrom(tokens, start + 3, keys.Length)
            : null;
    }

    private static string? AddressFrom(string[] tokens, int from, int to)
    {
        if (to - from is < 1 or > MaxAddressTokens)
            return null;

        var name = string.Join(' ', tokens[from..to]).Trim('.', ',', '!', '?', ':', ';', '"', '“', '”');
        return name.Length > 0 ? name : null;
    }

    private RouteResult Switch(string title)
    {
        CurrentTitle = title;
        _lastTargetAt = _clock();
        _conversationUntil = _clock() + _conversationWindow;
        return new RouteResult.SwitchTo(title);
    }

    // Without a general session the last target stays for good; with one, it only stays while recent.
    private string? UnnamedTarget()
    {
        if (_generalTitle is null || (CurrentTitle is not null && _clock() - _lastTargetAt < _stickyWindow))
            return CurrentTitle;

        return _generalTitle;
    }

    private (string? Title, int Consumed) MatchAlias(string[] keys, int start)
    {
        foreach (var (words, title) in _aliases)
        {
            if (start + words.Length > keys.Length)
                continue;

            var exact = words.Take(words.Length - 1).Select((w, i) => keys[start + i] == w).All(m => m);
            if (exact && MatchesWithSuffix(keys[start + words.Length - 1], words[^1]))
                return (title, words.Length);
        }

        return (null, 0);
    }

    private static bool MatchesWithSuffix(string token, string word) =>
        token == word || (token.StartsWith(word, StringComparison.Ordinal) && Suffixes.Contains(token[word.Length..]));

    private static string KeyPhrase(string text) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Key).Where(k => k.Length > 0));

    /// <summary>Lowercases with Turkish rules, drops punctuation and folds to ASCII so spellings compare equal.</summary>
    public static string Key(string token)
    {
        var builder = new StringBuilder(token.Length);

        foreach (var c in token.ToLower(Turkish))
        {
            var folded = c switch
            {
                'ş' => 's',
                'ı' => 'i',
                'ğ' => 'g',
                'ç' => 'c',
                'ö' => 'o',
                'ü' => 'u',
                _ => c,
            };

            if (char.IsLetterOrDigit(folded))
                builder.Append(folded);
        }

        return builder.ToString();
    }
}
