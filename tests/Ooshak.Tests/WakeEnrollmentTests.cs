using Ooshak.Core;

namespace Ooshak.Tests;

public class WakeEnrollmentTests
{
    [Theory]
    [InlineData("Jarvis.", "jarvis")]
    [InlineData("Hey Jarvis!", "jarvis")]
    [InlineData("  Uşak, ", "uşak")]
    [InlineData("IŞAK", "ışak")]
    public void A_short_take_yields_the_name(string transcript, string expected) =>
        Assert.Equal(expected, WakeEnrollment.Extract(transcript));

    [Theory]
    [InlineData("")]
    [InlineData("...")]
    [InlineData("ee")]
    [InlineData("İzlediğiniz için çok teşekkür ederim")]
    public void An_unusable_take_is_rejected(string transcript) =>
        Assert.Null(WakeEnrollment.Extract(transcript));

    [Fact]
    public void Spellings_that_compare_equal_collapse_to_one() =>
        Assert.Equal(["uşak", "ışak"], WakeEnrollment.Variants(["uşak", "usak", "ışak"]));

    [Fact]
    public void A_learned_name_replaces_the_old_one()
    {
        var router = new VoiceRouter(["uşak"], TimeSpan.Zero);
        router.SetTargets(["Atlas Backend"], new Dictionary<string, string>());
        router.SetWakeNames(["jarvis", "carvis"]);

        Assert.IsType<RouteResult.Ignore>(router.Route("Uşak, Atlas'a söyle test etsin"));
        Assert.IsType<RouteResult.Send>(router.Route("Jarvis, Atlas'a söyle test etsin"));
        Assert.IsType<RouteResult.Send>(router.Route("Hey Carvis, Atlas'a söyle test etsin"));
    }
}
