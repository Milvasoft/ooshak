using System.Text.Json;

namespace Ooshak.Core;

/// <summary>Which finished sessions are read aloud. Values match the order in the settings list.</summary>
public enum ReplyReadMode
{
    VoiceStarted,
    VisibleSession,
    AllSessions,
    SelectedSession,
}

public sealed class OoshakConfig
{
    // Relaxed escaping keeps Turkish letters readable when the user edits the file by hand.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string[] WakeNames { get; set; } = ["uşak", "ışak", "ooshak", "ushak"];
    public string Language { get; set; } =
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == L.Turkish ? L.Turkish : L.English;

    public string WhisperModel { get; set; } = "ggml-large-v3-turbo.bin";
    public string VadModel { get; set; } = "silero_vad.onnx";
    public string PiperExe { get; set; } = @"piper\piper.exe";
    public string PiperVoice { get; set; } = "tr_TR-dfki-medium.onnx";
    public string PiperVoiceEnglish { get; set; } = "en_US-ljspeech-medium.onnx";
    public int Port { get; set; } = 47811;

    /// <summary>Seconds after a command in which speech needs no name. Zero keeps the name mandatory.</summary>
    public int ConversationSeconds { get; set; }

    public int SilenceMs { get; set; } = 700;
    public string MessagePrefix { get; set; } = "🎙 ";

    /// <summary>English and project terms Whisper should keep as written.</summary>
    public string[] Vocabulary { get; set; } =
    [
        "commit", "push", "pull", "merge", "rebase", "branch", "master", "deploy", "build", "release",
        "bug", "fix", "refactor", "test", "endpoint", "backend", "frontend", "database", "migration",
        "rollback", "cache", "token", "API", "SDK", "Docker", "Azure", "GitHub", "Claude",
    ];

    public ReplyReadMode ReplyMode { get; set; } = ReplyReadMode.VoiceStarted;

    /// <summary>With <see cref="ReplyReadMode.SelectedSession"/>, the only session spoken to and heard from.</summary>
    public string MediatorSessionTitle { get; set; } = "";

    public string? ActiveMediator => ReplyMode == ReplyReadMode.SelectedSession && !string.IsNullOrWhiteSpace(MediatorSessionTitle)
        ? MediatorSessionTitle
        : null;

    /// <summary>What sessions should call the user. Empty leaves it up to them.</summary>
    public string AddressAs { get; set; } = "";

    /// <summary>Title of the session that takes unnamed speech. Empty turns it off.</summary>
    public string GeneralSessionTitle { get; set; } = "Ooshak Genel";

    /// <summary>Minutes the last session keeps unnamed speech before it goes to the general session.</summary>
    public int StickyMinutes { get; set; } = 3;

    /// <summary>Seconds after a reply in which one sentence needs no wake name. Zero turns it off.</summary>
    public int FollowUpSeconds { get; set; } = 12;

    /// <summary>Sent once per session so voice messages get spoken-style answers. Empty turns it off.</summary>
    public string VoiceReplyInstruction { get; set; } = TurkishInstruction;

    private const string TurkishInstruction =
        "[Sesli mod: 🎙 ile başlayan mesajlara cevabının son satırını 🔊 ile başlat. O satırı Jarvis gibi söyle: önce sonuç, sonra gerekirse tek bir soru; en fazla iki kısa cümle, doğal ve samimi; kod, liste, dosya yolu ve kimlik numarası yok.]";

    private const string EnglishInstruction =
        "[Voice mode: for messages starting with 🎙, begin the last line of your reply with 🔊. Say that line like Jarvis, in English: the outcome first, then one question if needed; two short sentences at most, natural and warm; no code, lists, file paths or ids.]";

    // Earlier built-in texts: a config that still holds one gets the current text, not a frozen copy.
    private static readonly HashSet<string> PreviousInstructions =
    [
        "[Sesli mod: 🎙 ile başlayan mesajlara cevabının son satırını 🔊 ile başlat; orada kişisel asistan gibi, en fazla iki cümle, kod ve liste olmadan konuş.]",
        "[Voice mode: for messages starting with 🎙, begin the last line of your reply with 🔊; there, speak like a personal assistant in English, two sentences at most, no code or lists.]",
        TurkishInstruction,
        EnglishInstruction,
    ];

    /// <summary>The instruction for the current language. A customized text is left as written.</summary>
    public string ReplyInstruction() => PreviousInstructions.Contains(VoiceReplyInstruction)
        ? Language == L.English ? EnglishInstruction : TurkishInstruction
        : VoiceReplyInstruction;

    public string ActiveVoice => Language == L.English ? PiperVoiceEnglish : PiperVoice;

    /// <summary>Speech speed, where 1 is the voice's natural pace.</summary>
    public double SpeechRate { get; set; } = 1.0;

    /// <summary>The user's own spellings, which override the built-in terms in SpeechTerms.txt.</summary>
    public Dictionary<string, string> Pronunciations { get; set; } = [];

    /// <summary>Microphone number in the device list, or -1 for the system default.</summary>
    public int MicrophoneDevice { get; set; } = -1;

    /// <summary>Stops listening and reading while a game or video fills the screen.</summary>
    public bool AutoPauseFullscreen { get; set; } = true;

    /// <summary>Says "on it" out loud when a message went through, instead of only beeping.</summary>
    public bool SpokenAcknowledgements { get; set; } = true;

    /// <summary>Reads permission requests and questions aloud so they can be answered by voice.</summary>
    public bool AnnounceRequests { get; set; } = true;

    /// <summary>Shows permission requests in a popup instead of only in the Claude window.</summary>
    public bool PermissionPopup { get; set; } = true;

    public bool IslandEnabled { get; set; } = true;
    public double? IslandLeft { get; set; }
    public double? IslandTop { get; set; }

    /// <summary>Spoken alias to exact session title. These win over aliases derived from titles.</summary>
    public Dictionary<string, string> Aliases { get; set; } = [];

    // Packaged apps get AppData redirected into a private copy, so a location under the
    // profile root is the one that looks the same no matter what started Ooshak.
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ooshak");

    public static string ModelsDir => Path.Combine(AppDataDir, "models");

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");

    public string ModelPath(string relative) => Path.Combine(ModelsDir, relative);

    public static OoshakConfig Load()
    {
        if (File.Exists(ConfigPath))
            return JsonSerializer.Deserialize<OoshakConfig>(File.ReadAllText(ConfigPath), JsonOptions) ?? new OoshakConfig();

        var config = new OoshakConfig();
        config.Save();
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
