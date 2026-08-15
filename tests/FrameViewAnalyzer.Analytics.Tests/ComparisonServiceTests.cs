using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

public class ComparisonServiceTests
{
    private readonly CaptureAnalysisService _analysis = new();
    private readonly ComparisonService _comparison = new();

    private SessionAnalysis SessionWithFrametime(double frameTimeMs)
    {
        var frameTime = frameTimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return _analysis.Analyze(
            TestCapture.CaptureWith(
                ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
                [
                    ["0.0", frameTime, "80.0"],
                    ["0.25", frameTime, "80.0"],
                    ["0.5", frameTime, "80.0"],
                ]),
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));
    }

    [Fact]
    public void Compare_builds_base_and_delta_rows_for_every_metric()
    {
        var baseSession = SessionWithFrametime(10.0);   // 100 FPS
        var comparison = SessionWithFrametime(20.0);    // 50 FPS

        var rows = _comparison.Compare(baseSession, comparison);

        Assert.NotEmpty(rows);
        var fpsAvg = rows.Single(row => row.MetricId == "fps" && row.StatisticKey == "avg");
        Assert.Equal(100.0, fpsAvg.BaseValue);
        Assert.Equal(50.0, fpsAvg.ComparisonValue);
        Assert.Equal(-50.0, fpsAvg.Delta);
        Assert.Equal(-50.0, fpsAvg.DeltaPercent);
        Assert.Equal(ImprovementKind.Regression, fpsAvg.Kind);
    }

    [Fact]
    public void Lower_is_better_metrics_improve_when_values_drop()
    {
        var baseSession = SessionWithFrametime(20.0);
        var comparison = SessionWithFrametime(10.0);

        var rows = _comparison.Compare(baseSession, comparison);
        var frameTimeAvg = rows.Single(row => row.MetricId == "frametime" && row.StatisticKey == "avg");

        Assert.Equal(ImprovementKind.Improvement, frameTimeAvg.Kind);
    }

    [Fact]
    public void Missing_comparison_leaves_delta_and_kind_empty()
    {
        var rows = _comparison.Compare(SessionWithFrametime(10.0));

        Assert.All(
            rows,
            row =>
            {
                Assert.Null(row.ComparisonValue);
                Assert.Null(row.Delta);
                Assert.Null(row.DeltaPercent);
                Assert.Equal(ImprovementKind.None, row.Kind);
            });
    }

    [Fact]
    public void Zero_base_reports_delta_without_percent()
    {
        var (delta, deltaPercent) = ComparisonService.ComputeDelta(0.0, 5.0);

        Assert.Equal(5.0, delta);
        Assert.Null(deltaPercent);
    }

    [Fact]
    public void Metric_union_includes_comparison_only_metrics()
    {
        var baseCapture = TestCapture.CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            [["0.0", "10.0", "80.0"]]);
        var comparisonCapture = TestCapture.CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)", "GPU0Temp(C)"],
            [["0.0", "10.0", "80.0", "61.0"]]);
        var baseSession = _analysis.Analyze(baseCapture);
        var comparisonSession = _analysis.Analyze(comparisonCapture);

        var rows = _comparison.Compare(baseSession, comparisonSession);

        Assert.Contains(rows, row => row.MetricId == "gpu0_temp");
    }

    [Fact]
    public void Fps_statistics_use_the_low_tail()
    {
        var stats = StatisticsCalculator.Compute(
            CoreMetricCatalog.CoreById["fps"],
            [60.0, 61.0, 62.0, 63.0, 64.0, 65.0]);

        Assert.Equal(60.05, stats.P1!.Value, precision: 6);
        Assert.Equal(60.005, stats.P01!.Value, precision: 6);
    }

    [Fact]
    public void Multiplier_metrics_skip_percentiles()
    {
        var stats = StatisticsCalculator.Compute(
            CoreMetricCatalog.CoreById["fg_multiplier"],
            [1.0, 2.0, 4.0]);

        Assert.Null(stats.P1);
        Assert.Null(stats.P01);
        Assert.Equal(1.0, stats.Min);
        Assert.Equal(4.0, stats.Max);
    }

    [Fact]
    public void Empty_values_yield_null_statistics()
    {
        var stats = StatisticsCalculator.Compute(CoreMetricCatalog.CoreById["fps"], []);

        Assert.Null(stats.Avg);
        Assert.Null(stats.Min);
        Assert.Null(stats.Max);
        Assert.Null(stats.P1);
        Assert.Null(stats.P01);
    }
}
