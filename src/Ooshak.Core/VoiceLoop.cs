using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Ooshak.Core;

public enum VoiceState
{
    Listening,
    Hearing,
    Working,
    Speaking,
    Muted,
    Paused,
}

public sealed record VoiceSnapshot(VoiceState State, float Level, string? Note, string? Target, string? LastCommand, int AwaitingReplies);

/// <summary>Wires microphone, transcription, routing, delivery and spoken replies together.</summary>
public sealed class VoiceLoop : IDisposable
{
    private const int SampleRate = 16000;

    private static readonly TimeSpan UtterancePreRoll = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan EchoTail = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan EchoMemory = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan SessionRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PermissionTimeout = TimeSpan.FromSeconds(55);
    private static readonly TimeSpan SpeechCommandInterval = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan NoteLifetime = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan SpeakingNoteLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(3);

    private readonly OoshakConfig _config;
    private readonly LogBook _log;
    private readonly PermissionBroker _permissions;
    private readonly Channel<(float[] Samples, bool OverSpeech)> _utterances = Channel.CreateUnbounded<(float[] Samples, bool OverSpeech)>();
    private readonly ConcurrentDictionary<string, string> _awaitingReply = new();
    private readonly ConcurrentDictionary<string, byte> _instructed = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly VoiceRouter _router;
    private readonly ClaudeWindow _claude = new();
    private readonly Transcriber _transcriber;
    private readonly SileroVad _vad;
    private readonly AudioListener _listener;
    private readonly Speaker _speaker;
    private readonly HookServer _hooks;
    private Task _processing = Task.CompletedTask;
    private Task _speechWatch = Task.CompletedTask;
    private string _vocabulary = "";
    private volatile Action<string?>? _enrollment;
    private DateTimeOffset _sessionsLoadedAt = DateTimeOffset.MinValue;
    private volatile bool _working;
    private volatile bool _followUpArmed;
    private volatile bool _muted;
    private volatile bool _autoPaused;
    private long _speechEndedTicks;
    private (string? Text, DateTimeOffset Until) _lastSpoken;
    private (string? Text, DateTimeOffset Until) _note;
    private string? _lastCommand;

