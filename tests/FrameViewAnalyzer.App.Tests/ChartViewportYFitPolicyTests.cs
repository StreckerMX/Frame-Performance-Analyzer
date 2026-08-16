using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for the adaptive Y-axis policy: padding must scale with
/// the DATA SPREAD — not with absolute magnitude floors — so tiny-unit and
/// large-magnitude narrow-band metrics stay visually meaningful.
/// </summary>
public class ChartViewportYFitPolicyTests
{
    private static readonly AxisLimits X = new(0, 10, 0, 1);

    [Fact]
    public void Sub_unit_range_gets_tight_limits()
    {
        // Time in Present API: ~0.278..0.394 ms.
        var fitted = ChartViewport.FitY(X, minY: 0.278, maxY: 0.394, fpsBaselineZero: false);

        Assert.True(fitted.Bottom >= 0.26, $"Bottom too low: {fitted.Bottom}");
        Assert.True(fitted.Bottom <= 0.278);
        Assert.True(fitted.Top <= 0.42, $"Top too high: {fitted.Top}");
        Assert.True(fitted.Top >= 0.394);
        // The whole span stays in the data band scale, nowhere near 1.6 units.
        Assert.True(fitted.VerticalSpan < 0.2, $"Span too large: {fitted.VerticalSpan}");
    }

    [Fact]
    public void Large_magnitude_narrow_band_uses_spread_based_padding()
    {
        // GPU0 Clock: ~2787.5..2842 MHz (spread ~54.5).
        var fitted = ChartViewport.FitY(X, minY: 2787.5, maxY: 2842.0, fpsBaselineZero: false);

        var spread = 2842.0 - 2787.5;
        var expectedSpan = spread * (1.0 + ChartViewport.LowerPaddingFraction + ChartViewport.UpperPaddingFraction);
        // Padding derives from the spread, not from the ~2800 magnitude.
        Assert.True(fitted.VerticalSpan < expectedSpan * 1.5,
            $"Span suggests magnitude-based padding: {fitted.VerticalSpan}");
        Assert.True(fitted.VerticalSpan < 200, $"Span too large: {fitted.VerticalSpan}");
        Assert.True(fitted.Bottom < 2787.5);
        Assert.True(fitted.Top > 2842.0);
    }

    [Fact]
    public void Normal_range_gets_reasonable_padding()
    {
        var fitted = ChartViewport.FitY(X, minY: 20, maxY: 40, fpsBaselineZero: false);

        Assert.True(fitted.Bottom < 20);
        Assert.True(fitted.Bottom >= 18);
        Assert.True(fitted.Top > 40);
        Assert.True(fitted.Top <= 43);
    }

    [Fact]
    public void Constant_non_zero_series_gets_a_finite_range()
    {
        var fitted = ChartViewport.FitY(X, minY: 14001, maxY: 14001, fpsBaselineZero: false);

        Assert.True(double.IsFinite(fitted.Bottom));
        Assert.True(double.IsFinite(fitted.Top));
        Assert.True(fitted.VerticalSpan > 0);
        Assert.True(fitted.VerticalSpan < 3000, $"Span too large: {fitted.VerticalSpan}");
        Assert.True(fitted.Bottom < 14001);
        Assert.True(fitted.Top > 14001);
    }

    [Fact]
    public void Constant_zero_series_gets_a_small_symmetric_range()
    {
        var fitted = ChartViewport.FitY(X, minY: 0, maxY: 0, fpsBaselineZero: false);

        Assert.Equal(-ChartViewport.ZeroSeriesPadding, fitted.Bottom, precision: 9);
        Assert.Equal(ChartViewport.ZeroSeriesPadding, fitted.Top, precision: 9);
    }

    [Fact]
    public void Base_and_comparison_union_covers_both_bands()
    {
        var baseSeries = new MetricSeries(
            CoreMetricCatalog.CoreById["in_present_api"],
            [0.0, 1.0],
            [0.30, 0.39]);
        var comparisonSeries = new MetricSeries(
            CoreMetricCatalog.CoreById["in_present_api"],
            [0.0, 1.0],
            [0.27, 0.35],
            "Comparison",
            SessionRole.Comparison);

        var limits = ChartViewport.FullSeriesLimits([baseSeries, comparisonSeries], fpsBaselineZero: false);

        Assert.NotNull(limits);
        // The shared band includes the comparison minimum and the base maximum.
        Assert.True(limits.Value.Bottom <= 0.27);
        Assert.True(limits.Value.Top >= 0.39);
    }

    [Fact]
    public void Fps_keeps_the_zero_baseline_with_the_new_policy()
    {
        var fitted = ChartViewport.FitY(X, minY: 60, maxY: 90, fpsBaselineZero: true);

        Assert.Equal(0, fitted.Bottom, precision: 9);
        Assert.True(fitted.Top > 90);
    }
}
