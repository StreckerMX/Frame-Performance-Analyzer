using System.Windows;
using FrameViewAnalyzer.App.Services;

namespace FrameViewAnalyzer.App.Tests;

public class WindowBoundsValidatorTests
{
    private static readonly Rect VirtualScreen = new(0, 0, 1920, 1080);

    [Fact]
    public void Fully_visible_window_is_usable() =>
        Assert.True(WindowBoundsValidator.IsUsable(new Rect(100, 100, 1200, 800), VirtualScreen));

    [Fact]
    public void Fully_off_screen_window_is_rejected() =>
        Assert.False(WindowBoundsValidator.IsUsable(new Rect(3000, 100, 1200, 800), VirtualScreen));

    [Fact]
    public void Partially_visible_window_is_usable() =>
        Assert.True(WindowBoundsValidator.IsUsable(new Rect(-50, 100, 1200, 800), VirtualScreen));

    [Fact]
    public void Tiny_sliver_is_rejected() =>
        Assert.False(WindowBoundsValidator.IsUsable(new Rect(-100, 100, 200, 800), VirtualScreen));

    [Theory]
    // WPF's Rect rejects negative dimensions in its constructor, so only
    // non-negative sizes are representable here; zero-sized windows must
    // be rejected regardless.
    [InlineData(0, 0)]
    [InlineData(0, 500)]
    [InlineData(500, 0)]
    public void Zero_sized_windows_are_rejected(double width, double height) =>
        Assert.False(WindowBoundsValidator.IsUsable(new Rect(0, 0, width, height), VirtualScreen));

    [Fact]
    public void Multi_monitor_virtual_screen_accepts_secondary_monitor_windows()
    {
        var virtualScreen = new Rect(-1920, 0, 3840, 1080);

        Assert.True(WindowBoundsValidator.IsUsable(new Rect(-1800, 100, 1200, 800), virtualScreen));
    }
}
