using Ooshak.Core;

namespace Ooshak.Tests;

public class UtteranceSegmenterTests
{
    private static readonly float[] Frame = new float[SileroVad.FrameSamples];

    private static float[]? Feed(UtteranceSegmenter segmenter, int frames, float probability)
    {
        float[]? result = null;
        for (var i = 0; i < frames && result is null; i++)
            result = segmenter.Feed(Frame, probability);
        return result;
    }

    [Fact]
    public void Speech_followed_by_silence_yields_an_utterance()
    {
        var segmenter = new UtteranceSegmenter(320);
        Assert.Null(Feed(segmenter, 20, 0.9f));
        Assert.NotNull(Feed(segmenter, 10, 0.1f));
    }

    [Fact]
    public void A_short_click_is_dropped()
    {
        var segmenter = new UtteranceSegmenter(320);
        Assert.Null(Feed(segmenter, 3, 0.9f));
        Assert.Null(Feed(segmenter, 20, 0.1f));
    }

    [Fact]
    public void Silence_alone_yields_nothing() =>
        Assert.Null(Feed(new UtteranceSegmenter(320), 200, 0.01f));

    [Fact]
    public void Endless_speech_is_cut_at_the_limit() =>
        Assert.NotNull(Feed(new UtteranceSegmenter(320), 2000, 0.9f));
}
