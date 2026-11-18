using System.Runtime.InteropServices;
using PortAudioSharp;
using PaStream = PortAudioSharp.Stream;

namespace Ooshak.Core.Mac;

internal static class PortAudioHost
{
    private static readonly Lazy<bool> Started = new(() =>
    {
        PortAudio.LoadNativeLibrary();
        PortAudio.Initialize();
        return true;
    });

    public static void EnsureStarted() => _ = Started.Value;

    public static IReadOnlyList<int> InputDevices()
    {
        EnsureStarted();
        return Enumerable.Range(0, PortAudio.DeviceCount)
            .Where(i => PortAudio.GetDeviceInfo(i).maxInputChannels > 0)
            .ToList();
    }
}

public sealed class PortAudioMicrophone : IMicrophone
{
    private readonly PaStream _stream;

    // Kept in a field: the native side calls it for as long as the stream lives.
    private readonly PaStream.Callback _callback;

    public PortAudioMicrophone(int device)
    {
        PortAudioHost.EnsureStarted();
        var inputs = PortAudioHost.InputDevices();
        var index = device >= 0 && device < inputs.Count ? inputs[device] : PortAudio.DefaultInputDevice;
        if (index == PortAudio.NoDevice)
            throw new InvalidOperationException("No microphone was found.");

        var info = PortAudio.GetDeviceInfo(index);
        var parameters = new StreamParameters
        {
            device = index,
            channelCount = 1,
            sampleFormat = SampleFormat.Int16,
            suggestedLatency = info.defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero,
        };

        var frames = (uint)(AudioDevices.SampleRate * AudioDevices.BufferMilliseconds / 1000);
        _callback = OnAudio;
        _stream = new PaStream(parameters, null, AudioDevices.SampleRate, frames, StreamFlags.ClipOff, _callback, null);
    }

    public event Action<byte[], int>? Data;

    public static IReadOnlyList<string> Names() =>
        PortAudioHost.InputDevices().Select(i => PortAudio.GetDeviceInfo(i).name).ToList();

    public void Start() => _stream.Start();

    private StreamCallbackResult OnAudio(IntPtr input, IntPtr output, uint frames, ref StreamCallbackTimeInfo time,
        StreamCallbackFlags flags, IntPtr userData)
    {
        if (input == IntPtr.Zero || frames == 0)
            return StreamCallbackResult.Continue;

        var buffer = new byte[frames * 2];
        Marshal.Copy(input, buffer, 0, buffer.Length);
        Data?.Invoke(buffer, buffer.Length);
        return StreamCallbackResult.Continue;
    }

    public void Dispose()
    {
        try
        {
            _stream.Stop();
        }
        catch (PortAudioException)
        {
            // Already stopped.
        }

        _stream.Dispose();
    }
}

public sealed class PortAudioPlayback : IPlayback
{
    private readonly byte[] _pcm;
    private readonly PaStream _stream;
    private readonly PaStream.Callback _callback;
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _position;
    private volatile bool _paused;
    private volatile bool _stopped;

    public PortAudioPlayback(byte[] pcm, int sampleRate)
    {
        PortAudioHost.EnsureStarted();
        _pcm = pcm;

        var device = PortAudio.DefaultOutputDevice;
        if (device == PortAudio.NoDevice)
            throw new InvalidOperationException("No speaker was found.");

        var parameters = new StreamParameters
        {
            device = device,
            channelCount = 1,
            sampleFormat = SampleFormat.Int16,
            suggestedLatency = PortAudio.GetDeviceInfo(device).defaultHighOutputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero,
        };

        _callback = OnAudio;
        _stream = new PaStream(null, parameters, sampleRate, PortAudio.FramesPerBufferUnspecified, StreamFlags.ClipOff, _callback, null);
        _stream.SetFinishedCallback(_ => _finished.TrySetResult());
        _stream.Start();
    }

    public Task Completion => _finished.Task;

    public long PlayedBytes => Interlocked.Read(ref _position);

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    public void Stop()
    {
        _stopped = true;
        _paused = false;
    }

    private StreamCallbackResult OnAudio(IntPtr input, IntPtr output, uint frames, ref StreamCallbackTimeInfo time,
        StreamCallbackFlags flags, IntPtr userData)
    {
        var wanted = (int)frames * 2;
        var silence = new byte[wanted];

        if (_stopped)
        {
            Marshal.Copy(silence, 0, output, wanted);
            return StreamCallbackResult.Complete;
        }

        if (_paused)
        {
            Marshal.Copy(silence, 0, output, wanted);
            return StreamCallbackResult.Continue;
        }

        var position = (int)Interlocked.Read(ref _position);
        var available = Math.Min(wanted, _pcm.Length - position);
        if (available > 0)
            Marshal.Copy(_pcm, position, output, available);
        if (available < wanted)
            Marshal.Copy(silence, 0, output + Math.Max(available, 0), wanted - Math.Max(available, 0));

        Interlocked.Add(ref _position, Math.Max(available, 0));
        return position + available >= _pcm.Length ? StreamCallbackResult.Complete : StreamCallbackResult.Continue;
    }

    public void Dispose()
    {
        _stopped = true;
        _finished.TrySetResult();
        _stream.Dispose();
    }
}