    public VoiceLoop(OoshakConfig config, LogBook log, PermissionBroker permissions)
    {
        _config = config;
        _log = log;
        _permissions = permissions;
        _router = new VoiceRouter(config.WakeNames, TimeSpan.FromSeconds(config.ConversationSeconds));
        _router.SetEnglish(config.Language == L.English);
        _transcriber = new Transcriber(config.ModelPath(config.WhisperModel), config.Language);
        _vad = new SileroVad(config.ModelPath(config.VadModel));
        _listener = new AudioListener(_vad, new UtteranceSegmenter(config.SilenceMs), config.MicrophoneDevice);
        _speaker = new Speaker(config.ModelPath(config.PiperExe), config.ModelPath(config.ActiveVoice));
        _hooks = new HookServer(config.Port, OnHook, OnPermissionAsync);
        _speaker.Drained += OnSpeechDrained;
        _speaker.Hold = () => _listener.InSpeech || _working;
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            _listener.Muted = _muted || _autoPaused;
        }
    }

    /// <summary>Set while a game or video fills the screen. Kept apart from <see cref="Muted"/> so it can undo itself.</summary>
    public bool AutoPaused
    {
        get => _autoPaused;
        set
        {
            _autoPaused = value;
            _listener.Muted = _muted || _autoPaused;
            if (value)
                _speaker.Stop();
        }
    }

    public void Start()
    {
        RefreshSessions();
        _hooks.Start();
        _listener.UtteranceReady += u => _utterances.Writer.TryWrite((u, StartedOverSpeech(u)));
        _listener.Start();
        _processing = Task.Run(ProcessAsync);
        _speechWatch = Task.Run(WatchForCommandsWhileSpeakingAsync);
    }

    public VoiceSnapshot Snapshot()
    {
        var state = _autoPaused ? VoiceState.Paused
            : _muted ? VoiceState.Muted
            : _speaker.IsSpeaking ? VoiceState.Speaking
            : _working ? VoiceState.Working
            : _listener.InSpeech ? VoiceState.Hearing
            : VoiceState.Listening;

        var level = state == VoiceState.Speaking ? _speaker.Level : _listener.Level;
        var note = _note.Until > DateTimeOffset.UtcNow ? _note.Text : null;
        return new VoiceSnapshot(state, level, note, _router.CurrentTitle, _lastCommand, _awaitingReply.Count);
    }

    public bool ReadingPaused => _speaker.IsPaused;

    /// <summary>Pauses reading, or continues it when it is already paused.</summary>
    public void ToggleReading()
    {
        if (_speaker.IsPaused)
            _speaker.Resume();
        else
            _speaker.Pause();
    }

    private void CancelReading()
    {
        _followUpArmed = false;
        _speaker.Stop();
    }

    // The sentence detector cannot close a sentence while the voice keeps playing, so during
    // playback a rolling window of recent audio is checked for a stop or pause command.
    private async Task WatchForCommandsWhileSpeakingAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(SpeechCommandInterval, _shutdown.Token);
                if (!_speaker.IsSpeaking)
                    continue;

                var heard = await _transcriber.TranscribeAsync(_listener.RecentAudio(), string.Join(", ", _config.WakeNames), _shutdown.Token);

                switch (_router.FindCommandDuringSpeech(heard))
                {
                    case RouteResult.StopSpeaking:
                        CancelReading();
                        break;
                    case RouteResult.PauseSpeaking:
                        _speaker.Pause();
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception e)
        {
            _log.Add(L.T("Konuşma işlenemedi: {0}", e.Message));
        }
    }

    /// <summary>Diverts speech to the callback instead of routing it, until <see cref="EndEnrollment"/>.</summary>
    public void BeginEnrollment(Action<string?> onTake)
    {
        _speaker.Stop();
        _enrollment = onTake;
    }

    public void EndEnrollment() => _enrollment = null;

    /// <summary>Applies the settings that do not need the microphone or models to restart.</summary>
    public void ApplyNames()
    {
        _router.SetWakeNames(_config.WakeNames);
        _sessionsLoadedAt = DateTimeOffset.MinValue;
        RefreshSessions();
    }

    private void RefreshSessions()
    {
        if (DateTimeOffset.UtcNow - _sessionsLoadedAt < SessionRefreshInterval)
            return;

        _sessionsLoadedAt = DateTimeOffset.UtcNow;
        var titles = SessionCatalog.LoadRecent().Select(s => s.Title).ToList();
        var aliases = new Dictionary<string, string>(_config.Aliases);
        if (!string.IsNullOrWhiteSpace(_config.GeneralSessionTitle))
            aliases[L.IsEnglish ? "general" : "genel"] = _config.GeneralSessionTitle;

        _router.SetTargets(titles, aliases);
        _router.SetGeneral(_config.GeneralSessionTitle, TimeSpan.FromMinutes(_config.StickyMinutes));
        _router.SetMediator(_config.ActiveMediator);
        _vocabulary = VoicePrompt.Build(_config, titles);
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var (samples, overSpeech) in _utterances.Reader.ReadAllAsync(_shutdown.Token))
            {
                try
                {
                    var enrollment = _enrollment;

                    // No vocabulary here: it would bias Whisper toward the old names.
                    var text = await _transcriber.TranscribeAsync(samples, enrollment is null ? _vocabulary : "", _shutdown.Token);

                    if (enrollment is null)
                    {
                        Handle(text, overSpeech);
                        continue;
                    }

                    _speaker.Beep();
                    enrollment(WakeEnrollment.Extract(text));
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _log.Add(L.T("Konuşma işlenemedi: {0}", e.Message));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private void Handle(string text, bool overSpeech)
    {
        RefreshSessions();

        // Checked before routing so that an echo cannot use up the follow-up window.
        if (DateTimeOffset.UtcNow < _lastSpoken.Until && EchoGuard.IsEcho(text, _lastSpoken.Text))
            return;

        switch (_router.Route(text, overSpeech || _speaker.IsSpeaking, _speaker.IsPaused))
        {
            case RouteResult.StopSpeaking:
                CancelReading();
                break;
            case RouteResult.PauseSpeaking:
                _speaker.Pause();
                break;
            case RouteResult.ResumeSpeaking:
                _speaker.Resume();
                break;
            case RouteResult.WhereAmI where:
                Speak(where.Title is null ? L.T("Bir oturum seçili değil.") : L.T("{0} oturumundayım.", where.Title));
                break;
            case RouteResult.NeedTarget:
                ShowNote(L.T("Hangi oturum?"), NoteLifetime);
                Speak(L.T("Hangi oturum?"));
                break;
            case RouteResult.SwitchTo target:
                _log.Add(L.T("Hedef: {0}", target.Title));
                ShowNote(target.Title, NoteLifetime);
                _speaker.Beep();
                break;
            case RouteResult.SetAddress address:
                SetAddress(address.Name);
                break;
            case RouteResult.Send send:
                Deliver(send);
                break;
        }
    }

    private void SetAddress(string name)
    {
        _config.AddressAs = name;
        _config.Save();
        ForgetInstructions();
        _log.Add(L.T("Hitap: {0}", name));
        ShowNote(name, NoteLifetime);
        Speak(L.T("Tamam, bundan sonra sana {0} diyeceğim.", name));
    }

    /// <summary>Makes every session receive the instruction again with its next voice message.</summary>
    public void ForgetInstructions() => _instructed.Clear();

    private void Deliver(RouteResult.Send send)
    {
        _working = true;
        _lastCommand = send.Text;
        ShowNote(send.Title, NoteLifetime);

        var session = SessionCatalog.Load().FirstOrDefault(s => s.Title == send.Title);

        DeliveryStatus status;
        try
        {
            status = _claude.Deliver(send.Title, ComposeMessage(send, session), restoreView: true, session?.LocalId);
        }
        finally
        {
            _working = false;
        }

        _log.Add($"{Describe(status)} · {send.Title} · {send.Text}");

        if (status != DeliveryStatus.Sent && session is not null)
            _instructed.TryRemove(session.CliSessionId, out _);

        if (status == DeliveryStatus.Sent)
        {
            if (session is not null)
                _awaitingReply[session.CliSessionId] = send.Title;

            ShowNote(L.T("{0}: gönderildi", send.Title), NoteLifetime);
            _speaker.Beep();
            return;
        }

        ShowNote(Describe(status), NoteLifetime);
        Speak(Describe(status));
    }

    private static string Describe(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Sent => L.T("Gönderildi"),
        DeliveryStatus.UserIsTyping => L.T("Yazıyorsun, göndermedim."),
        DeliveryStatus.TargetHasDraft => L.T("O oturumda taslak var, göndermedim."),
        DeliveryStatus.SessionNotInSidebar => L.T("O oturumu kenar çubuğunda bulamadım."),
        _ => L.T("Mesajı gönderemedim."),
    };

    /// <returns>The hook's reply, or null to leave the decision to Claude's own dialog.</returns>
    private async Task<string?> OnPermissionAsync(JsonElement payload)
    {
        var tool = payload.TryGetProperty("tool_name", out var name) ? name.GetString() ?? "" : "";
        var sessionId = payload.TryGetProperty("session_id", out var id) ? id.GetString() ?? "" : "";
        var title = SessionCatalog.Load().FirstOrDefault(s => s.CliSessionId == sessionId)?.Title;

        if (!_config.PermissionPopup || _autoPaused || title is null || PermissionText.NeedsTheApp(tool))
            return null;

        var input = payload.TryGetProperty("tool_input", out var value) ? value : default;
        var detail = PermissionText.Detail(tool, input);
        var allowed = await _permissions.AskAsync(SpokenName(title), detail, PermissionTimeout);

        _log.Add(L.T(allowed switch
        {
            true => "İzin verildi · {0} · {1}",
            false => "İzin reddedildi · {0} · {1}",
            null => "İzin yanıtlanmadı · {0} · {1}",
        }, title, detail));

        return allowed is { } allow ? PermissionText.Reply(allow, L.T("Kullanıcı reddetti.")) : null;
    }

    private void OnHook(string route, JsonElement payload)
    {
        var sessionId = payload.TryGetProperty("session_id", out var id) ? id.GetString() ?? "" : "";

        var voiceStarted = _awaitingReply.TryGetValue(sessionId, out var title);
        title = voiceStarted ? title : ReadableTitle(sessionId);
        if (title is null || (_config.ReplyMode == ReplyReadMode.SelectedSession && title != _config.MediatorSessionTitle))
            return;

        if (route == "notification")
        {
            // Idle reminders follow every finished turn, so other sessions only speak up for approvals.
            if (voiceStarted || IsPermissionRequest(payload))
            {
                _log.Add(L.T("{0} seni bekliyor", title));
                ShowNote(L.T("{0} seni bekliyor", title), NoteLifetime);
                Speak(L.T("{0} oturumu seni bekliyor.", SpokenName(title)));
            }

            return;
        }

        if (route == "stop")
        {
            _awaitingReply.TryRemove(sessionId, out _);
            if (voiceStarted)
                ResendInstructionIfForgotten(sessionId, payload);

            var reply = ReplyExtractor.FromStopPayload(payload);
            _log.Add(L.T("Cevap · {0} · {1}", title, reply));
            ShowNote(title, SpeakingNoteLifetime);

            // The session being talked to answers directly; any other one is introduced first.
            var name = SpokenName(title);
            _followUpArmed = _router.CurrentTitle == title && _config.FollowUpSeconds > 0;
            Speak(reply is null ? L.T("{0} oturumu bitti.", name)
                : _router.CurrentTitle == title ? reply
                : L.T("{0} oturumundan haber var. {1}", name, reply));
        }
    }

    /// <summary>The title to announce for a session the user did not speak to, or null to stay quiet.</summary>
    private string? ReadableTitle(string sessionId)
    {
        if (_config.ReplyMode == ReplyReadMode.VoiceStarted)
            return null;

        // Unknown ids are headless runs that have no place in the app.
        var title = SessionCatalog.Load().FirstOrDefault(s => s.CliSessionId == sessionId)?.Title;
        if (title is null || _config.ReplyMode is ReplyReadMode.AllSessions or ReplyReadMode.SelectedSession)
            return title;

        return _claude.ReadState().Shown == title ? title : null;
    }

    private static bool IsPermissionRequest(JsonElement payload) =>
        payload.TryGetProperty("message", out var message)
        && (message.GetString() ?? "").Contains("permission", StringComparison.OrdinalIgnoreCase);

    // The window opens when the reply has finished playing, so its length is all listening time.
    private void OnSpeechDrained()
    {
        Volatile.Write(ref _speechEndedTicks, DateTimeOffset.UtcNow.UtcTicks);
        _lastSpoken.Until = DateTimeOffset.UtcNow + EchoMemory;

        if (!_followUpArmed)
            return;

        _followUpArmed = false;
        var window = TimeSpan.FromSeconds(_config.FollowUpSeconds);
        _router.OpenConversation(window);
        ShowNote(L.T("Seni dinliyorum"), window);
    }

    // The instruction stays in the session's history, so repeating it would only burn tokens.
    private string ComposeMessage(RouteResult.Send send, SessionInfo? session)
    {
        var message = _config.MessagePrefix + send.Text;
        if (session is not null && !_instructed.TryAdd(session.CliSessionId, 0))
            return message;

        // The general session's own brief covers the reply format, but not how to address the user.
        var instruction = send.Title == _config.GeneralSessionTitle && !L.IsEnglish ? "" : _config.ReplyInstruction();
        var address = string.IsNullOrWhiteSpace(_config.AddressAs) ? "" : L.T("[Bana “{0}” diye hitap et.]", _config.AddressAs);
        var isOtherMediator = send.Title == _config.ActiveMediator && send.Title != _config.GeneralSessionTitle;
        var mediator = isOtherMediator ? L.T("[Aracı modu: Başka oturumları ilgilendiren isteklerimi oturumlar arası mesaj aracıyla onlara ilet.]") : "";
        return string.Join(' ', new[] { message, instruction, address, mediator }.Where(part => part.Length > 0));
    }

    // A reply without the marker means the session forgot, for example after compaction.
    private void ResendInstructionIfForgotten(string sessionId, JsonElement payload)
    {
        var last = payload.TryGetProperty("last_assistant_message", out var message) ? message.GetString() : null;
        if (ReplyExtractor.SpokenLine(last) is null)
            _instructed.TryRemove(sessionId, out _);
    }

    /// <summary>The short alias of a session if it has one, because full titles sound like a report.</summary>
    private string SpokenName(string title) =>
        _config.Aliases.FirstOrDefault(a => a.Value == title).Key ?? title;

    // The rewriting targets a Turkish voice; an English voice reads English as written.
    private static string Pronounce(string text, IReadOnlyDictionary<string, string> pronunciations) =>
        L.IsEnglish ? text : SpeechLexicon.Apply(text, pronunciations);

    private void ShowNote(string text, TimeSpan lifetime) => _note = (text, DateTimeOffset.UtcNow + lifetime);

    private void Speak(string text)
    {
        // Replies are still in the log and in the session; a long game should not end in a backlog of speech.
        if (_autoPaused)
            return;

        // The written form is kept because that is what Whisper produces when it hears the voice.
        _lastSpoken = (text, DateTimeOffset.UtcNow + EchoMemory);
        _speaker.Say(Pronounce(text, _config.Pronunciations), _config.SpeechRate);
    }

    // Detection closes a sentence only after trailing silence, so a sentence that began during
    // playback can finish after it. Its true start decides, not the moment it was reported.
    private bool StartedOverSpeech(float[] samples)
    {
        if (_speaker.IsSpeaking)
            return true;

        var start = DateTimeOffset.UtcNow - TimeSpan.FromSeconds((double)samples.Length / SampleRate) + UtterancePreRoll;
        return start.UtcTicks < Volatile.Read(ref _speechEndedTicks) + EchoTail.Ticks;
    }

    /// <summary>Reads a sample with settings that have not been saved yet.</summary>
    public void Preview(string text, double rate, IReadOnlyDictionary<string, string> pronunciations)
    {
        _speaker.Stop();
        _speaker.Say(Pronounce(text, pronunciations), rate);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _listener.Dispose();
        _hooks.Dispose();
        _speaker.Dispose();
        Task.WaitAll([_processing, _speechWatch], ShutdownWait);
        _vad.Dispose();
        _transcriber.Dispose();
        _claude.Dispose();
    }
}
