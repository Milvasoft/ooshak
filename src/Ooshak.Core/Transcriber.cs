using System.Text;
using Whisper.net;

namespace Ooshak.Core;

public sealed class Transcriber(string modelPath, string language) : IDisposable
{
    private readonly WhisperFactory _factory = WhisperFactory.FromPath(modelPath);
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>The native backend Whisper.net actually loaded, such as Cuda, Vulkan or Cpu.</summary>
    public static string? LoadedBackend => Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary?.ToString();

    /// <param name="samples">16 kHz mono audio.</param>
    /// <param name="vocabulary">Names Whisper should prefer, such as the wake name and session titles.</param>
    public async Task<string> TranscribeAsync(float[] samples, string vocabulary, CancellationToken cancellationToken = default)
    {
        await _oneAtATime.WaitAsync(cancellationToken);
        try
        {
            var builder = _factory.CreateBuilder().WithLanguage(language);
            if (vocabulary.Length > 0)
                builder = builder.WithPrompt(vocabulary);

            await using var processor = builder.Build();

            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
                text.Append(segment.Text);

            return text.ToString().Trim();
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    public void Dispose() => _factory.Dispose();
}
