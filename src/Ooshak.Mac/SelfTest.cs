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

        using var transcriber = new Transcriber(config.ModelPath(config.WhisperModel), config.Language);
        var router = new VoiceRouter(config.WakeNames, TimeSpan.Zero);
        router.SetTargets(["Atlas"], new Dictionary<string, string>());
        var vocabulary = VoicePrompt.Build(config, ["Atlas"]);

        var heard = new List<string>();
        var utterances = new List<float[]>();
        var vadLoaded = false;
        try
        {
            using var vad = new SileroVad(config.ModelPath(config.VadModel));
            vadLoaded = true;
            var segmenter = new UtteranceSegmenter(config.SilenceMs);
            for (var offset = 0; offset + SileroVad.FrameSamples * 2 <= audio.Length; offset += SileroVad.FrameSamples * 2)
            {
                var frame = Samples(audio, offset, SileroVad.FrameSamples);
                if (segmenter.Feed(frame, vad.Probability(frame)) is { } utterance)
                    utterances.Add(utterance);
            }
        }
        catch (Exception e) when (!IsAppleSilicon && e is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException)
        {
            // ONNX Runtime ships no Intel Mac library, so an Intel build agent tests the rest without it.
            Console.WriteLine($"vad: not available on {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} ({e.GetType().Name}), transcribing the whole clip");
            utterances.Add(Samples(audio, 0, audio.Length / 2));
        }

        Console.WriteLine($"vad loaded: {vadLoaded}, utterances: {utterances.Count}");
        foreach (var utterance in utterances)
        {
            var text = await transcriber.TranscribeAsync(utterance, vocabulary);
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

    private static bool IsAppleSilicon =>
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64;

    private static float[] Samples(byte[] pcm, int offset, int count)
    {
        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = BitConverter.ToInt16(pcm, offset + i * 2) / 32768f;
        return samples;
    }
}
