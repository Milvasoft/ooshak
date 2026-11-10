using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Ooshak.Core;

namespace Ooshak.Tests;

public class EnglishSupportTests
{
    private const string Atlas = "Atlas Backend";
    private const string General = "Ooshak General";

    private static VoiceRouter Create(Func<DateTimeOffset>? clock = null)
    {
        var router = new VoiceRouter(["ooshak"], TimeSpan.Zero, clock);
        router.SetEnglish(true);
        router.SetTargets([Atlas, General], new Dictionary<string, string> { ["atlas"] = Atlas, ["general"] = General });
        return router;
    }

    [Theory]
    [InlineData("Ooshak, tell Atlas to run the tests.", "run the tests.")]
    [InlineData("Ooshak, ask Atlas what failed", "what failed")]
    [InlineData("Ooshak, tell Atlas that the build is broken", "the build is broken")]
    [InlineData("Ooshak, Atlas, run the tests", "run the tests")]
    public void A_named_session_receives_the_message(string transcript, string expected)
    {
        var send = Assert.IsType<RouteResult.Send>(Create().Route(transcript));
        Assert.Equal(Atlas, send.Title);
        Assert.Equal(expected, send.Text);
    }

    [Theory]
    [InlineData("Ooshak, switch to Atlas.")]
    [InlineData("Ooshak, go back to Atlas")]
    [InlineData("Ooshak, switch to the Atlas session")]
    public void Switching_works_in_english(string transcript) =>
        Assert.Equal(Atlas, Assert.IsType<RouteResult.SwitchTo>(Create().Route(transcript)).Title);

    [Fact]
    public void Switch_to_general_returns_to_the_general_session() =>
        Assert.Equal(General, Assert.IsType<RouteResult.SwitchTo>(Create().Route("Ooshak, switch to general")).Title);

    [Fact]
    public void A_verb_without_a_known_session_stays_part_of_the_message()
    {
        var router = Create();
        router.Route("Ooshak, switch to Atlas");

        var send = Assert.IsType<RouteResult.Send>(router.Route("Ooshak, go through the logs"));
        Assert.Equal("go through the logs", send.Text);
    }

    [Theory]
    [InlineData("Ooshak, stop.")]
    [InlineData("Ooshak, shut up")]
    public void Stop_commands_work(string transcript) =>
        Assert.IsType<RouteResult.StopSpeaking>(Create().Route(transcript, speaking: true));

    [Theory]
    [InlineData("Ooshak, call me boss.", "boss")]
    [InlineData("Ooshak, address me as Mister Stark", "Mister Stark")]
    public void Saying_how_to_be_addressed_is_recognized(string transcript, string expected) =>
        Assert.Equal(expected, Assert.IsType<RouteResult.SetAddress>(Create().Route(transcript)).Name);

    [Fact]
    public void Call_me_back_is_not_a_name()
    {
        var router = Create();
        router.Route("Ooshak, switch to Atlas");
        Assert.IsType<RouteResult.Send>(router.Route("Ooshak, call me back when the build is done"));
    }

    [Fact]
    public void Where_are_you_reports_the_session()
    {
        var router = Create();
        router.Route("Ooshak, switch to Atlas");
        Assert.Equal(Atlas, Assert.IsType<RouteResult.WhereAmI>(router.Route("Ooshak, where are you?")).Title);
    }

    [Fact]
    public void Turkish_verbs_are_not_stripped_from_english_messages()
    {
        var send = Assert.IsType<RouteResult.Send>(Create().Route("Ooshak, tell Atlas to ask and send"));
        Assert.Equal("ask and send", send.Text);
    }

    [Fact]
    public void Default_instruction_follows_the_language()
    {
        var config = new OoshakConfig { Language = L.English };
        Assert.Contains("in English", config.ReplyInstruction());

        config.Language = L.Turkish;
        Assert.Contains("Sesli mod", config.ReplyInstruction());
    }

    [Fact]
    public void A_customized_instruction_is_left_alone()
    {
        var config = new OoshakConfig { Language = L.English, VoiceReplyInstruction = "[Benim tarzım]" };
        Assert.Equal("[Benim tarzım]", config.ReplyInstruction());
    }

    [Fact]
    public void Every_translatable_text_has_an_english_translation()
    {
        var root = RepositoryRoot();
        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.xaml", SearchOption.AllDirectories).Where(IsSource))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{local:T '((?:[^'\\]|\\.)*)'\}"))
                missing.Add(match.Groups[1].Value.Replace("\\'", "'"));
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories).Where(IsSource))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\bL\.T\(""((?:[^""\\]|\\.)*)"""))
                missing.Add(match.Groups[1].Value);
        }

        Assert.NotEmpty(missing);
        Assert.Empty(missing.Where(text => !Translations.English.ContainsKey(text)).Distinct());
    }

    private static bool IsSource(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
