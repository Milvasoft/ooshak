using Ooshak.Core;

namespace Ooshak.Tests;

public class FullscreenDetectorTests
{
    private static readonly (int, int, int, int) Monitor = (0, 0, 1920, 1080);

    [Fact]
    public void A_window_filling_the_monitor_counts() =>
        Assert.True(FullscreenDetector.Covers((0, 0, 1920, 1080), Monitor));

    [Fact]
    public void A_window_larger_than_the_monitor_counts() =>
        Assert.True(FullscreenDetector.Covers((-8, -8, 1928, 1088), Monitor));

    [Fact]
    public void A_maximized_window_above_the_taskbar_does_not_count() =>
        Assert.False(FullscreenDetector.Covers((0, 0, 1920, 1032), Monitor));

    [Fact]
    public void A_small_window_does_not_count() =>
        Assert.False(FullscreenDetector.Covers((100, 100, 900, 700), Monitor));
}
