using System.Threading.Channels;

namespace Ooshak.Core;

/// <summary>Speaks queued sentences one at a time through a local voice.</summary>
public sealed class Speaker : IDisposable
{
    private const int BeepRate = 22050;
    private const int BeepFrequency = 880;
    private const double BeepSeconds = 0.12;
    private const short BeepAmplitude = 6000;
    private const int LevelWindowBytes = 2048;

    private static readonly TimeSpan PausePoll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PauseLimit = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan QuietBeforeSpeech = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxHold = TimeSpan.FromSeconds(60);

    public const double MinRate = 0.6;
    public const double MaxRate = 2.0;

    private readonly ISpeechVoice _voice;
    private readonly Channel<(string Text, double Rate)> _queue = Channel.CreateUnbounded<(string Text, double Rate)>();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource _current = new();
    private volatile bool _speaking;
    private (IPlayback Output, byte[] Pcm)? _active;
    private int _stopGeneration;
    private int _pauseCount;
    private volatile bool _paused;

    public Speaker(ISpeechVoice voice)
    {
        _voice = voice;
        _ = Task.Run(LoopAsync);
    }

    public bool IsSpeaking => _speaking && !_paused;

    public bool IsPaused => _paused;

    /// <summary>Freezes the current sentence and holds the queue until resumed or the limit passes.</summary>
    public void Pause()
    {
        if (!_speaking || _paused)
            return;

        _paused = true;
        _active?.Output.Pause();

        var pause = Interlocked.Increment(ref _pauseCount);
        _ = Task.Run(async () =>
        {
            await Task.Delay(PauseLimit);
            if (_paused && Volatile.Read(ref _pauseCount) == pause)
                Stop();
        });
    }

    public void Resume()
    {
        if (!_paused)
            return;

        _paused = false;
        _active?.Output.Resume();
    }

    /// <summary>While this returns true, queued sentences wait instead of starting.</summary>
    public Func<bool>? Hold { get; set; }

    /// <summary>Raised when the last queued sentence has finished or was cut off.</summary>
    public event Action? Drained;

    public void Say(string text, double rate = 1.0) => _queue.Writer.TryWrite((text, rate));

    /// <summary>Drops everything queued and cuts off the sentence being spoken.</summary>
    public void Stop()
    {
        while (_queue.Reader.TryRead(out _))
        {
        }

        Interlocked.Increment(ref _stopGeneration);
        _paused = false;
        _current.Cancel();
    }

    /// <summary>A short tone that confirms a command without talking over the user.</summary>
    public void Beep()
    {
        var count = (int)(BeepRate * BeepSeconds);
        var pcm = new byte[count * 2];

        for (var i = 0; i < count; i++)
        {
            var sample = (short)(BeepAmplitude * Math.Sin(2 * Math.PI * BeepFrequency * i / BeepRate));
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), sample);
        }

        _ = Task.Run(() => Play(pcm, BeepRate, CancellationToken.None));
    }

    private async Task LoopAsync()
    {
        try
        {
            await foreach (var (text, rate) in _queue.Reader.ReadAllAsync(_shutdown.Token))
            {
                var generation = Volatile.Read(ref _stopGeneration);

                // Not "speaking" while it waits, or the user's own words would be treated as echo.
                _speaking = false;
                while (_paused && generation == Volatile.Read(ref _stopGeneration))
                    await Task.Delay(PausePoll, _shutdown.Token);

                await SpeechGate.WaitAsync(Hold, QuietBeforeSpeech, MaxHold, _shutdown.Token);
                if (generation != Volatile.Read(ref _stopGeneration))
                    continue;

                _current = new CancellationTokenSource();
                _speaking = true;

                try
                {
                    var pcm = await _voice.SynthesizeAsync(text, Math.Clamp(rate, MinRate, MaxRate), _current.Token);
                    await Play(pcm, _voice.SampleRate, _current.Token, isSpeech: true);
                }
                catch (OperationCanceledException)
                {
                    // Stop() was called.
                }
                finally
                {
                    _speaking = _queue.Reader.Count > 0;
                    if (!_speaking)
                        Drained?.Invoke();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task Play(byte[] pcm, int sampleRate, CancellationToken cancellationToken, bool isSpeech = false)
    {
        using var output = AudioDevices.Play(pcm, sampleRate);

        if (isSpeech)
            _active = (output, pcm);

        await using (cancellationToken.Register(output.Stop))
            await output.Completion;

        if (isSpeech)
            _active = null;
    }

    /// <summary>Loudness of the audio being spoken right now, from 0 to 1.</summary>
    public float Level
    {
        get
        {
            if (_active is not { } active)
                return 0;

            var position = (int)Math.Min(active.Output.PlayedBytes, active.Pcm.Length) & ~1;
            var length = Math.Min(LevelWindowBytes, active.Pcm.Length - position);
            return AudioLevel.FromPcm16(active.Pcm.AsSpan(position, length));
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _current.Cancel();
    }
}

