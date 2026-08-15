using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class ChartViewModelInteractionTests
{
    private static SessionAnalysis MakeSession(int seconds = 10)
    {
        var rows = new List<string[]>();
        for (var second = 0; second < seconds; second++)
        {
            foreach (var offset in new[] { 0.0, 0.25, 0.5 })
            {
                rows.Add(
                [
                    (second + offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "10.0",
                    "80.0",
                ]);
            }
        }

        var columns = new[]
        {
            rows.Select(row => row[0]).ToArray(),
            rows.Select(row => row[1]).ToArray(),
            rows.Select(row => row[2]).ToArray(),
        };
        var capture = new CaptureData
        {
            Path = "capture.csv",
            DisplayName = "capture",
            Kind = CsvKind.Log,
            Headers = ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            Columns = columns,
        };
        return new CaptureAnalysisService().Analyze(
            capture,
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));
    }

    [Fact]
    public void Load_populates_the_kpi_strip()
    {
        var viewModel = new ChartViewModel();

        viewModel.Load(MakeSession(seconds: 5));

        Assert.Equal("100.0", viewModel.AvgFpsText);
        Assert.Equal("100.0", viewModel.P1FpsText);
        Assert.Equal("100 FPS", viewModel.MaxFpsText);
        Assert.Equal("100.0 FPS", viewModel.MinFpsText);
        Assert.Equal("5 s", viewModel.VisibleTimeText);
    }

    [Fact]
    public void Visible_range_updates_the_kpi_strip()
    {
        var viewModel = new ChartViewModel();
        viewModel.Load(MakeSession(seconds: 10));

        viewModel.UpdateVisibleRange(new ScottPlot.AxisLimits(2, 5, 0, 150));

        Assert.Equal("100.0", viewModel.AvgFpsText);
        Assert.Equal("4 s", viewModel.VisibleTimeText);
    }

    [Fact]
    public void Step_selected_metric_moves_without_wrapping()
    {
        var viewModel = new ChartViewModel();
        viewModel.Load(MakeSession(seconds: 4));
        var metricCount = viewModel.Metrics.Count;

        Assert.Equal("fps", viewModel.SelectedMetric!.Id);
        Assert.False(viewModel.StepSelectedMetric(-1)); // already at the start

        Assert.True(viewModel.StepSelectedMetric(1));
        Assert.Equal(viewModel.Metrics[1], viewModel.SelectedMetric);

        for (var i = 0; i < metricCount; i++)
        {
            viewModel.StepSelectedMetric(1);
        }

        Assert.Equal(viewModel.Metrics[^1], viewModel.SelectedMetric);
        Assert.False(viewModel.StepSelectedMetric(1)); // clamped at the end
    }

    [Fact]
    public void Step_selected_metric_without_metrics_is_a_no_op()
    {
        var viewModel = new ChartViewModel();

        Assert.False(viewModel.StepSelectedMetric(1));
    }

    [Fact]
    public void Interaction_toggles_default_to_enabled()
    {
        var viewModel = new ChartViewModel();

        Assert.True(viewModel.WheelZoomEnabled);
        Assert.True(viewModel.PanEnabled);
        Assert.False(viewModel.MarkersVisible);
    }

    [Fact]
    public void Clear_resets_the_kpi_strip()
    {
        var viewModel = new ChartViewModel();
        viewModel.Load(MakeSession(seconds: 3));

        viewModel.Clear();

        Assert.Equal("--", viewModel.AvgFpsText);
        Assert.Equal("--", viewModel.VisibleTimeText);
    }
}
