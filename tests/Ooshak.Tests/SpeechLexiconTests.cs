using Ooshak.Core;

namespace Ooshak.Tests;

public class SpeechLexiconTests
{
    private static readonly Dictionary<string, string> Words = new() { ["Atlas"] = "Etlas", ["commit"] = "kamit" };
    private static readonly Dictionary<string, string> NoWords = [];

    [Theory]
    [InlineData("Atlas bitti.", "Etlas bitti.")]
    [InlineData("atlas'ı açtım", "Etlas'ı açtım")]
    [InlineData("COMMIT attım", "kamit attım")]
    public void Known_words_are_rewritten_and_suffixes_kept(string input, string expected) =>
        Assert.Equal(expected, SpeechLexicon.Apply(input, Words));

    [Fact]
    public void Words_that_only_contain_a_known_word_stay() =>
        Assert.Equal("Atlaszor recommitli", SpeechLexicon.Apply("Atlaszor recommitli", Words));

    [Theory]
    [InlineData("code", "kod")]
    [InlineData("Code", "kod")]
    [InlineData("CODE", "kod")]
    [InlineData("review", "rivyu")]
    [InlineData("pull request", "pul rikuest")]
    [InlineData("repo'ya config", "repo'ya konfig")]
    [InlineData("query frontend", "kueri frontend")]
    [InlineData("feature issue", "fiçır işu")]
    [InlineData("API", "ey pi ay")]
    public void Built_in_terms_are_read_correctly(string input, string expected) =>
        Assert.Equal(expected, SpeechLexicon.Apply(input, NoWords));

    [Theory]
    [InlineData("bridge", "briç")]
    [InlineData("mountain", "mauntın")]
    [InlineData("thinking", "tinking")]
    public void English_outside_the_terms_uses_the_general_rule(string input, string expected) =>
        Assert.Equal(expected, SpeechLexicon.Apply(input, NoWords));

    [Theory]
    [InlineData("code'u", "kod'u")]
    [InlineData("code’un", "kod’un")]
    [InlineData("server'da", "sörvır'da")]
    [InlineData("'code' yazdı", "'kod' yazdı")]
    public void Suffixes_stay_and_quotes_do_not_count_as_suffixes(string input, string expected) =>
        Assert.Equal(expected, SpeechLexicon.Apply(input, NoWords));

    [Fact]
    public void Own_words_win_over_terms_and_terms_over_the_general_rule()
    {
        Assert.Equal("kodcuk", SpeechLexicon.Apply("code", new Dictionary<string, string> { ["code"] = "kodcuk" }));
        Assert.Equal("rid", SpeechLexicon.Apply("read", NoWords));
    }

    [Theory]
    [InlineData("CI geçti, ci kaldı", "si ay geçti, ci kaldı")]
    [InlineData("OS ve os", "o es ve os")]
    [InlineData("ID ve id", "ay di ve id")]
    public void Capital_terms_match_only_in_capitals(string input, string expected) =>
        Assert.Equal(expected, SpeechLexicon.Apply(input, NoWords));

    [Theory]
    [InlineData("PullRequest", "pul rikuest")]
    [InlineData("codeReview", "kod rivyu")]
    [InlineData("codeZzqx", "codeZzqx")]
    public void Joined_words_are_read_only_when_every_part_is_known(string input, string expected) =>
        Assert.Equal(expected, SpeechLexicon.Apply(input, NoWords));

    [Theory]
    [InlineData("kod test bir an kim son ben at sen o ne de da ve bu su ki mi")]
    [InlineData("anne baba kapi masa oda ama ara bile dur olan kalan gelen seni beni bana sana ona")]
    [InlineData("git dev file fail port cart curt link format plan program problem video data sistem")]
    [InlineData("ad al as ay az el er es et in is it on ok ot oy us bar ban bak bas but can car dam den dip don far gel hat her kan kar kas kol kum mal mat men not pas pay pek sat say sol tam tan tek ten top tur var yan yak yol")]
    [InlineData("verdin sola kente benzine eve size lar den nun biter")]
    [InlineData("gözlük çiçek ışık ağaç şeker üzüm")]
    public void Turkish_words_are_never_rewritten(string text) =>
        Assert.Equal(text, SpeechLexicon.Apply(text, NoWords));

    [Theory]
    [InlineData("code", "K OW1 D", "kod")]
    [InlineData("review", "R IY0 V Y UW2", "rivyu")]
    [InlineData("server", "S ER1 V ER0", "sörvır")]
    [InlineData("data", "D EY1 T AH0", "deyta")]
    [InlineData("docker", "D AA1 K ER0", "dokır")]
    [InlineData("father", "F AA1 DH ER0", "fadır")]
    [InlineData("think", "TH IH1 NG K", "tink")]
    [InlineData("quick", "K W IH1 K", "kuik")]
    [InlineData("web", "W EH1 B", "veb")]
    [InlineData("cloud", "K L AW1 D", "klaud")]
    [InlineData("page", "P EY1 JH", "peyç")]
    [InlineData("major", "M EY1 JH ER0", "meycır")]
    public void Arpabet_becomes_turkish_letters(string word, string arpabet, string expected) =>
        Assert.Equal(expected, TurkishSpelling.FromArpabet(word, arpabet));

    [Fact]
    public void Parse_reads_pairs_and_skips_bad_lines()
    {
        var parsed = SpeechLexicon.Parse("Atlas = Etlas\r\nbozuk satır\n = boş\nAPI=ey pi ay");
        Assert.Equal(2, parsed.Count);
        Assert.Equal("Etlas", parsed["atlas"]);
        Assert.Equal("ey pi ay", parsed["API"]);
    }

    [Fact]
    public void Format_round_trips_through_parse() =>
        Assert.Equal(Words, SpeechLexicon.Parse(SpeechLexicon.Format(Words)));
}
