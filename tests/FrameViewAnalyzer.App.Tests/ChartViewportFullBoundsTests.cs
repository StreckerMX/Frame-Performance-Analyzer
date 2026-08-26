using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for the canonical chart bounds: initial fit and Auto
/// Zoom / Reset Zoom must derive from the FULL-resolution series arrays, never
/// from the current viewport or decimated rendering data.
/// </summary>
public class ChartViewportFullBoundsTests
{
    private static MetricSeries Series(double[] xs, double[] ys, string? label = null) =>
        new(CoreMetricCatalog.CoreById["fps"], xs, ys, label);

    [Fact]
    public void Full_limits_span_the_entire_loaded_session_range()
    {
        var xs = Enumerable.Range(0, 151).Select(i => (double)i).ToArray();
        var ys = Enumerable.Range(0, 151).Select(i => 80.0 + (i % 10)).ToArray();
        var session = Series(xs, ys);

        // A ~150 s session must fit to the full 0..150 s range immediately,
        // not to a default -10..10 viewport.
        var limits = ChartViewport.FullSeriesLimits([session], fpsAdaptiveScale: true);

        Assert.NotNull(limits);
        Assert.Equal(0, limits.Value.Left, precision: 9);
        Assert.Equal(150, limits.Value.Right, precision: 9);
        Assert.True(limits.Value.Bottom > 0);
        Assert.True(limits.Value.Bottom <= 80);
        Assert.True(limits.Value.Top >= 89);
        Assert.True(limits.Value.VerticalSpan >= ChartViewport.MinimumFpsVerticalSpan);
    }

    [Fact]
    public void Full_limits_include_both_sessions_for_comparison()
    {
        var baseXs = Enumerable.Range(0, 151).Select(i => (double)i).ToArray();
        var baseYs = Enumerable.Range(0, 151).Select(i => 100.0).ToArray();
        var comparisonXs = Enumerable.Range(100, 161).Select(i => (double)i).ToArray();
        var comparisonYs = Enumerable.Range(0, 161).Select(i => 60.0).ToArray();

        var limits = ChartViewport.FullSeriesLimits(
            [Series(baseXs, baseYs), Series(comparisonXs, comparisonYs, "Comparison")],
            fpsAdaptiveScale: true);

        Assert.NotNull(limits);
        // X spans the union of both sessions.
        Assert.Equal(0, limits.Value.Left, precision: 9);
        Assert.Equal(260, limits.Value.Right, precision: 9);
        // Y covers the lowest comparison value and the highest base value
        // without wasting the chart on an unnecessary zero baseline.
        Assert.True(limits.Value.Bottom > 0);
        Assert.True(limits.Value.Bottom <= 60);
        Assert.True(limits.Value.Top >= 100);
    }

    [Fact]
    public void Full_limits_do_not_depend_on_any_viewport()
    {
        var xs = Enumerable.Range(0, 151).Select(i => (double)i).ToArray();
        var ys = Enumerable.Range(0, 151).Select(i => 90.0).ToArray();
        var session = Series(xs, ys);

        // The canonical bounds must be identical no matter what the current
        // visible range is — recovery after a 0..7 s zoom must land on the
        // full session.
        var full = ChartViewport.FullSeriesLimits([session], fpsAdaptiveScale: true);
        var afterNarrowView = ChartViewport.FullSeriesLimits([session], fpsAdaptiveScale: true);

        Assert.NotNull(full);
        Assert.NotNull(afterNarrowView);
        Assert.Equal(full.Value.Left, afterNarrowView.Value.Left, precision: 12);
        Assert.Equal(full.Value.Right, afterNarrowView.Value.Right, precision: 12);
        Assert.Equal(full.Value.Bottom, afterNarrowView.Value.Bottom, precision: 12);
        Assert.Equal(full.Value.Top, afterNarrowView.Value.Top, precision: 12);
        Assert.Equal(150, afterNarrowView.Value.Right, precision: 9);
    }

    [Fact]
    public void Full_limits_return_null_for_an_empty_series_list()
    {
        Assert.Null(ChartViewport.FullSeriesLimits([], fpsAdaptiveScale: false));
    }

    [Fact]
    public void Non_fps_metrics_pad_below_zero_baseline()
    {
        var metric = CoreMetricCatalog.CoreById["gpu0_util"];
        var series = new MetricSeries(
            metric,
            [0.0, 1.0, 2.0],
            [50.0, 60.0, 70.0]);

        var limits = ChartViewport.FullSeriesLimits([series], fpsAdaptiveScale: false);

        Assert.NotNull(limits);
        Assert.True(limits.Value.Bottom < 50);
        Assert.True(limits.Value.Top >= 70);
    }
}