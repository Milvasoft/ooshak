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
    public void A_maximized_window_covering_an_auto_hidden_taskbar_is_not_full_screen() =>
        Assert.False(FullscreenDetector.LooksFullscreen(coversMonitor: true, maximized: true, hasTitleBar: true));

    [Fact]
    public void A_covering_window_with_a_title_bar_is_not_full_screen() =>
        Assert.False(FullscreenDetector.LooksFullscreen(coversMonitor: true, maximized: false, hasTitleBar: true));

    [Fact]
    public void A_borderless_covering_window_is_full_screen() =>
        Assert.True(FullscreenDetector.LooksFullscreen(coversMonitor: true, maximized: false, hasTitleBar: false));

    [Fact]
    public void A_borderless_window_that_does_not_cover_is_not_full_screen() =>
        Assert.False(FullscreenDetector.LooksFullscreen(coversMonitor: false, maximized: false, hasTitleBar: false));

    [Fact]
    public void A_small_window_does_not_count() =>
        Assert.False(FullscreenDetector.Covers((100, 100, 900, 700), Monitor));
}
