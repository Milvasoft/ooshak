using NAudio.Wave;

namespace Ooshak.Core.Windows;

public sealed class WaveInMicrophone : IMicrophone
{
    private readonly WaveIn _wave;

    public WaveInMicrophone(int device)
    {
        _wave = new WaveIn
        {
            WaveFormat = new WaveFormat(AudioDevices.SampleRate, 16, 1),
            BufferMilliseconds = AudioDevices.BufferMilliseconds,
            DeviceNumber = device,
        };
        _wave.DataAvailable += (_, e) => Data?.Invoke(e.Buffer, e.BytesRecorded);
    }

    public event Action<byte[], int>? Data;

    public static IReadOnlyList<string> Names() =>
        Enumerable.Range(0, WaveIn.DeviceCount).Select(i => WaveIn.GetCapabilities(i).ProductName).ToList();

    public void Start() => _wave.StartRecording();

    public void Dispose()
    {
        _wave.StopRecording();
        _wave.Dispose();
    }
}

public sealed class WaveOutPlayback : IPlayback
{
    private readonly RawSourceWaveStream _stream;
    private readonly WaveOut _output = new();
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WaveOutPlayback(byte[] pcm, int sampleRate)
    {
        _stream = new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(sampleRate, 16, 1));
        _output.PlaybackStopped += (_, _) => _finished.TrySetResult();
        _output.Init(_stream);
        _output.Play();
    }

    public Task Completion => _finished.Task;

    public long PlayedBytes
    {
        get
        {
            try
            {
                return _output.GetPosition();
            }
            catch (Exception e) when (e is ObjectDisposedException or NAudio.MmException)
            {
                return 0;
            }
        }
    }

    public void Pause() => _output.Pause();

    public void Resume() => _output.Play();

    public void Stop() => _output.Stop();

    public void Dispose()
    {
        _output.Dispose();
        _stream.Dispose();
    }
}
