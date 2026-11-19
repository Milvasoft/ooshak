using Ooshak.Core;
using Ooshak.Core.Mac;

namespace Ooshak.Mac;

/// <summary>
/// Runs speech end to end without a window or microphone: the system voice says a command, Silero
/// cuts it out, Whisper writes it down and the router reads it. Used by CI on a real Mac.
/// </summary>
public static class SelfTest
{
    private const string Sentence = "Uşak, Atlas'a söyle testleri çalıştırsın.";

    public static async Task<int> RunAsync()
    {
        Console.WriteLine($"process: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");

        var config = new OoshakConfig { Language = L.Turkish };
        var missing = ModelCatalog.Missing(OoshakConfig.ModelsDir);
        Console.WriteLine($"downloading {missing.Count} files, {ModelCatalog.TotalBytes(missing) >> 20} MB");
        await ModelDownloader.DownloadAsync(missing, OoshakConfig.ModelsDir, null, CancellationToken.None);

        var voiceName = MacSayVoice.PickVoice(L.Turkish);
        Console.WriteLine($"say voice: {voiceName ?? "(system default)"}");
        var voice = new MacSayVoice(L.Turkish, AudioDevices.SampleRate);
        var pcm = await voice.SynthesizeAsync(Sentence, 1.0, CancellationToken.None);
        Console.WriteLine($"spoken: {pcm.Length / 2 / (double)AudioDevices.SampleRate:F1} s");

        // Silence around the sentence, as the microphone would hear it.
        var padding = new byte[AudioDevices.SampleRate];
        var audio = padding.Concat(pcm).Concat(padding).Concat(padding).ToArray();

        using var vad = new SileroVad(config.ModelPath(config.VadModel));
        using var transcriber = new Transcriber(config.ModelPath(config.WhisperModel), config.Language);
        var segmenter = new UtteranceSegmenter(config.SilenceMs);
        var router = new VoiceRouter(config.WakeNames, TimeSpan.Zero);
        router.SetTargets(["Atlas"], new Dictionary<string, string>());

        var heard = new List<string>();
        for (var offset = 0; offset + SileroVad.FrameSamples * 2 <= audio.Length; offset += SileroVad.FrameSamples * 2)
        {
            var frame = new float[SileroVad.FrameSamples];
            for (var i = 0; i < frame.Length; i++)
                frame[i] = BitConverter.ToInt16(audio, offset + i * 2) / 32768f;

            if (segmenter.Feed(frame, vad.Probability(frame)) is not { } utterance)
                continue;

            var text = await transcriber.TranscribeAsync(utterance, VoicePrompt.Build(config, ["Atlas"]));
            heard.Add(text);
            Console.WriteLine($"heard: {text}");
            Console.WriteLine($"route: {router.Route(text)}");
        }

        Console.WriteLine($"whisper backend: {Transcriber.LoadedBackend}");

        try
        {
            Console.WriteLine($"microphones: {string.Join(", ", AudioDevices.MicrophoneNames())}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"microphones: failed, {e.GetType().Name}: {e.Message}");
            return 3;
        }

        Console.WriteLine($"accessibility trusted: {MacAccessibility.IsTrusted(prompt: false)}");
        Console.WriteLine($"claude window: {ClaudeDesktop.Open().ReadState()}");

        return heard.Any(h => h.Length > 0) ? 0 : 2;
    }
}
