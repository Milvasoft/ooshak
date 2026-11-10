using Ooshak.Core;

namespace Ooshak.Tests;

public class EchoGuardTests
{
    private const string Spoken = "Ben de senin söylemeni bekliyordum. Bir şey sorabilirsin ya da istersen diğer oturumlara bir iş ileteyim.";

    [Theory]
    [InlineData("Ben de senin söylemeni bekliyordum. Bir şey sorabilirsin ya da istersen diğer oturumlara bir işiyle değil.")]
    [InlineData("Buradayım, söyle bakalım")]
    public void Own_voice_is_recognized(string heard)
    {
        var spoken = heard.StartsWith("Burada", StringComparison.Ordinal) ? "Narved! Buradayım, söyle bakalım, ne yapıyoruz?" : Spoken;
        Assert.True(EchoGuard.IsEcho(heard, spoken));
    }

    [Theory]
    [InlineData("loglara da bak")]
    [InlineData("Atlas oturumunda testleri çalıştır")]
    public void New_speech_is_not_an_echo(string heard) =>
        Assert.False(EchoGuard.IsEcho(heard, Spoken));

    [Theory]
    [InlineData("Evet", "Evet, tamamladım.")]
    [InlineData("loglara bak", null)]
    public void Single_words_and_silence_do_not_count(string heard, string? spoken) =>
        Assert.False(EchoGuard.IsEcho(heard, spoken));
}
