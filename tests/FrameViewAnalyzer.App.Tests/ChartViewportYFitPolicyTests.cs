using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for the adaptive Y-axis policy: padding must scale with
/// the DATA SPREAD so narrow-band metrics stay visually meaningful, while FPS
/// keeps enough vertical context to avoid exaggerating tiny differences.
/// </summary>
public class ChartViewportYFitPolicyTests
{
    private static readonly AxisLimits X = new(0, 10, 0, 1);

    [Fact]
    public void Sub_unit_range_gets_tight_limits()
    {
        // Time in Present API: ~0.278..0.394 ms.
        var fitted = ChartViewport.FitY(X, minY: 0.278, maxY: 0.394, fpsAdaptiveScale: false);

        Assert.True(fitted.Bottom >= 0.26, $"Bottom too low: {fitted.Bottom}");
        Assert.True(fitted.Bottom <= 0.278);
        Assert.True(fitted.Top <= 0.42, $"Top too high: {fitted.Top}");
        Assert.True(fitted.Top >= 0.394);
        Assert.True(fitted.VerticalSpan < 0.2, $"Span too large: {fitted.VerticalSpan}");
    }

    [Fact]
    public void Large_magnitude_narrow_band_uses_spread_based_padding()
    {
        // GPU0 Clock: ~2787.5..2842 MHz (spread ~54.5).
        var fitted = ChartViewport.FitY(X, minY: 2787.5, maxY: 2842.0, fpsAdaptiveScale: false);

        var spread = 2842.0 - 2787.5;
        var expectedSpan = spread * (1.0 + ChartViewport.LowerPaddingFraction + ChartViewport.UpperPaddingFraction);
        Assert.True(fitted.VerticalSpan < expectedSpan * 1.5,
            $"Span suggests magnitude-based padding: {fitted.VerticalSpan}");
        Assert.True(fitted.VerticalSpan < 200, $"Span too large: {fitted.VerticalSpan}");
        Assert.True(fitted.Bottom < 2787.5);
        Assert.True(fitted.Top > 2842.0);
    }

    [Fact]
    public void Normal_range_gets_reasonable_padding()
    {
        var fitted = ChartViewport.FitY(X, minY: 20, maxY: 40, fpsAdaptiveScale: false);

        Assert.True(fitted.Bottom < 20);
        Assert.True(fitted.Bottom >= 18);
        Assert.True(fitted.Top > 40);
        Assert.True(fitted.Top <= 43);
    }

    [Fact]
    public void Constant_non_zero_series_gets_a_finite_range()
    {
        var fitted = ChartViewport.FitY(X, minY: 14001, maxY: 14001, fpsAdaptiveScale: false);

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
        var fitted = ChartViewport.FitY(X, minY: 0, maxY: 0, fpsAdaptiveScale: false);

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

        var limits = ChartViewport.FullSeriesLimits([baseSeries, comparisonSeries], fpsAdaptiveScale: false);

        Assert.NotNull(limits);
        Assert.True(limits.Value.Bottom <= 0.27);
        Assert.True(limits.Value.Top >= 0.39);
    }

    [Fact]
    public void Fps_uses_a_non_zero_adaptive_baseline()
    {
        var fitted = ChartViewport.FitY(X, minY: 60, maxY: 90, fpsAdaptiveScale: true);

        Assert.True(fitted.Bottom > 0);
        Assert.True(fitted.Bottom <= 60);
        Assert.True(fitted.Top > 90);
        Assert.True(fitted.VerticalSpan >= ChartViewport.MinimumFpsVerticalSpan);
    }

    [Fact]
    public void Narrow_fps_band_keeps_minimum_visual_context()
    {
        var fitted = ChartViewport.FitY(X, minY: 140, maxY: 145, fpsAdaptiveScale: true);

        Assert.True(fitted.Bottom > 0);
        Assert.True(fitted.Bottom <= 140);
        Assert.True(fitted.Top >= 145);
        Assert.True(fitted.VerticalSpan >= ChartViewport.MinimumFpsVerticalSpan);
        Assert.True(fitted.VerticalSpan <= 40, $"FPS span too loose: {fitted.VerticalSpan}");
    }

    [Fact]
    public void Fps_limits_round_outward_to_five_fps_steps()
    {
        var fitted = ChartViewport.FitY(X, minY: 125, maxY: 172, fpsAdaptiveScale: true);

        Assert.Equal(0, fitted.Bottom % ChartViewport.FpsAxisStep, precision: 9);
        Assert.Equal(0, fitted.Top % ChartViewport.FpsAxisStep, precision: 9);
        Assert.True(fitted.Bottom <= 125);
        Assert.True(fitted.Top > 172);
    }
}