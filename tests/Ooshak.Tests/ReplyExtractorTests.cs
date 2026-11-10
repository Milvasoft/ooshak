using Ooshak.Core;

namespace Ooshak.Tests;

public sealed class ReplyExtractorTests : IDisposable
{
    private const string UserText = """{"type":"user","message":{"content":"yap"}}""";
    private const string Recap = """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"mcp__ccd_turn__wrap_up","input":{"recap":"Testler geçti."}}]}}""";
    private const string ToolResult = """{"type":"user","message":{"content":[{"type":"tool_result","content":"ok"}]}}""";
    private const string Text = """{"type":"assistant","message":{"content":[{"type":"text","text":"Bitti."}]}}""";

    private readonly string _path = Path.GetTempFileName();

    [Fact]
    public void Recap_of_the_latest_turn_is_found_past_tool_results()
    {
        File.WriteAllLines(_path, [UserText, Recap, ToolResult, Text]);
        Assert.Equal("Testler geçti.", ReplyExtractor.RecapFromTranscript(_path));
    }

    [Fact]
    public void A_recap_from_an_earlier_turn_is_not_read()
    {
        File.WriteAllLines(_path, [UserText, Recap, UserText, Text]);
        Assert.Null(ReplyExtractor.RecapFromTranscript(_path));
    }

    [Theory]
    [InlineData("Uzun rapor.\n\n🔊 Testleri bitirdim, hepsi geçti.", "Testleri bitirdim, hepsi geçti.")]
    [InlineData("🔊 ilk\nara metin\n🔊 son\nkuyruk", "son")]
    [InlineData("Rapor.\n**🔊 Kalın yazılmış.**", "Kalın yazılmış.")]
    public void The_spoken_line_is_taken_from_the_message(string message, string expected) =>
        Assert.Equal(expected, ReplyExtractor.SpokenLine(message));

    [Theory]
    [InlineData("Marker yok.")]
    [InlineData("Boş 🔊  ")]
    [InlineData("Cevabında 🔊 satırı olmaz, talimat tekrar gider.")]
    [InlineData(null)]
    public void No_spoken_line_returns_null(string? message) =>
        Assert.Null(ReplyExtractor.SpokenLine(message));

    [Fact]
    public void The_spoken_line_wins_over_the_recap()
    {
        File.WriteAllLines(_path, [UserText, Recap]);
        using var payload = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new
        {
            transcript_path = _path,
            last_assistant_message = "Rapor.\n🔊 Hallettim.",
        }));

        Assert.Equal("Hallettim.", ReplyExtractor.FromStopPayload(payload.RootElement));
    }

    [Fact]
    public void Markup_links_and_code_are_removed()
    {
        var cleaned = ReplyExtractor.Clean("**Tamam.** [dosya](src/a.cs) hazır.\n```cs\nvar x;\n```\nBkz https://x.dev/a sonra.");
        Assert.Equal("Tamam. dosya hazır. Bkz sonra.", cleaned);
    }

    [Fact]
    public void Long_text_is_cut_at_a_sentence_boundary()
    {
        var cleaned = ReplyExtractor.Clean(string.Concat(Enumerable.Repeat("Bu bir cümledir. ", 40)));
        Assert.True(cleaned.Length <= 320);
        Assert.EndsWith(".", cleaned);
    }

    public void Dispose() => File.Delete(_path);
}
