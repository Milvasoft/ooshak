using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ooshak.Core;

/// <summary>Turns a sentence into signed 16-bit mono PCM.</summary>
public interface ISpeechVoice
{
    int SampleRate { get; }

    /// <param name="rate">Speed, where 1 is the voice's natural pace.</param>
    Task<byte[]> SynthesizeAsync(string text, double rate, CancellationToken cancellationToken);
}

public static class SpeechVoices
{
    /// <summary>Piper on Windows; on macOS the system's own voices, since Piper ships no working Mac build.</summary>
    public static ISpeechVoice Create(OoshakConfig config) => OperatingSystem.IsWindows()
        ? new PiperVoice(config.ModelPath(config.PiperExe), config.ModelPath(config.ActiveVoice))
        : new MacSayVoice(config.Language);
}

/// <summary>A local Piper voice run as a child process.</summary>
public sealed class PiperVoice : ISpeechVoice
{
    private readonly string _piperExe;
    private readonly string _voicePath;

    public PiperVoice(string piperExe, string voicePath)
    {
        _piperExe = piperExe;
        _voicePath = voicePath;

        using var config = JsonDocument.Parse(File.ReadAllText(voicePath + ".json"));
        SampleRate = config.RootElement.GetProperty("audio").GetProperty("sample_rate").GetInt32();
    }

    public int SampleRate { get; }

    public async Task<byte[]> SynthesizeAsync(string text, double rate, CancellationToken cancellationToken)
    {
        // Piper's length scale is the inverse of speed: a smaller value means shorter, faster phonemes.
        var lengthScale = (1 / rate).ToString("0.###", CultureInfo.InvariantCulture);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(_piperExe)
            {
                ArgumentList = { "--model", _voicePath, "--output_raw", "--length_scale", lengthScale },
                WorkingDirectory = Path.GetDirectoryName(_piperExe)!,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        _ = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.StandardInput.WriteLineAsync(text);
        process.StandardInput.Close();

        using var pcm = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(pcm, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return pcm.ToArray();
    }
}

/// <summary>The macOS "say" command, writing 16-bit WAVE that is read back as PCM.</summary>
public sealed partial class MacSayVoice(string language) : ISpeechVoice
{
    private const string SayPath = "/usr/bin/say";
    private const int NaturalWordsPerMinute = 180;

    // The clearest built-in voice per language, used when the Mac has it.
    private static readonly Dictionary<string, string> Preferred = new()
    {
        [L.Turkish] = "Yelda",
        [L.English] = "Samantha",
    };

    private readonly Lazy<string?> _voice = new(() => PickVoice(language));

    public int SampleRate => 22050;

    public async Task<byte[]> SynthesizeAsync(string text, double rate, CancellationToken cancellationToken)
    {
        var file = Path.Combine(Path.GetTempPath(), $"ooshak-{Guid.NewGuid():N}.wav");
        try
        {
            var start = new ProcessStartInfo(SayPath)
            {
                RedirectStandardInput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (_voice.Value is { } voice)
            {
                start.ArgumentList.Add("-v");
                start.ArgumentList.Add(voice);
            }

            start.ArgumentList.Add("-r");
            start.ArgumentList.Add(((int)Math.Round(NaturalWordsPerMinute * rate)).ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--file-format=WAVE");
            start.ArgumentList.Add($"--data-format=LEI16@{SampleRate}");
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add(file);
            start.ArgumentList.Add("-f");
            start.ArgumentList.Add("-");

            using var process = Process.Start(start)!;
            _ = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.StandardInput.WriteAsync(text);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);

            return File.Exists(file) ? Wave.Pcm(await File.ReadAllBytesAsync(file, cancellationToken)) : [];
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Picks an installed voice for the language from "say -v ?", or null for the system default.</summary>
    public static string? PickVoice(string language)
    {
        try
        {
            var start = new ProcessStartInfo(SayPath) { RedirectStandardOutput = true, UseShellExecute = false };
            start.ArgumentList.Add("-v");
            start.ArgumentList.Add("?");
            using var process = Process.Start(start)!;
            var listing = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return PickVoice(listing, language);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    public static string? PickVoice(string listing, string language)
    {
        var voices = listing.Split('\n')
            .Select(line => VoiceLine().Match(line))
            .Where(m => m.Success && m.Groups[2].Value.StartsWith(language + "_", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();

        return Preferred.TryGetValue(language, out var preferred) && voices.Contains(preferred)
            ? preferred
            : voices.FirstOrDefault();
    }

    [GeneratedRegex(@"^(.+?)\s+([a-z]{2,3}_[A-Za-z0-9]{2,4})\s+#")]
    private static partial Regex VoiceLine();
}

public static class Wave
{
    /// <summary>The samples of a RIFF WAVE file, found by walking its chunks.</summary>
    public static byte[] Pcm(byte[] file)
    {
        const int HeaderBytes = 12;
        const int ChunkHeaderBytes = 8;

        if (file.Length < HeaderBytes || Encoding.ASCII.GetString(file, 0, 4) != "RIFF")
            return [];

        var offset = HeaderBytes;
        while (offset + ChunkHeaderBytes <= file.Length)
        {
            var id = Encoding.ASCII.GetString(file, offset, 4);
            var size = BitConverter.ToInt32(file, offset + 4);
            var body = offset + ChunkHeaderBytes;

            if (id == "data")
                return file[body..Math.Min(file.Length, body + size)];

            // Chunks are padded to an even length.
            offset = body + size + (size & 1);
        }

        return [];
    }
}
