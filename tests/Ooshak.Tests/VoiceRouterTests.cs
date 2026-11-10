using Ooshak.Core;

namespace Ooshak.Tests;

public class VoiceRouterTests
{
    private const string Atlas = "Atlas Backend";
    private const string Backend = "Orbit Backend (API, worker)";
    private const string Mobile = "Orbit Mobil (Expo)";
    private const string General = "Ooshak Genel";

    private static VoiceRouter Create(int windowSeconds = 0, Func<DateTimeOffset>? clock = null)
    {
        var router = new VoiceRouter(["uşak", "ooshak"], TimeSpan.FromSeconds(windowSeconds), clock);
        router.SetTargets([Atlas, Backend, Mobile], new Dictionary<string, string> { ["etlas"] = Atlas });
        return router;
    }

    [Fact]
    public void Speech_without_the_name_is_ignored() =>
        Assert.IsType<RouteResult.Ignore>(Create().Route("Atlas'a testleri çalıştırmasını söyle"));

    [Fact]
    public void The_name_later_in_a_sentence_is_ignored() =>
        Assert.IsType<RouteResult.Ignore>(Create().Route("yarın sabah otobüsle gidip dönüşte Uşak'a uğrayacağım"));

    [Theory]
    [InlineData("Uşak, Atlas'a söyle testleri çalıştırsın")]
    [InlineData("uşak atlasa yaz testleri çalıştırsın")]
    [InlineData("Ooshak Etlas'a testleri çalıştırsın")]
    [InlineData("Hey uşak, Atlas'a testleri çalıştırsın de")]
    public void A_named_session_receives_the_message(string transcript)
    {
        var send = Assert.IsType<RouteResult.Send>(Create().Route(transcript));
        Assert.Equal(Atlas, send.Title);
        Assert.Equal("testleri çalıştırsın", send.Text);
    }

    [Fact]
    public void Switching_makes_the_target_sticky_but_the_name_stays_required()
    {
        var router = Create();
        Assert.IsType<RouteResult.SwitchTo>(router.Route("Uşak, Atlas'a geç."));

        Assert.IsType<RouteResult.Ignore>(router.Route("loglara da bak"));

        var send = Assert.IsType<RouteResult.Send>(router.Route("Uşak, loglara da bak"));
        Assert.Equal(Atlas, send.Title);
        Assert.Equal("loglara da bak", send.Text);
    }

    [Fact]
    public void A_message_with_no_target_asks_which_session() =>
        Assert.IsType<RouteResult.NeedTarget>(Create().Route("Uşak, testleri çalıştır"));

    [Fact]
    public void A_shared_first_word_does_not_guess_a_session() =>
        Assert.IsType<RouteResult.NeedTarget>(Create().Route("Uşak, Orbit'e söyle deploy etsin"));

    [Fact]
    public void Two_word_alias_picks_the_right_session()
    {
        var send = Assert.IsType<RouteResult.Send>(Create().Route("Uşak, Orbit Mobil'e söyle build alsın"));
        Assert.Equal(Mobile, send.Title);
        Assert.Equal("build alsın", send.Text);
    }

    [Fact]
    public void While_speaking_only_a_named_stop_counts()
    {
        var router = Create();
        router.Route("Uşak, Atlas'a geç");

        Assert.IsType<RouteResult.Ignore>(router.Route("Uşak, testleri çalıştır", speaking: true));
        Assert.IsType<RouteResult.Ignore>(router.Route("sus", speaking: true));
        Assert.IsType<RouteResult.StopSpeaking>(router.Route("Uşak sus!", speaking: true));
    }

    [Fact]
    public void A_stop_word_that_starts_a_longer_message_is_a_message()
    {
        var router = Create();
        router.Route("Uşak, Atlas'a geç");

        var send = Assert.IsType<RouteResult.Send>(router.Route("Uşak, dur bakalım önce planı göster"));
        Assert.Equal("dur bakalım önce planı göster", send.Text);
    }

    private static VoiceRouter WithGeneral(Func<DateTimeOffset> clock)
    {
        var router = new VoiceRouter(["uşak"], TimeSpan.Zero, clock);
        router.SetTargets(
            [Atlas, General],
            new Dictionary<string, string> { ["atlas"] = Atlas, ["genel"] = General });
        router.SetGeneral(General, TimeSpan.FromMinutes(3));
        return router;
    }

