using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class ChartViewModelTests
{
    private static CaptureAnalysisService Analysis { get; } = new();

    private static SessionAnalysis SessionOf(
        double[] frameTimes,
        (string Header, string[] Values)? extra = null)
    {
        var rows = frameTimes
            .Select((frameTime, index) => new[]
            {
                (index * 0.25).ToString(System.Globalization.CultureInfo.InvariantCulture),
                frameTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "80.0",
            })
            .ToArray();
        var headers = new List<string> { "TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)" };
        var columns = new List<string[]>
        {
            rows.Select(row => row[0]).ToArray(),
            rows.Select(row => row[1]).ToArray(),
            rows.Select(row => row[2]).ToArray(),
        };
        if (extra is { } additional)
        {
            headers.Add(additional.Header);
            columns.Add(additional.Values);
        }

        var capture = new CaptureData
        {
            Path = "capture.csv",
            DisplayName = "capture",
            Kind = CsvKind.Log,
            Headers = headers.ToArray(),
            Columns = columns.ToArray(),
        };
        return Analysis.Analyze(
            capture,
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));
    }

    private static (string, string[]) DroppedColumn(double[] frameTimes) =>
        ("Dropped", frameTimes.Select(_ => "0.0").ToArray());

    [Fact]
    public void SetSessions_populates_metrics_and_selects_fps()
    {
        var session = SessionOf([10.0, 10.0, 10.0, 10.0]);
        var viewModel = new ChartViewModel();

        viewModel.SetSessions(session, null);

        Assert.Equal(session.Catalog.Count, viewModel.Metrics.Count);
        Assert.Equal("fps", viewModel.SelectedMetric!.Id);
        Assert.True(viewModel.HasData);
        Assert.NotNull(viewModel.Series);
        Assert.Single(viewModel.Series!.X);
        Assert.Single(viewModel.SeriesList);
    }

    [Fact]
    public void Selecting_a_metric_rebuilds_the_series()
    {
        var session = SessionOf([10.0, 10.0, 10.0, 10.0]);
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(session, null);

        viewModel.SelectedMetric = viewModel.Metrics.Single(metric => metric.Id == "frametime");

        Assert.True(viewModel.HasData);
        Assert.Equal("frametime", viewModel.Series!.Metric.Id);
        Assert.Equal(10.0, viewModel.Series.Y[0]);
    }

    [Fact]
    public void Clear_resets_all_chart_state()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(SessionOf([10.0, 10.0, 10.0, 10.0]), null);

        viewModel.Clear();

        Assert.Null(viewModel.Session);
        Assert.Empty(viewModel.Metrics);
        Assert.Null(viewModel.SelectedMetric);
        Assert.False(viewModel.HasData);
        Assert.Empty(viewModel.SeriesList);
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

        viewModel.SetSessions(session, null);

        Assert.False(viewModel.HasData);
        Assert.Null(viewModel.Series);
    }

    [Fact]
    public void Comparison_session_adds_a_second_series_with_legend_label()
    {
        var baseSession = SessionOf([10.0, 10.0, 10.0, 10.0]);
        var comparison = SessionOf([20.0, 20.0, 20.0, 20.0]);
        var viewModel = new ChartViewModel();

        viewModel.SetSessions(baseSession, comparison);

        Assert.Equal(2, viewModel.SeriesList.Count);
        Assert.Equal("Comparison", viewModel.SeriesList[1].LabelOrDefault);
        Assert.NotNull(viewModel.ComparisonSeries);
    }

    [Fact]
    public void Clearing_the_comparison_keeps_the_base_series()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(SessionOf([10.0, 10.0, 10.0, 10.0]), SessionOf([20.0, 20.0, 20.0, 20.0]));

        viewModel.SetSessions(viewModel.Session, null);

        Assert.Single(viewModel.SeriesList);
        Assert.Null(viewModel.ComparisonSession);
    }

    [Fact]
    public void Replacing_the_base_preserves_the_selected_metric_when_possible()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(SessionOf([10.0, 10.0, 10.0, 10.0]), null);
        viewModel.SelectedMetric = viewModel.Metrics.Single(metric => metric.Id == "frametime");

        viewModel.SetSessions(SessionOf([10.0, 10.0, 10.0, 10.0]), null);

        Assert.Equal("frametime", viewModel.SelectedMetric!.Id);
    }

    [Fact]
    public void Kpi_tiles_show_base_to_comparison_values_with_direction()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(
            SessionOf([10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0]),   // 100 FPS, 2 s
            SessionOf([20.0, 20.0, 20.0, 20.0]));                          // 50 FPS, 1 s

        var averageTile = viewModel.KpiTiles[0];
        Assert.Equal("100.0 → 50.0", averageTile.Value);
        Assert.Equal("↓ 50.0%", averageTile.DeltaText);
        Assert.Equal(ImprovementKind.Regression, averageTile.Kind);

        var timeTile = viewModel.KpiTiles[5];
        Assert.Equal("2 s", timeTile.Value);
        Assert.Equal("vs 1 s", timeTile.DeltaText);
    }

    [Fact]
    public void Kpi_tiles_show_improvement_in_the_right_direction()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(
            SessionOf([20.0, 20.0, 20.0, 20.0]),
            SessionOf([10.0, 10.0, 10.0, 10.0]));

        var averageTile = viewModel.KpiTiles[0];
        Assert.Equal(ImprovementKind.Improvement, averageTile.Kind);
        Assert.Equal("↑ 100.0%", averageTile.DeltaText);
    }

    [Fact]
    public void To_points_converts_series_and_handles_missing_series()
    {
        var series = new MetricSeries(
            CoreMetricCatalog.CoreById["fps"],
            [0.0, 1.0, 2.0],
            [100.0, 120.0, 90.0]);

        var points = ChartViewModel.ToPoints(series);

        Assert.Equal(3, points.Count);
        Assert.Equal(new ChartPoint(1.0, 120.0), points[1]);
        Assert.Empty(ChartViewModel.ToPoints(null));
    }

    [Fact]
    public void Metrics_are_the_union_of_base_and_comparison_catalogs()
    {
        var baseSession = SessionOf([10.0, 10.0, 10.0, 10.0]);
        var comparison = SessionOf(
            [20.0, 20.0, 20.0, 20.0],
            DroppedColumn([20.0, 20.0, 20.0, 20.0]));
        var viewModel = new ChartViewModel();

        viewModel.SetSessions(baseSession, comparison);

        var ids = viewModel.Metrics.Select(metric => metric.Id).ToList();
        Assert.Equal(baseSession.Catalog.Count + 1, ids.Count);
        Assert.Contains("dropped", ids);
        Assert.DoesNotContain("dropped", baseSession.Catalog.Select(metric => metric.Id));
        // Base ordering first, comparison-only metrics appended.
        Assert.Equal(
            baseSession.Catalog.Select(metric => metric.Id),
            ids.Take(baseSession.Catalog.Count));
        Assert.Equal("dropped", ids[^1]);
    }

    [Fact]
    public void Comparison_only_metric_renders_the_comparison_alone()
    {
        var baseSession = SessionOf([10.0, 10.0, 10.0, 10.0]);
        var comparison = SessionOf(
            [20.0, 20.0, 20.0, 20.0],
            DroppedColumn([20.0, 20.0, 20.0, 20.0]));
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(baseSession, comparison);

        viewModel.SelectedMetric = viewModel.Metrics.Single(metric => metric.Id == "dropped");

        Assert.True(viewModel.HasData);
        Assert.Null(viewModel.Series);
        Assert.NotNull(viewModel.ComparisonSeries);
        Assert.Equal("Comparison", viewModel.ComparisonSeries!.Label);
        Assert.Single(viewModel.SeriesList);
    }

    [Fact]
    public void Base_only_metric_renders_the_base_normally()
    {
        var baseSession = SessionOf(
            [10.0, 10.0, 10.0, 10.0],
            DroppedColumn([10.0, 10.0, 10.0, 10.0]));
        var comparison = SessionOf([20.0, 20.0, 20.0, 20.0]);
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(baseSession, comparison);

        viewModel.SelectedMetric = viewModel.Metrics.Single(metric => metric.Id == "dropped");

        Assert.True(viewModel.HasData);
        Assert.NotNull(viewModel.Series);
        Assert.Null(viewModel.ComparisonSeries);
        Assert.Single(viewModel.SeriesList);
    }

    [Fact]
    public void Comparison_only_metric_does_not_hide_shared_metrics()
    {
        var baseSession = SessionOf([10.0, 10.0, 10.0, 10.0]);
        var comparison = SessionOf(
            [20.0, 20.0, 20.0, 20.0],
            DroppedColumn([20.0, 20.0, 20.0, 20.0]));
        var viewModel = new ChartViewModel();
        viewModel.SetSessions(baseSession, comparison);
        viewModel.SelectedMetric = viewModel.Metrics.Single(metric => metric.Id == "dropped");

        viewModel.SelectedMetric = viewModel.Metrics.Single(metric => metric.Id == "fps");

        Assert.True(viewModel.HasData);
        Assert.NotNull(viewModel.Series);
        Assert.NotNull(viewModel.ComparisonSeries);
        Assert.Equal(2, viewModel.SeriesList.Count);
    }
}
