using Ooshak.Core;

namespace Ooshak.Tests;

public sealed class ModelCatalogTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ooshak-models").FullName;

    [Fact]
    public void An_empty_folder_needs_every_model() =>
        Assert.Equal(ModelCatalog.All.Count, ModelCatalog.Missing(_directory).Count);

    [Fact]
    public void An_unpacked_archive_counts_as_present()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "piper"));
        File.WriteAllText(Path.Combine(_directory, "piper", "piper.exe"), "");

        Assert.DoesNotContain(ModelCatalog.Missing(_directory), f => f.Name == "piper_windows_amd64.zip");
    }

    [Fact]
    public void Every_download_is_pinned_and_checksummed()
    {
        Assert.All(ModelCatalog.All, file =>
        {
            Assert.StartsWith("https://", file.Url);
            Assert.DoesNotContain("/resolve/main/", file.Url);
            Assert.DoesNotContain("/master/", file.Url);
            Assert.Equal(64, file.Sha256.Length);
            Assert.True(file.Size > 0);
        });
    }

    [Fact]
    public void The_default_config_uses_files_from_the_catalog()
    {
        var config = new OoshakConfig();
        var names = ModelCatalog.All.Select(f => f.ExtractCheck ?? f.Name).ToHashSet();

        Assert.Contains(config.WhisperModel, names);
        Assert.Contains(config.VadModel, names);
        Assert.Contains(config.PiperExe, names);
        Assert.Contains(config.PiperVoice, names);
        Assert.Contains(config.PiperVoiceEnglish, names);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