    [Fact]
    public void Unnamed_speech_goes_to_the_general_session_when_nothing_was_chosen()
    {
        var send = Assert.IsType<RouteResult.Send>(WithGeneral(() => DateTimeOffset.UnixEpoch).Route("Uşak, naber"));
        Assert.Equal(General, send.Title);
        Assert.Equal("naber", send.Text);
    }

    [Fact]
    public void Unnamed_speech_stays_with_a_recent_session()
    {
        var now = DateTimeOffset.UnixEpoch;
        var router = WithGeneral(() => now);
        router.Route("Uşak, Atlas'a söyle test etsin");

        now = now.AddMinutes(2);

        Assert.Equal(Atlas, Assert.IsType<RouteResult.Send>(router.Route("Uşak, loglara da bak")).Title);
    }

    [Fact]
    public void Unnamed_speech_returns_to_the_general_session_after_the_window()
    {
        var now = DateTimeOffset.UnixEpoch;
        var router = WithGeneral(() => now);
        router.Route("Uşak, Atlas'a söyle test etsin");

        now = now.AddMinutes(4);

        Assert.Equal(General, Assert.IsType<RouteResult.Send>(router.Route("Uşak, naber")).Title);
    }

    [Fact]
    public void Saying_general_switches_to_it_at_once()
    {
        var router = WithGeneral(() => DateTimeOffset.UnixEpoch);
        router.Route("Uşak, Atlas'a geç");

        Assert.Equal(General, Assert.IsType<RouteResult.SwitchTo>(router.Route("Uşak, genele geç")).Title);
    }

    [Theory]
    [InlineData("Uşak, bana efendim de.", "efendim")]
    [InlineData("Uşak, bana Deniz diye hitap et", "Deniz")]
    [InlineData("Uşak, bana patron diye seslen", "patron")]
    [InlineData("Uşak, bana Deniz Bey diye de", "Deniz Bey")]
    public void Saying_how_to_be_addressed_is_recognized(string transcript, string expected) =>
        Assert.Equal(expected, Assert.IsType<RouteResult.SetAddress>(Create().Route(transcript)).Name);

    [Theory]
    [InlineData("Uşak, bana merhaba de")]
    [InlineData("Uşak, bana bir film öner de")]
    [InlineData("Uşak, bana şunu anlat")]
    public void Ordinary_requests_are_not_taken_for_a_name(string transcript)
    {
        var router = Create();
        router.Route("Uşak, Atlas'a geç");
        Assert.IsType<RouteResult.Send>(router.Route(transcript));
    }

    private static VoiceRouter WithMediator()
    {
        var router = Create();
        router.SetMediator(General);
        return router;
    }

    [Theory]
    [InlineData("Uşak, naber", "naber")]
    [InlineData("Uşak, Atlas'a söyle testleri çalıştırsın", "Atlas'a söyle testleri çalıştırsın")]
    [InlineData("Uşak, Atlas'a geç", "Atlas'a geç")]
    public void The_mediator_receives_every_sentence_word_for_word(string transcript, string expected)
    {
        var send = Assert.IsType<RouteResult.Send>(WithMediator().Route(transcript));
        Assert.Equal(General, send.Title);
        Assert.Equal(expected, send.Text);
    }

    [Fact]
    public void Speech_controls_stay_local_with_a_mediator()
    {
        var router = WithMediator();

        Assert.IsType<RouteResult.StopSpeaking>(router.Route("Uşak sus"));
        Assert.IsType<RouteResult.PauseSpeaking>(router.Route("Uşak dur"));
        Assert.IsType<RouteResult.ResumeSpeaking>(router.Route("Uşak devam et", paused: true));
        Assert.IsType<RouteResult.SetAddress>(router.Route("Uşak, bana patron diye hitap et"));
    }

    [Fact]
    public void Clearing_the_mediator_restores_named_sessions()
    {
        var router = WithMediator();
        router.SetMediator("");

        Assert.Equal(Atlas, Assert.IsType<RouteResult.Send>(router.Route("Uşak, Atlas'a söyle test etsin")).Title);
    }

