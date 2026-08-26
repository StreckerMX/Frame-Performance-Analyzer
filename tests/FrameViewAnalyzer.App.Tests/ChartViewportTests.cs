using FrameViewAnalyzer.App.Charting;
using ScottPlot;

namespace FrameViewAnalyzer.App.Tests;

public class ChartViewportTests
{
    private static readonly AxisLimits Full = new(0, 100, 0, 200);
    private static readonly AxisLimits Mid = new(40, 60, 0, 200);

    [Fact]
    public void Zoom_in_shrinks_the_span_around_the_cursor()
    {
        var next = ChartViewport.ZoomAt(Mid, anchorX: 50, scale: 0.5, Full);

        Assert.Equal(10, next.HorizontalSpan, precision: 9);
        Assert.Equal(50, next.HorizontalCenter, precision: 9);
    }

    [Fact]
    public void Zoom_never_drops_below_the_minimum_span()
    {
        var narrow = new AxisLimits(49, 51, 0, 200);

        var next = ChartViewport.ZoomAt(narrow, anchorX: 50, scale: 0.1, Full);

        Assert.True(next.HorizontalSpan >= ChartViewport.MinimumSpanSeconds - 1e-9);
    }

    [Fact]
    public void Zoom_out_clamps_to_the_full_range()
    {
        var next = ChartViewport.ZoomAt(Mid, anchorX: 50, scale: 10, Full);

        Assert.Equal(Full.Left, next.Left, precision: 9);
        Assert.Equal(Full.Right, next.Right, precision: 9);
    }

    [Fact]
    public void Zoom_at_the_left_edge_stays_anchored_and_clamped()
    {
        var next = ChartViewport.ZoomAt(new AxisLimits(0, 20, 0, 200), anchorX: 0, scale: 0.5, Full);

        Assert.Equal(0, next.Left, precision: 9);
        Assert.Equal(10, next.HorizontalSpan, precision: 9);
    }

    [Fact]
    public void Pan_shift_is_clamped_to_the_full_range()
    {
        var next = ChartViewport.PanTo(Mid, Full, deltaX: -100);

        Assert.Equal(Full.Left, next.Left, precision: 9);
        Assert.Equal(20, next.HorizontalSpan, precision: 9);
    }

    [Fact]
    public void Pan_inside_the_range_moves_the_window()
    {
        var next = ChartViewport.PanTo(Mid, Full, deltaX: 10);

        Assert.Equal(50, next.Left, precision: 9);
        Assert.Equal(70, next.Right, precision: 9);
    }

    [Fact]
    public void FitY_uses_adaptive_scale_for_fps()
    {
        var fitted = ChartViewport.FitY(new AxisLimits(0, 10, 0, 100), minY: 60, maxY: 90, fpsAdaptiveScale: true);

        Assert.True(fitted.Bottom > 0);
        Assert.True(fitted.Bottom <= 60);
        Assert.True(fitted.Top > 90);
        Assert.True(fitted.VerticalSpan >= ChartViewport.MinimumFpsVerticalSpan);
    }

    [Fact]
    public void FitY_pads_non_fps_metrics_above_and_below()
    {
        var fitted = ChartViewport.FitY(new AxisLimits(0, 10, 0, 100), minY: 60, maxY: 90, fpsAdaptiveScale: false);

        Assert.True(fitted.Bottom < 60);
        Assert.True(fitted.Top > 90);
    }
}