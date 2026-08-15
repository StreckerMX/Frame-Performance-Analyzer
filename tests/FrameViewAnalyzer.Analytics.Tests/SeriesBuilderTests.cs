using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

public class SeriesBuilderTests
{
    private readonly CaptureAnalysisService _service = new();

    [Fact]
    public void Frametime_series_averages_each_valid_bin()
    {
        var session = _service.Analyze(
            TestCapture.MakeSession(seconds: 4),
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));

        var series = SeriesBuilder.Build(session, "frametime");

        Assert.Equal(4, series.X.Length);
        Assert.All(series.Y, value => Assert.Equal(10.0, value));
        Assert.Equal(0.0, series.X[0]);
    }

    [Fact]
    public void Series_x_is_relative_to_the_active_window_start()
    {
        var session = _service.Analyze(
            TestCapture.MakeSession(seconds: 8),
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 1, AutoGpuThreshold: false));

        var series = SeriesBuilder.Build(session, "fps");

        Assert.Equal(6, series.X.Length);
        Assert.Equal(0.0, series.X[0]);
        Assert.Equal(5.0, series.X[^1]);
    }

    [Fact]
    public void Missing_metric_produces_an_empty_series()
    {
        var session = _service.Analyze(TestCapture.MakeSession(seconds: 4));

        var series = SeriesBuilder.Build(session, "not_a_metric");

        Assert.Empty(series.X);
        Assert.Empty(series.Y);
    }

    [Fact]
    public void Bins_with_fewer_than_three_values_are_skipped()
    {
        var capture = TestCapture.CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)", "GPU0Temp(C)"],
            [
                ["0.0", "10.0", "80.0", "60.0"],
                ["0.2", "10.0", "80.0", "61.0"],
                ["0.4", "10.0", "80.0", "62.0"],
                ["1.0", "10.0", "80.0", "60.0"],
                ["1.2", "10.0", "80.0", "61.0"],
                ["1.4", "10.0", "80.0", "62.0"],
                ["2.0", "10.0", "80.0", "60.0"],
                ["2.2", "10.0", "80.0", "61.0"],
            ]);
        var session = _service.Analyze(
            capture,
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));

        var series = SeriesBuilder.Build(session, "gpu0_temp");

        Assert.Equal(2, series.X.Length);
        Assert.Equal(0.0, series.X[0]);
        Assert.Equal(1.0, series.X[1]);
    }

    [Fact]
    public void Fps_series_uses_bin_frame_counts_not_raw_fps_means()
    {
        // Two frames in bin 0 (10 ms + 20 ms) → harmonic fps = 2 / 0.03.
        var capture = TestCapture.CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            [["0.1", "10.0", "80.0"], ["0.9", "20.0", "80.0"]]);
        var session = _service.Analyze(
            capture,
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));

        var series = SeriesBuilder.Build(session, "fps");

        // Bin 0 has fewer than three frames, so no FPS point is produced.
        Assert.Empty(series.X);
    }

    [Fact]
    public void Direction_maps_to_the_catalog_definitions()
    {
        Assert.Equal(
            MetricDirection.LowerIsBetter,
            CoreMetricCatalog.CoreById["frametime"].Direction);
        Assert.Equal(
            MetricDirection.HigherIsBetter,
            CoreMetricCatalog.CoreById["fps"].Direction);
        Assert.Equal(
            MetricDirection.Undefined,
            CoreMetricCatalog.CoreById["gpu0_util"].Direction);
    }

    [Fact]
    public void Values_matches_Build_y_for_every_metric()
    {
        var session = _service.Analyze(
            TestCapture.MakeSession(seconds: 6),
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));

        foreach (var metric in session.Catalog)
        {
            Assert.Equal(
                SeriesBuilder.Build(session, metric.Id).Y,
                SeriesBuilder.Values(session, metric.Id));
        }
    }
}
