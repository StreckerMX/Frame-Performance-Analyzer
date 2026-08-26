using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for adaptive FPS report axis limits: the final
/// ScottPlot limits of a built report panel must contain every full-resolution
/// FPS value from every included session without forcing a zero baseline.
/// </summary>
public class ReportFpsAxisTests
{
    private static readonly MetricDefinition Fps = CoreMetricCatalog.CoreById["fps"];

    private static MetricSeries FpsSeries(double min, double max, SessionRole role, string? label = null) =>
        new(
            Fps,
            [0.0, 1.0, 2.0, 3.0],
            [min, (min + max) / 2, max, (min + max) / 2],
            label,
            role);

    [Fact]
    public void Base_and_comparison_fps_panel_contains_the_full_union()
    {
        var group = new ReportPlotBuilder.ReportGroup(
            Fps,
            [
                FpsSeries(72, 143, SessionRole.Base),
                FpsSeries(71, 111, SessionRole.Comparison, "Comparison"),
            ]);
        var multiplot = ReportPlotBuilder.Build([group], ChartStyle.FromApplicationResources());

        var limits = multiplot.Subplots.GetPlot(0).Axes.GetLimits();

        Assert.True(limits.Bottom > 0, "Adaptive FPS reports should not force a zero baseline.");
        Assert.True(limits.Bottom <= 71, $"FPS minimum clipped: {limits.Bottom}");
        Assert.True(limits.Top > 143, $"FPS maximum clipped: {limits.Top}");
        Assert.True(limits.VerticalSpan >= ChartViewport.MinimumFpsVerticalSpan);
    }

    [Fact]
    public void Base_only_fps_panel_contains_the_full_series()
    {
        var group = new ReportPlotBuilder.ReportGroup(
            Fps,
            [FpsSeries(72, 143, SessionRole.Base)]);
        var multiplot = ReportPlotBuilder.Build([group], ChartStyle.FromApplicationResources());

        var limits = multiplot.Subplots.GetPlot(0).Axes.GetLimits();

        Assert.True(limits.Bottom > 0, "Adaptive FPS reports should not force a zero baseline.");
        Assert.True(limits.Bottom <= 72, $"FPS minimum clipped: {limits.Bottom}");
        Assert.True(limits.Top > 143, $"FPS maximum clipped: {limits.Top}");
        Assert.True(limits.VerticalSpan >= ChartViewport.MinimumFpsVerticalSpan);
    }

    [Fact]
    public void Comparison_only_fps_panel_contains_the_full_series()
    {
        var group = new ReportPlotBuilder.ReportGroup(
            Fps,
            [FpsSeries(71, 111, SessionRole.Comparison, "Comparison")]);
        var multiplot = ReportPlotBuilder.Build([group], ChartStyle.FromApplicationResources());

        var limits = multiplot.Subplots.GetPlot(0).Axes.GetLimits();

        Assert.True(limits.Bottom > 0, "Adaptive FPS reports should not force a zero baseline.");
        Assert.True(limits.Bottom <= 71, $"FPS minimum clipped: {limits.Bottom}");
        Assert.True(limits.Top > 111, $"FPS maximum clipped: {limits.Top}");
        Assert.True(limits.VerticalSpan >= ChartViewport.MinimumFpsVerticalSpan);
    }

    [Fact]
    public void Fps_title_remains_on_the_first_panel()
    {
        var group = new ReportPlotBuilder.ReportGroup(
            Fps,
            [FpsSeries(72, 143, SessionRole.Base)]);
        var multiplot = ReportPlotBuilder.Build([group], ChartStyle.FromApplicationResources());

        var plot = multiplot.Subplots.GetPlot(0);
        Assert.Equal("FPS (Calculated)", plot.Axes.Title.Label.Text);
    }
}
