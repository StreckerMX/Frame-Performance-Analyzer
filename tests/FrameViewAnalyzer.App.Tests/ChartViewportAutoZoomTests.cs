using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Multi-series Auto Zoom regression tests: bounds must come from every
/// plotted series, full-resolution only, ignoring series with no visible
/// points and preserving the X range.
/// </summary>
public class ChartViewportAutoZoomTests
{
    private static MetricSeries Series(double[] xs, double[] ys, string? label = null) =>
        new(CoreMetricCatalog.CoreById["fps"], xs, ys, label);

    private static readonly MetricSeries Base = Series(
        [0.0, 1.0, 2.0, 3.0, 4.0],
        [100.0, 100.0, 100.0, 100.0, 100.0]);

    private static readonly MetricSeries Comparison = Series(
        [0.0, 1.0, 2.0, 3.0, 4.0],
        [80.0, 80.0, 80.0, 80.0, 80.0],
        "Comparison");

    private static readonly AxisLimits View = new(0, 4, 0, 200);

    [Fact]
    public void Bounds_cover_every_visible_series()
    {
        var fitted = ChartViewport.AutoZoomToSeries(View, [Base, Comparison], fpsBaselineZero: false);

        Assert.NotNull(fitted);
        Assert.Equal(0, fitted.Value.Left, precision: 9);
        Assert.Equal(4, fitted.Value.Right, precision: 9);
        Assert.True(fitted.Value.Bottom <= 80);
        Assert.True(fitted.Value.Top >= 100);
    }

    [Fact]
    public void Much_higher_comparison_stretches_the_top()
    {
        var higher = Series([0.0, 1.0, 2.0, 3.0, 4.0], [400.0, 400.0, 400.0, 400.0, 400.0], "Comparison");

        var fitted = ChartViewport.AutoZoomToSeries(View, [Base, higher], fpsBaselineZero: false);

        Assert.NotNull(fitted);
        Assert.True(fitted.Value.Top >= 400);
    }

    [Fact]
    public void Much_lower_comparison_stretches_the_bottom()
    {
        var lower = Series([0.0, 1.0, 2.0, 3.0, 4.0], [10.0, 10.0, 10.0, 10.0, 10.0], "Comparison");

        var fitted = ChartViewport.AutoZoomToSeries(View, [Base, lower], fpsBaselineZero: false);

        Assert.NotNull(fitted);
        Assert.True(fitted.Value.Bottom <= 10);
    }

    [Fact]
    public void Only_base_values_in_range_use_base_bounds()
    {
        var farComparison = Series([50.0, 51.0, 52.0], [10.0, 10.0, 10.0], "Comparison");

        var fitted = ChartViewport.AutoZoomToSeries(View, [Base, farComparison], fpsBaselineZero: false);

        Assert.NotNull(fitted);
        Assert.True(fitted.Value.Bottom <= 100);
        Assert.True(fitted.Value.Top >= 100);
    }

    [Fact]
    public void Only_comparison_values_in_range_use_comparison_bounds()
    {
        var farBase = Series([50.0, 51.0, 52.0], [100.0, 100.0, 100.0]);

        var fitted = ChartViewport.AutoZoomToSeries(View, [farBase, Comparison], fpsBaselineZero: false);

        Assert.NotNull(fitted);
        Assert.True(fitted.Value.Bottom <= 80);
        Assert.True(fitted.Value.Top >= 80);
    }

    [Fact]
    public void Nothing_in_range_returns_null()
    {
        var farBase = Series([50.0, 51.0], [100.0, 100.0]);
        var farComparison = Series([60.0, 61.0], [50.0, 50.0], "Comparison");

        Assert.Null(ChartViewport.AutoZoomToSeries(View, [farBase, farComparison], fpsBaselineZero: false));
    }

    [Fact]
    public void Fps_keeps_the_zero_baseline()
    {
        var fitted = ChartViewport.AutoZoomToSeries(View, [Base, Comparison], fpsBaselineZero: true);

        Assert.NotNull(fitted);
        Assert.Equal(0, fitted.Value.Bottom, precision: 9);
        Assert.True(fitted.Value.Top >= 100);
    }

    [Fact]
    public void Empty_series_list_returns_null()
    {
        Assert.Null(ChartViewport.AutoZoomToSeries(View, [], fpsBaselineZero: false));
    }
}
