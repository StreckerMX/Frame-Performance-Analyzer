using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class ChartViewModelTests
{
    private static CaptureAnalysisService Analysis { get; } = new();

    private static SessionAnalysis SessionOf(double[] frameTimes)
    {
        var rows = frameTimes
            .Select((frameTime, index) => new[]
            {
                (index * 0.25).ToString(System.Globalization.CultureInfo.InvariantCulture),
                frameTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "80.0",
            })
            .ToArray();
        var capture = new CaptureData
        {
            Path = "capture.csv",
            DisplayName = "capture",
            Kind = CsvKind.Log,
            Headers = ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            Columns =
            [
                rows.Select(row => row[0]).ToArray(),
                rows.Select(row => row[1]).ToArray(),
                rows.Select(row => row[2]).ToArray(),
            ],
        };
        return Analysis.Analyze(
            capture,
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));
    }

    [Fact]
    public void Load_populates_metrics_and_selects_fps()
    {
        var session = SessionOf([10.0, 10.0, 10.0, 10.0]);
        var viewModel = new ChartViewModel();

        viewModel.Load(session);

        Assert.Equal(session.Catalog.Count, viewModel.Metrics.Count);
        Assert.Equal("fps", viewModel.SelectedMetric!.Id);
        Assert.True(viewModel.HasData);
        Assert.NotNull(viewModel.Series);
        Assert.Single(viewModel.Series!.X);
    }

    [Fact]
    public void Selecting_a_metric_rebuilds_the_series()
    {
        var session = SessionOf([10.0, 10.0, 10.0, 10.0]);
        var viewModel = new ChartViewModel();
        viewModel.Load(session);

        viewModel.SelectedMetric = viewModel.Metrics.Single(metric => metric.Id == "frametime");

        Assert.True(viewModel.HasData);
        Assert.Equal("frametime", viewModel.Series!.Metric.Id);
        Assert.Equal(10.0, viewModel.Series.Y[0]);
    }

    [Fact]
    public void Clear_resets_all_chart_state()
    {
        var viewModel = new ChartViewModel();
        viewModel.Load(SessionOf([10.0, 10.0, 10.0, 10.0]));

        viewModel.Clear();

        Assert.Null(viewModel.Session);
        Assert.Empty(viewModel.Metrics);
        Assert.Null(viewModel.SelectedMetric);
        Assert.False(viewModel.HasData);
        Assert.Equal(0, viewModel.SampleCount);
    }

    [Fact]
    public void Session_without_valid_bins_has_no_data()
    {
        var capture = new CaptureData
        {
            Path = "low.csv",
            DisplayName = "low",
            Kind = CsvKind.Log,
            Headers = ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            Columns =
            [
                ["0.0", "0.25", "0.5"],
                ["10.0", "10.0", "10.0"],
                ["5.0", "5.0", "5.0"],
            ],
        };
        var session = Analysis.Analyze(
            capture,
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));
        var viewModel = new ChartViewModel();

        viewModel.Load(session);

        Assert.False(viewModel.HasData);
        Assert.Null(viewModel.Series);
    }
}
