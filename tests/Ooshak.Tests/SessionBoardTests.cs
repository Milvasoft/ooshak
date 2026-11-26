using System.Text.Json;
using Ooshak.Core;

namespace Ooshak.Tests;

public sealed class SessionBoardTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ooshak-board").FullName;
    private readonly List<SessionInfo> _catalog = [new("local_app1", "app-session", "Atlas", DateTimeOffset.UtcNow)];

    private SessionBoard NewBoard(string? path = null) => new(path, () => _catalog);

    private static JsonElement Payload(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void A_finished_turn_waits_with_its_reply()
    {
        var board = NewBoard();
        board.OnHook("stop", Payload(new { session_id = "app-session", cwd = "/repos/storefront", last_assistant_message = "Done.\n🔊 Tests pass." }));

        var item = Assert.Single(board.Items);
        Assert.Equal(WaitKind.Waiting, item.Kind);
        Assert.Equal("Atlas", item.Title);
        Assert.Equal("Tests pass.", item.Detail);
        Assert.Equal("storefront", item.Project);
        Assert.Equal("local_app1", item.LocalId);
        Assert.Equal("claude --resume app-session", item.ResumeCommand);
    }

    [Fact]
    public void A_typed_prompt_clears_the_session()
    {
        var board = NewBoard();
        board.OnHook("stop", Payload(new { session_id = "app-session", last_assistant_message = "Done." }));
        board.OnHook("userpromptsubmit", Payload(new { session_id = "app-session" }));

        Assert.Empty(board.Items);
    }

    [Fact]
    public void A_question_shows_what_it_asks()
    {
        var board = NewBoard();
        board.OnPermission(Payload(new
        {
            session_id = "app-session",
            tool_name = "AskUserQuestion",
            tool_input = new { questions = new[] { new { question = "Firebase or Auth0?" } } },
        }), null);

        var item = Assert.Single(board.Items);
        Assert.Equal(WaitKind.Question, item.Kind);
        Assert.Equal("Firebase or Auth0?", item.Detail);
    }

    [Fact]
    public void A_permission_stays_while_another_tool_finishes()
    {
        var board = NewBoard();
        board.OnPermission(Payload(new { session_id = "app-session", tool_name = "Bash", tool_input = new { command = "npm test" } }), null);
        board.OnHook("posttooluse", Payload(new { session_id = "app-session", tool_name = "Read" }));

        Assert.Equal(WaitKind.Permission, Assert.Single(board.Items).Kind);

        board.OnHook("posttooluse", Payload(new { session_id = "app-session", tool_name = "Bash" }));
        Assert.Empty(board.Items);
    }

    [Fact]
    public void An_answered_popup_removes_the_permission_and_a_timeout_keeps_it()
    {
        var board = NewBoard();
        var permission = Payload(new { session_id = "app-session", tool_name = "Bash", tool_input = new { command = "rm -rf build" } });

        board.OnPermission(permission, new PermissionRequest("Atlas", "rm -rf build"));
        board.Answered("app-session", null);
        var kept = Assert.Single(board.Items);
        Assert.Null(kept.Request);

        board.Answered("app-session", true);
        Assert.Empty(board.Items);
    }

    [Fact]
    public void Permissions_come_before_questions_and_finished_turns()
    {
        var board = NewBoard();
        board.OnHook("stop", Payload(new { session_id = "a", last_assistant_message = "Done." }));
        board.OnPermission(Payload(new { session_id = "b", tool_name = "AskUserQuestion", tool_input = new { } }), null);
        board.OnPermission(Payload(new { session_id = "c", tool_name = "Bash", tool_input = new { command = "ls" } }), null);

        Assert.Equal([WaitKind.Permission, WaitKind.Question, WaitKind.Waiting], board.Items.Select(i => i.Kind));
    }

    [Fact]
    public void Idle_reminders_add_nothing_but_approvals_do()
    {
        var board = NewBoard();
        board.OnHook("notification", Payload(new { session_id = "a", message = "Claude is waiting for your input" }));
        Assert.Empty(board.Items);

        board.OnHook("notification", Payload(new { session_id = "a", message = "Claude needs your permission to use Bash" }));
        Assert.Equal(WaitKind.Permission, Assert.Single(board.Items).Kind);
    }

    [Fact]
    public void A_terminal_session_is_named_after_its_first_prompt()
    {
        var transcript = Path.Combine(_directory, "t.jsonl");
        File.WriteAllLines(transcript,
        [
            """{"type":"summary","summary":"x"}""",
            """{"type":"user","message":{"role":"user","content":"<command-name>/clear</command-name>"}}""",
            """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"Add product search to the store"}]}}""",
        ]);

        var board = NewBoard();
        board.OnHook("stop", Payload(new { session_id = "term-1", transcript_path = transcript, last_assistant_message = "Done." }));

        var item = Assert.Single(board.Items);
        Assert.Equal("Add product search to the store", item.Title);
        Assert.False(item.InApp);
    }

    [Fact]
    public void The_list_survives_a_restart_without_stale_permissions()
    {
        var path = Path.Combine(_directory, "waiting.json");
        var board = NewBoard(path);
        board.OnHook("stop", Payload(new { session_id = "a", last_assistant_message = "Done." }));
        board.OnPermission(Payload(new { session_id = "b", tool_name = "Bash", tool_input = new { command = "ls" } }), null);

        var reloaded = NewBoard(path);

        var item = Assert.Single(reloaded.Items);
        Assert.Equal("a", item.SessionId);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