    [Theory]
    [InlineData("bana patron de", "patron")]
    [InlineData("bana Deniz diye hitap et", "Deniz")]
    [InlineData("call me boss", "boss")]
    [InlineData("patron", "patron")]
    [InlineData("Deniz Bey", "Deniz Bey")]
    [InlineData("", "")]
    public void A_typed_sentence_is_reduced_to_the_name(string typed, string expected) =>
        Assert.Equal(expected, VoiceRouter.NormalizeAddress(typed));

    [Fact]
    public void Pause_words_pause_while_reading() =>
        Assert.IsType<RouteResult.PauseSpeaking>(Create().Route("Uşak dur", speaking: true));

    [Fact]
    public void Continue_resumes_only_while_something_is_paused()
    {
        var router = Create();
        router.Route("Uşak, Atlas'a geç");

        Assert.IsType<RouteResult.ResumeSpeaking>(router.Route("Uşak, devam et", paused: true));
        Assert.Equal("devam et", Assert.IsType<RouteResult.Send>(router.Route("Uşak, devam et")).Text);
    }

    [Theory]
    [InlineData("testler geçti hepsi tamam Uşak sus")]
    [InlineData("Uşak sus artık bu kadar yeter")]
    public void A_stop_command_is_found_inside_the_apps_own_voice(string heard) =>
        Assert.IsType<RouteResult.StopSpeaking>(Create().FindCommandDuringSpeech(heard));

    [Fact]
    public void A_pause_command_is_found_inside_the_apps_own_voice() =>
        Assert.IsType<RouteResult.PauseSpeaking>(Create().FindCommandDuringSpeech("derleme bitti Uşak dur lütfen"));

    [Theory]
    [InlineData("testler geçti, sus demeden önce bitti")]
    [InlineData("Uşak iyi bir addır")]
    public void Reading_without_a_command_is_left_alone(string heard) =>
        Assert.Null(Create().FindCommandDuringSpeech(heard));

    [Fact]
    public void After_a_reply_one_sentence_needs_no_name_and_goes_to_the_last_session()
    {
        var now = DateTimeOffset.UnixEpoch;
        var router = Create(clock: () => now);
        router.Route("Uşak, Atlas'a söyle testleri çalıştırsın");

        router.OpenConversation(TimeSpan.FromSeconds(12));
        now = now.AddSeconds(5);

        var send = Assert.IsType<RouteResult.Send>(router.Route("loglara da bak"));
        Assert.Equal(Atlas, send.Title);
        Assert.Equal("loglara da bak", send.Text);

        Assert.IsType<RouteResult.Ignore>(router.Route("bu artık gitmemeli"));
    }

    [Fact]
    public void The_follow_up_window_expires()
    {
        var now = DateTimeOffset.UnixEpoch;
        var router = Create(clock: () => now);
        router.Route("Uşak, Atlas'a geç");
        router.OpenConversation(TimeSpan.FromSeconds(12));

        now = now.AddSeconds(13);
        Assert.IsType<RouteResult.Ignore>(router.Route("loglara da bak"));
    }

    [Theory]
    [InlineData("Altyazı M.K.")]
    [InlineData("İzlediğiniz için teşekkür ederim.")]
    public void Whisper_noise_never_reaches_a_session(string transcript)
    {
        var router = Create();
        router.Route("Uşak, Atlas'a geç");
        router.OpenConversation(TimeSpan.FromSeconds(12));

        Assert.IsType<RouteResult.Ignore>(router.Route(transcript));
    }

    [Fact]
    public void Without_a_last_session_the_window_does_nothing()
    {
        var router = Create();
        router.OpenConversation(TimeSpan.FromSeconds(12));

        Assert.IsType<RouteResult.Ignore>(router.Route("loglara da bak"));
    }

    [Fact]
    public void Conversation_window_allows_speech_without_the_name_until_it_ends()
    {
        var now = DateTimeOffset.UnixEpoch;
        var router = Create(windowSeconds: 20, clock: () => now);
        router.Route("Uşak, Atlas'a geç");

        now = now.AddSeconds(10);
        Assert.IsType<RouteResult.Send>(router.Route("loglara da bak"));

        now = now.AddSeconds(25);
        Assert.IsType<RouteResult.Ignore>(router.Route("loglara da bak"));
    }
}
