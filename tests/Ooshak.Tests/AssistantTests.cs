using Ooshak.Core;

namespace Ooshak.Tests;

public class AssistantTests
{
    private static readonly string[] Wake = ["uşak", "ooshak"];

    private static readonly AskedQuestion Boss = new(
        "Boss savaşı kaç fazlı olsun?",
        "Boss",
        [new("Tek faz", ""), new("İki faz", ""), new("Üç faz", "")],
        MultiSelect: false);

    [Theory]
    [InlineData("Evet.", true)]
    [InlineData("Uşak onayla", true)]
    [InlineData("tamam izin ver", true)]
    [InlineData("Hayır.", false)]
    [InlineData("reddet", false)]
    [InlineData("izin verme", false)]
    public void Short_turkish_answers_approve_or_deny(string text, bool expected) =>
        Assert.Equal(expected, VoiceDecisions.Approval(text, Wake, english: false));

    [Theory]
    [InlineData("Yes, go ahead", true)]
    [InlineData("wants permission to run a command", null)]
    [InlineData("no", false)]
    [InlineData("deny it", false)]
    public void Short_english_answers_approve_or_deny(string text, bool? expected) =>
        Assert.Equal(expected, VoiceDecisions.Approval(text, Wake, english: true));

    [Theory]
    [InlineData("bir komut çalıştırmak için izin istiyor")]
    [InlineData("izin istiyor onaylıyor musun")]
    [InlineData("Bence bu testleri sonra çalıştıralım ve tamam diyelim")]
    [InlineData("merhaba")]
    public void Longer_or_unrelated_speech_is_not_an_answer(string text) =>
        Assert.Null(VoiceDecisions.Approval(text, Wake, english: false));

    [Theory]
    [InlineData("iki faz", "İki faz")]
    [InlineData("Uşak, üç faz olsun", "Üç faz")]
    [InlineData("ilki", "Tek faz")]
    [InlineData("ikincisi", "İki faz")]
    public void An_option_is_picked_by_name_or_position(string text, string expected) =>
        Assert.Equal(expected, VoiceDecisions.Option(text, Boss, Wake, english: false));

    [Fact]
    public void Speech_naming_no_option_picks_nothing() =>
        Assert.Null(VoiceDecisions.Option("şimdi bunu düşünmem lazım biraz bekle", Boss, Wake, english: false));

    [Theory]
    [InlineData("Uşak, ne durumdayız?")]
    [InlineData("Uşak durum ne")]
    [InlineData("uşak kim bekliyor")]
    public void Turkish_status_questions_ask_ooshak(string text)
    {
        var router = new VoiceRouter(Wake, TimeSpan.Zero);
        router.SetTargets(["Atlas"], new Dictionary<string, string>());

        Assert.IsType<RouteResult.Status>(router.Route(text));
    }

    [Fact]
    public void English_status_question_asks_ooshak()
    {
        var router = new VoiceRouter(Wake, TimeSpan.Zero);
        router.SetEnglish(true);

        Assert.IsType<RouteResult.Status>(router.Route("Ooshak, what's the status?"));
    }

    [Fact]
    public void A_saved_older_default_instruction_follows_the_new_text()
    {
        var config = new OoshakConfig
        {
            Language = L.Turkish,
            VoiceReplyInstruction = "[Sesli mod: 🎙 ile başlayan mesajlara cevabının son satırını 🔊 ile başlat; orada kişisel asistan gibi, en fazla iki cümle, kod ve liste olmadan konuş.]",
        };

        Assert.Contains("Jarvis", config.ReplyInstruction());
    }
}
