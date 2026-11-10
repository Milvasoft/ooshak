using System.Text.Json;
using Ooshak.Core;

namespace Ooshak.Tests;

public class PermissionBrokerTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_users_answer_is_returned(bool allow)
    {
        var broker = new PermissionBroker();
        var answer = broker.AskAsync("atlas", "gradle build", Long);

        broker.Decide(broker.Current!, allow);

        Assert.Equal(allow, await answer);
        Assert.Null(broker.Current);
    }

    [Fact]
    public async Task No_answer_in_time_returns_null_and_clears_the_question()
    {
        var broker = new PermissionBroker();

        Assert.Null(await broker.AskAsync("atlas", "gradle build", TimeSpan.FromMilliseconds(30)));
        Assert.Equal(0, broker.Count);
    }

    [Fact]
    public async Task Questions_are_shown_oldest_first()
    {
        var broker = new PermissionBroker();
        var first = broker.AskAsync("a", "one", Long);
        var second = broker.AskAsync("b", "two", Long);

        Assert.Equal("a", broker.Current!.Session);
        Assert.Equal(2, broker.Count);

        broker.Decide(broker.Current!, true);
        Assert.Equal("b", broker.Current!.Session);

        broker.Decide(broker.Current!, false);
        Assert.True(await first);
        Assert.False(await second);
    }

    [Fact]
    public async Task Release_hands_questions_back_to_the_app()
    {
        var broker = new PermissionBroker();
        var answer = broker.AskAsync("a", "one", Long);

        broker.Release();

        Assert.Null(await answer);
    }

    [Fact]
    public void A_late_click_changes_nothing()
    {
        var broker = new PermissionBroker();
        _ = broker.AskAsync("a", "one", Long);
        var request = broker.Current!;
        broker.Decide(request, false);

        broker.Decide(request, true);

        Assert.Equal(0, broker.Count);
    }

    [Fact]
    public void A_long_command_is_shown_whole()
    {
        var command = new string('x', 5000);
        using var input = JsonDocument.Parse(JsonSerializer.Serialize(new { command }));

        Assert.Equal(command, PermissionText.Detail("Bash", input.RootElement));
    }

    [Fact]
    public void File_tools_show_their_path()
    {
        using var input = JsonDocument.Parse("""{"file_path":"src/a.cs","content":"x"}""");
        Assert.Equal("Write · src/a.cs", PermissionText.Detail("Write", input.RootElement));
    }

    [Theory]
    [InlineData("AskUserQuestion")]
    [InlineData("ExitPlanMode")]
    public void Questions_stay_in_the_app(string tool) => Assert.True(PermissionText.NeedsTheApp(tool));

    [Fact]
    public void Allow_reply_has_the_documented_shape()
    {
        using var reply = JsonDocument.Parse(PermissionText.Reply(true, "no"));
        var output = reply.RootElement.GetProperty("hookSpecificOutput");

        Assert.Equal("PermissionRequest", output.GetProperty("hookEventName").GetString());
        Assert.Equal("allow", output.GetProperty("decision").GetProperty("behavior").GetString());
    }

    [Fact]
    public void Deny_reply_carries_the_message()
    {
        using var reply = JsonDocument.Parse(PermissionText.Reply(false, "Reddedildi"));
        var decision = reply.RootElement.GetProperty("hookSpecificOutput").GetProperty("decision");

        Assert.Equal("deny", decision.GetProperty("behavior").GetString());
        Assert.Equal("Reddedildi", decision.GetProperty("message").GetString());
    }
}
