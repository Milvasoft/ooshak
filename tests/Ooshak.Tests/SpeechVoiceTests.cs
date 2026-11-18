using System.Text;
using Ooshak.Core;

namespace Ooshak.Tests;

public class SpeechVoiceTests
{
    private const string Listing = """
        Albert              en_US    # Hello! My name is Albert.
        Eddy (Turkish (Turkey)) tr_TR    # Merhaba! Benim adım Eddy.
        Samantha            en_US    # Hello! My name is Samantha.
        Yelda               tr_TR    # Merhaba, benim adım Yelda.
        """;

    [Fact]
    public void The_preferred_voice_wins_when_installed() =>
        Assert.Equal("Yelda", MacSayVoice.PickVoice(Listing, L.Turkish));

    [Fact]
    public void English_prefers_samantha() =>
        Assert.Equal("Samantha", MacSayVoice.PickVoice(Listing, L.English));

    [Fact]
    public void Another_voice_of_the_language_is_used_when_the_preferred_one_is_missing() =>
        Assert.Equal("Eddy (Turkish (Turkey))", MacSayVoice.PickVoice(Listing.Replace("Yelda ", "Zeynep "), L.Turkish));

    [Fact]
    public void No_voice_for_the_language_leaves_the_system_default() =>
        Assert.Null(MacSayVoice.PickVoice("Albert              en_US    # Hello!", L.Turkish));

    [Fact]
    public void Wave_samples_are_found_after_other_chunks()
    {
        var samples = new byte[] { 1, 2, 3, 4 };
        var file = Riff(Chunk("fmt ", new byte[16]), Chunk("FLLR", new byte[3]), Chunk("data", samples));

        Assert.Equal(samples, Wave.Pcm(file));
    }

    [Fact]
    public void A_file_that_is_not_wave_gives_no_samples() =>
        Assert.Empty(Wave.Pcm(Encoding.ASCII.GetBytes("not a wave file")));

    private static byte[] Chunk(string id, byte[] body)
    {
        var chunk = new List<byte>(Encoding.ASCII.GetBytes(id));
        chunk.AddRange(BitConverter.GetBytes(body.Length));
        chunk.AddRange(body);
        if (body.Length % 2 == 1)
            chunk.Add(0);
        return chunk.ToArray();
    }

    private static byte[] Riff(params byte[][] chunks)
    {
        var body = chunks.SelectMany(c => c).ToArray();
        var file = new List<byte>(Encoding.ASCII.GetBytes("RIFF"));
        file.AddRange(BitConverter.GetBytes(body.Length + 4));
        file.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        file.AddRange(body);
        return file.ToArray();
    }
}
