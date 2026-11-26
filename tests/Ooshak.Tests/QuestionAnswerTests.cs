using System.Text.Json;
using Ooshak.Core;

namespace Ooshak.Tests;

public class QuestionAnswerTests
{
    private static readonly JsonElement Input = JsonSerializer.SerializeToElement(new
    {
        questions = new object[]
        {
            new
            {
                question = "Which framework?",
                header = "Framework",
                options = new[] { new { label = "React", description = "Component library" }, new { label = "Vue", description = "Progressive" } },
                multiSelect = false,
            },
            new
            {
                question = "Which extras?",
                header = "Extras",
                options = new[] { new { label = "Tests", description = "" }, new { label = "Docs", description = "" } },
                multiSelect = true,
            },
        },
    });

    [Fact]
    public void Questions_are_read_with_their_options()
    {
        var questions = PermissionText.Questions(Input);

        Assert.Equal(2, questions.Count);
        Assert.Equal("Which framework?", questions[0].Question);
        Assert.Equal(["React", "Vue"], questions[0].Options.Select(o => o.Label));
        Assert.Equal("Component library", questions[0].Options[0].Description);
        Assert.False(questions[0].MultiSelect);
        Assert.True(questions[1].MultiSelect);
    }

    [Fact]
    public void A_question_without_options_cannot_be_answered_here() =>
        Assert.Empty(PermissionText.Questions(JsonSerializer.SerializeToElement(new { questions = new[] { new { question = "Free text?" } } })));

    [Fact]
    public void The_reply_allows_the_tool_and_keeps_the_questions()
    {
        var reply = PermissionText.AnswerReply(Input, new Dictionary<string, string>
        {
            ["Which framework?"] = "React",
            ["Which extras?"] = "Tests, Docs",
        });

        using var json = JsonDocument.Parse(reply);
        var decision = json.RootElement.GetProperty("hookSpecificOutput").GetProperty("decision");
        Assert.Equal("allow", decision.GetProperty("behavior").GetString());

        var updated = decision.GetProperty("updatedInput");
        Assert.Equal(2, updated.GetProperty("questions").GetArrayLength());
        Assert.Equal("React", updated.GetProperty("answers").GetProperty("Which framework?").GetString());
        Assert.Equal("Tests, Docs", updated.GetProperty("answers").GetProperty("Which extras?").GetString());
    }

    [Fact]
    public async Task Answering_returns_allowed_with_the_answers()
    {
        var broker = new PermissionBroker();
        PermissionRequest? request = null;
        var result = broker.AskAsync("atlas", "Which framework?", TimeSpan.FromSeconds(30), r => request = r, PermissionText.Questions(Input));

        Assert.True(request!.IsQuestion);
        broker.AnswerQuestions(request, new Dictionary<string, string> { ["Which framework?"] = "Vue" });

        Assert.True(await result);
        Assert.Equal("Vue", request.Answers!["Which framework?"]);
    }

    [Fact]
    public async Task Handing_off_leaves_the_question_to_claude()
    {
        var broker = new PermissionBroker();
        var result = broker.AskAsync("atlas", "Which framework?", TimeSpan.FromSeconds(30), null, PermissionText.Questions(Input));

        broker.HandOff(broker.Current!);

        Assert.Null(await result);
        Assert.Equal(0, broker.Count);
    }
}
