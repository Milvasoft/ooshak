using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Ooshak.Core;

/// <param name="ExtractCheck">For an archive, a file that exists once it has been unpacked.</param>
public sealed record ModelFile(string Name, string Url, long Size, string Sha256, string? ExtractCheck = null);

public sealed record ModelProgress(string File, long Done, long Total);

/// <summary>
/// The speech models Ooshak needs. Every address is pinned to a fixed revision and every file is
/// checked against its SHA-256, so a changed upstream file is refused instead of run.
/// </summary>
public static class ModelCatalog
{
    private const string WhisperRevision = "5359861c739e955e79d9a303bcbc70fb988958b1";
    private const string VoicesRevision = "cf910fb59f1d79ab3b625a63993a108751c6c25d";
    private const string SileroRevision = "066409f3d9db45a30d21dfcbf9147826ffdd7052";
    private const string Voices = $"https://huggingface.co/rhasspy/piper-voices/resolve/{VoicesRevision}";

    public static readonly IReadOnlyList<ModelFile> All =
    [
        new("silero_vad.onnx",
            $"https://raw.githubusercontent.com/snakers4/silero-vad/{SileroRevision}/src/silero_vad/data/silero_vad.onnx",
            2_327_524, "1A153A22F4509E292A94E67D6F9B85E8DEB25B4988682B7E174C65279D8788E3"),
        new("piper_windows_amd64.zip",
            "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip",
            22_477_236, "F3C58906402B24F3A96D92145F58ACBA6D86C9B5DB896D207F78DC80811EFCEA", @"piper\piper.exe"),
        new("tr_TR-dfki-medium.onnx", $"{Voices}/tr/tr_TR/dfki/medium/tr_TR-dfki-medium.onnx",
            63_201_294, "2844717F524AB965D3FE86E60562CBB601D3E456836EFCC2196CC3A14112A8FB"),
        new("tr_TR-dfki-medium.onnx.json", $"{Voices}/tr/tr_TR/dfki/medium/tr_TR-dfki-medium.onnx.json",
            4_960, "13EBD7810F1B61B5027583CF3131A0A233B6EA81C38F2200EBC4FF41C3CCA039"),
        new("en_US-ljspeech-medium.onnx", $"{Voices}/en/en_US/ljspeech/medium/en_US-ljspeech-medium.onnx",
            63_531_379, "6F52A751E2349ABE7A76735EB09DC1875298C77EA2342FFD2FEF79FF81B87F22"),
        new("en_US-ljspeech-medium.onnx.json", $"{Voices}/en/en_US/ljspeech/medium/en_US-ljspeech-medium.onnx.json",
            4_972, "141D612CC0A95ED7EFC1CA936B845C2364967F2E9217C5DBFCF69FC4D6C65860"),
        new("ggml-large-v3-turbo.bin",
            $"https://huggingface.co/ggerganov/whisper.cpp/resolve/{WhisperRevision}/ggml-large-v3-turbo.bin",
            1_624_555_275, "1FC70F774D38EB169993AC391EEA357EF47C88757EF72EE5943879B7E8E2BC69"),
    ];

    public static IReadOnlyList<ModelFile> Missing(string modelsDir) =>
        All.Where(f => !File.Exists(Path.Combine(modelsDir, f.ExtractCheck ?? f.Name))).ToList();

    public static long TotalBytes(IEnumerable<ModelFile> files) => files.Sum(f => f.Size);
}

public static class ModelDownloader
{
    private const int BufferSize = 1 << 16;
    private const long ReportEveryBytes = 1 << 20;

    private static readonly HttpClient Http = CreateClient();

    public static async Task DownloadAsync(
        IReadOnlyList<ModelFile> files, string directory, IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var total = ModelCatalog.TotalBytes(files);
        long done = 0;

        foreach (var file in files)
        {
            var target = Path.Combine(directory, file.Name);
            var partial = target + ".part";

            using (var response = await Http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(partial);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                var buffer = new byte[BufferSize];
                long sinceReport = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    done += read;
                    sinceReport += read;

                    if (sinceReport >= ReportEveryBytes)
                    {
                        sinceReport = 0;
                        progress?.Report(new ModelProgress(file.Name, done, total));
                    }
                }

                var actual = Convert.ToHexString(hash.GetHashAndReset());
                if (!actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    output.Close();
                    File.Delete(partial);
                    throw new InvalidDataException($"{file.Name}: SHA-256 does not match the pinned value.");
                }
            }

            File.Move(partial, target, overwrite: true);

            if (file.ExtractCheck is not null)
            {
                ZipFile.ExtractToDirectory(target, directory, overwriteFiles: true);
                File.Delete(target);
            }

            progress?.Report(new ModelProgress(file.Name, done, total));
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Ooshak", "1.0"));
        return client;
    }
}
