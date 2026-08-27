using FrameViewAnalyzer.App.Views;

namespace FrameViewAnalyzer.App.Tests;

public class FramePointViewportTests
{
    [Fact]
    public void Visible_slice_returns_only_points_inside_the_viewport()
    {
        var xs = Enumerable.Range(0, 101).Select(index => index / 10.0).ToArray();
        var ys = xs.Select(value => 100.0 + value).ToArray();

        var (visibleX, visibleY) = SessionChartView.VisibleSlice(xs, ys, 2.25, 4.75);

        Assert.NotEmpty(visibleX);
        Assert.Equal(visibleX.Length, visibleY.Length);
        Assert.All(visibleX, value => Assert.InRange(value, 2.25, 4.75));
        Assert.Equal(2.3, visibleX[0], precision: 8);
        Assert.Equal(4.7, visibleX[^1], precision: 8);
    }

    [Fact]
    public void Visible_slice_is_empty_when_the_viewport_misses_the_series()
    {
        var (visibleX, visibleY) = SessionChartView.VisibleSlice(
            [0.0, 1.0, 2.0],
            [10.0, 11.0, 12.0],
            5.0,
            6.0);

        Assert.Empty(visibleX);
        Assert.Empty(visibleY);
    }

    [Fact]
    public void Frame_markers_appear_only_when_viewport_density_is_low_enough()
    {
        Assert.Equal(0f, SessionChartView.FrameMarkerSize(5_000, 1_000));
        Assert.True(SessionChartView.FrameMarkerSize(800, 1_000) > 0);
    }
}
