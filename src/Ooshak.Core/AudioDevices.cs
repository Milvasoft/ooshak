namespace Ooshak.Core;

/// <summary>A 16 kHz, 16-bit mono microphone that hands over raw buffers as they arrive.</summary>
public interface IMicrophone : IDisposable
{
    /// <summary>Raised with a buffer and the number of valid bytes in it.</summary>
    event Action<byte[], int>? Data;

    void Start();
}

/// <summary>One clip of 16-bit mono audio being played.</summary>
public interface IPlayback : IDisposable
{
    /// <summary>Completes when the clip has played to the end or was stopped.</summary>
    Task Completion { get; }

    /// <summary>Bytes of the clip that have been played so far.</summary>
    long PlayedBytes { get; }

    void Pause();

    void Resume();

    void Stop();
}

/// <summary>Opens the platform's microphone and speakers.</summary>
public static class AudioDevices
{
    public const int SampleRate = 16000;
    public const int BufferMilliseconds = 32;

    /// <param name="device">Index into <see cref="MicrophoneNames"/>, or -1 for the system default.</param>
    public static IMicrophone OpenMicrophone(int device) =>
#if WINDOWS
        new Windows.WaveInMicrophone(device);
#else
        new Mac.PortAudioMicrophone(device);
#endif

    public static IReadOnlyList<string> MicrophoneNames() =>
#if WINDOWS
        Windows.WaveInMicrophone.Names();
#else
        Mac.PortAudioMicrophone.Names();
#endif

    /// <summary>Starts playing signed 16-bit mono PCM at once.</summary>
    public static IPlayback Play(byte[] pcm, int sampleRate) =>
#if WINDOWS
        new Windows.WaveOutPlayback(pcm, sampleRate);
#else
        new Mac.PortAudioPlayback(pcm, sampleRate);
#endif
}
