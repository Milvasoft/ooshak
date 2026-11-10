using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;

namespace Ooshak.Core;

public static class AudioLevel
{
    private const double FloorDb = -52;
    private const double RangeDb = 42;

    /// <summary>Maps 16-bit PCM loudness onto 0 to 1 on a decibel scale, which matches how speech feels.</summary>
    public static float FromPcm16(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length < 2)
            return 0;

        double sum = 0;
        var count = pcm.Length / 2;
        for (var i = 0; i < count; i++)
        {
            double sample = BitConverter.ToInt16(pcm.Slice(i * 2, 2)) / 32768.0;
            sum += sample * sample;
        }

        var rms = Math.Sqrt(sum / count);
        return rms <= 0 ? 0 : (float)Math.Clamp((20 * Math.Log10(rms) - FloorDb) / RangeDb, 0, 1);
    }
}

/// <summary>Silero VAD v5 at 16 kHz. Keeps the recurrent state between frames.</summary>
public sealed class SileroVad(string modelPath) : IDisposable
{
    public const int FrameSamples = 512;
    private const int ContextSamples = 64;

    // A tiny model runs 31 times a second. By default its thread pool would spin on every core.
    private readonly InferenceSession _session = new(modelPath, SingleThreaded());
    private readonly float[] _context = new float[ContextSamples];
    private float[] _state = new float[2 * 128];

    public float Probability(float[] frame)
    {
        var input = new float[ContextSamples + FrameSamples];
        _context.CopyTo(input, 0);
        frame.CopyTo(input, ContextSamples);

        using var results = _session.Run(
        [
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(input, [1, input.Length])),
            NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(_state, [2, 1, 128])),
            NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { 16000 }, [1])),
        ]);

        _state = results.First(r => r.Name == "stateN").AsTensor<float>().ToArray();
        Array.Copy(frame, FrameSamples - ContextSamples, _context, 0, ContextSamples);
        return results.First(r => r.Name == "output").AsTensor<float>()[0];
    }

    private static SessionOptions SingleThreaded()
    {
        var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        return options;
    }

    public void Dispose() => _session.Dispose();
}

/// <summary>Cuts a stream of frames into utterances from per-frame speech probabilities.</summary>
public sealed class UtteranceSegmenter(int silenceMs)
{
    private const float SpeechThreshold = 0.5f;
    private const int FrameMs = 32;
    private const int PreRollFrames = 10;
    private const int MinSpeechFrames = 8;
    private const int MaxFrames = 30_000 / FrameMs;

    private readonly int _silenceFrames = Math.Max(1, silenceMs / FrameMs);
    private readonly Queue<float[]> _preRoll = new();
    private readonly List<float[]> _current = [];
    private int _speechFrames;
    private int _trailingSilence;

    public bool InSpeech => _current.Count > 0;

    public float[]? Feed(float[] frame, float probability)
    {
        var isSpeech = probability >= SpeechThreshold;

        if (_current.Count == 0)
        {
            if (!isSpeech)
            {
                _preRoll.Enqueue(frame);
                if (_preRoll.Count > PreRollFrames)
                    _preRoll.Dequeue();
                return null;
            }

            _current.AddRange(_preRoll);
            _preRoll.Clear();
        }

        _current.Add(frame);
        _speechFrames += isSpeech ? 1 : 0;
        _trailingSilence = isSpeech ? 0 : _trailingSilence + 1;

        return _trailingSilence >= _silenceFrames || _current.Count >= MaxFrames ? Finish() : null;
    }

    private float[]? Finish()
    {
        var result = _speechFrames >= MinSpeechFrames ? _current.SelectMany(f => f).ToArray() : null;
        _current.Clear();
        _speechFrames = 0;
        _trailingSilence = 0;
        return result;
    }
}

/// <summary>Reads the default microphone and raises one event per finished utterance.</summary>
public sealed class AudioListener(SileroVad vad, UtteranceSegmenter segmenter, int deviceNumber) : IDisposable
{
    private readonly WaveIn _wave = new()
    {
        WaveFormat = new WaveFormat(16000, 16, 1),
        BufferMilliseconds = 32,
        DeviceNumber = deviceNumber,
    };

    private readonly float[] _frame = new float[SileroVad.FrameSamples];
    private int _filled;
    private volatile float _level;
    private readonly float[] _recent = new float[16000 * 5 / 2];
    private int _recentEnd;

    public event Action<float[]>? UtteranceReady;

    /// <summary>When true, audio is discarded before it reaches the detector.</summary>
    public bool Muted { get; set; }

    /// <summary>Loudness of the latest buffer, from 0 to 1.</summary>
    public float Level => _level;

    public bool InSpeech => segmenter.InSpeech;

    /// <summary>A copy of the last few seconds of microphone audio, oldest sample first.</summary>
    public float[] RecentAudio()
    {
        lock (_recent)
        {
            var copy = new float[_recent.Length];
            var tail = _recent.Length - _recentEnd;
            Array.Copy(_recent, _recentEnd, copy, 0, tail);
            Array.Copy(_recent, 0, copy, tail, _recentEnd);
            return copy;
        }
    }

    public static IReadOnlyList<string> DeviceNames() =>
        Enumerable.Range(0, WaveIn.DeviceCount).Select(i => WaveIn.GetCapabilities(i).ProductName).ToList();

    public void Start()
    {
        _wave.DataAvailable += OnData;
        _wave.StartRecording();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (Muted)
        {
            _filled = 0;
            _level = 0;
            return;
        }

        _level = AudioLevel.FromPcm16(e.Buffer.AsSpan(0, e.BytesRecorded));

        for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            var sample = BitConverter.ToInt16(e.Buffer, i) / 32768f;
            lock (_recent)
            {
                _recent[_recentEnd] = sample;
                _recentEnd = (_recentEnd + 1) % _recent.Length;
            }

            _frame[_filled++] = sample;
            if (_filled < SileroVad.FrameSamples)
                continue;

            _filled = 0;
            var frame = (float[])_frame.Clone();
            var utterance = segmenter.Feed(frame, vad.Probability(frame));
            if (utterance is not null)
                UtteranceReady?.Invoke(utterance);
        }
    }

    public void Dispose()
    {
        _wave.StopRecording();
        _wave.Dispose();
    }
}

