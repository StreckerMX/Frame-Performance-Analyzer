using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class ChartViewModelInteractionTests
{
    private static SessionAnalysis MakeSession(int seconds = 10, double frameTime = 10.0)
    {
        var rows = new List<string[]>();
        for (var second = 0; second < seconds; second++)
        {
            foreach (var offset in new[] { 0.0, 0.25, 0.5 })
            {
                rows.Add(
                [
                    (second + offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    frameTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
    public void Load_populates_the_fps_kpi_strip()
    {
        var viewModel = new ChartViewModel();

        viewModel.SetSessions(MakeSession(seconds: 5), null);

        Assert.Equal(6, viewModel.KpiTiles.Count);
        Assert.Equal("AVERAGE", viewModel.KpiTiles[0].Label);
        Assert.Equal("1% LOW", viewModel.KpiTiles[1].Label);
        Assert.Equal("0.1% LOW", viewModel.KpiTiles[2].Label);
        Assert.Equal("Max", viewModel.KpiTiles[3].Label);
        Assert.Equal("Min", viewModel.KpiTiles[4].Label);
        Assert.Equal("VISIBLE TIME", viewModel.KpiTiles[5].Label);
        Assert.Equal("100.0", viewModel.KpiTiles[0].Value);
        Assert.Equal("100.0", viewModel.KpiTiles[1].Value);
        Assert.Equal("100.0 FPS", viewModel.KpiTiles[3].Value);
        Assert.Equal("100.0 FPS", viewModel.KpiTiles[4].Value);
        Assert.Equal("5 s", viewModel.KpiTiles[5].Value);
    }

    [Fact]
    public void Frame_points_recalculate_every_fps_kpi_from_full_resolution_values()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(MakeSession(seconds: 5), null);
        var summarySeries = Assert.Single(viewModel.SeriesList);
        var frameX = Enumerable.Range(0, 20).Select(index => index / 4.0).ToArray();
        var frameY = Enumerable.Repeat(50.0, frameX.Length).ToArray();

        viewModel.SetFramePointSeries(
        [
            summarySeries with
            {
                X = frameX,
                Y = frameY,
            },
        ]);

        Assert.Equal("50.0", viewModel.KpiTiles[0].Value);
        Assert.Equal("50.0", viewModel.KpiTiles[1].Value);
        Assert.Equal("50.0", viewModel.KpiTiles[2].Value);
        Assert.Equal("50.0 FPS", viewModel.KpiTiles[3].Value);
        Assert.Equal("50.0 FPS", viewModel.KpiTiles[4].Value);
        Assert.Equal("5 s", viewModel.KpiTiles[5].Value);

        viewModel.ClearFramePointSeries();

        Assert.Equal("100.0", viewModel.KpiTiles[0].Value);
        Assert.Equal("100.0", viewModel.KpiTiles[1].Value);
        Assert.Equal("100.0", viewModel.KpiTiles[2].Value);
        Assert.Equal("100.0 FPS", viewModel.KpiTiles[3].Value);
        Assert.Equal("100.0 FPS", viewModel.KpiTiles[4].Value);
        Assert.Equal("5 s", viewModel.KpiTiles[5].Value);
    }

    [Fact]
    public void Visible_range_updates_the_kpi_strip()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(MakeSession(seconds: 10), null);

        viewModel.UpdateVisibleRange(new ScottPlot.AxisLimits(2, 5, 0, 150));

        Assert.Equal("100.0", viewModel.KpiTiles[0].Value);
        Assert.Equal("4 s", viewModel.KpiTiles[5].Value);
    }

    [Fact]
    public void Selecting_frametime_uses_average_max_min_and_keeps_the_visible_time_range()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(MakeSession(seconds: 10, frameTime: 10.0), null);
        viewModel.UpdateVisibleRange(new ScottPlot.AxisLimits(2, 5, 0, 150));

        viewModel.SelectedMetric = viewModel.Metrics.Single(metric => metric.Id == "frametime");

        Assert.Equal(4, viewModel.KpiTiles.Count);
        Assert.Equal(["AVERAGE", "Max", "Min", "VISIBLE TIME"],
            viewModel.KpiTiles.Select(tile => tile.Label).ToArray());
        Assert.Equal("10.0 ms", viewModel.KpiTiles[0].Value);
        Assert.Equal("10.0 ms", viewModel.KpiTiles[1].Value);
        Assert.Equal("10.0 ms", viewModel.KpiTiles[2].Value);
        Assert.Equal("4 s", viewModel.KpiTiles[3].Value);
    }

    [Fact]
    public void Utilization_metric_uses_the_same_compact_average_range_layout()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(MakeSession(seconds: 5), null);

        viewModel.SelectedMetric = viewModel.Metrics.Single(metric => metric.Id == "gpu0_util");

        Assert.Equal(4, viewModel.KpiTiles.Count);
        Assert.Equal(["AVERAGE", "Max", "Min", "VISIBLE TIME"],
            viewModel.KpiTiles.Select(tile => tile.Label).ToArray());
        Assert.Equal("80.0 %", viewModel.KpiTiles[0].Value);
        Assert.Equal("80.0 %", viewModel.KpiTiles[1].Value);
        Assert.Equal("5 s", viewModel.KpiTiles[^1].Value);
    }

    [Fact]
    public void Metric_direction_controls_comparison_improvement()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(
            MakeSession(seconds: 4, frameTime: 20.0),
            MakeSession(seconds: 4, frameTime: 10.0));

        viewModel.SelectedMetric = viewModel.Metrics.Single(metric => metric.Id == "frametime");

        var averageTile = viewModel.KpiTiles[0];
        Assert.Equal("20.0 ms → 10.0 ms", averageTile.Value);
        Assert.Equal("↓ 50.0%", averageTile.DeltaText);
        Assert.Equal(ImprovementKind.Improvement, averageTile.Kind);
    }

    [Fact]
    public void Step_selected_metric_moves_without_wrapping()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(MakeSession(seconds: 4), null);
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
        viewModel.SetSessions(MakeSession(seconds: 3), null);

        viewModel.Clear();

        Assert.Equal(6, viewModel.KpiTiles.Count);
        Assert.Equal("AVERAGE", viewModel.KpiTiles[0].Label);
        Assert.Equal("Max", viewModel.KpiTiles[3].Label);
        Assert.Equal("Min", viewModel.KpiTiles[4].Label);
        Assert.Equal("--", viewModel.KpiTiles[0].Value);
        Assert.Equal("--", viewModel.KpiTiles[5].Value);
    }
}
