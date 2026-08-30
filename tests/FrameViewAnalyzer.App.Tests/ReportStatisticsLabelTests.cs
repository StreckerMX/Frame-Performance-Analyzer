using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;

namespace FrameViewAnalyzer.App.Tests;

public class ReportStatisticsLabelTests
{
    [Fact]
    public void Pair_fps_text_contains_the_six_expected_compact_statistics()
    {
        var metric = CoreMetricCatalog.CoreById["fps"];
        var text = ReportStatisticsLabel.BuildText(
        [
            Series(metric, [100.0, 120.0, 90.0], SessionRole.Base),
            Series(metric, [80.0, 90.0, 70.0], SessionRole.Comparison),
        ]);

        Assert.Contains("AVERAGE", text);
        Assert.Contains("1% LOW", text);
        Assert.Contains("0.1% LOW", text);
        Assert.Contains("MAX", text);
        Assert.Contains("MIN", text);
        Assert.Contains("TIME 3 s", text);
        Assert.Contains("→", text);
        Assert.Contains("%", text);
        Assert.Equal(2, text.Split('\n').Length);
    }

    [Fact]
    public void Non_fps_text_uses_average_max_min_and_visible_time_only()
    {
        var metric = CoreMetricCatalog.CoreById["frametime"];
        var text = ReportStatisticsLabel.BuildText(
        [
            Series(metric, [8.0, 9.0, 10.0], SessionRole.Base),
            Series(metric, [7.0, 8.0, 9.0], SessionRole.Comparison),
        ]);

        Assert.Contains("AVERAGE", text);
        Assert.Contains("MAX", text);
        Assert.Contains("MIN", text);
        Assert.Contains("TIME 3 s", text);
        Assert.DoesNotContain("1% LOW", text);
        Assert.Contains("ms", text);
        Assert.Equal(2, text.Split('\n').Length);
    }

    [Fact]
    public void Axis_label_is_changed_only_for_explicit_report_series()
    {
        var metric = CoreMetricCatalog.CoreById["fps"];
        var plot = new Plot();
        plot.Axes.Bottom.Label.Text = "Capture time (s)";
        var style = ChartStyle.FromApplicationResources();

        ReportStatisticsLabel.Apply(
            plot,
            [Series(metric, [100.0, 101.0], SessionRole.Base, isReportSeries: false)],
            style);
        Assert.Equal("Capture time (s)", plot.Axes.Bottom.Label.Text);

        ReportStatisticsLabel.Apply(
            plot,
            [Series(metric, [100.0, 101.0], SessionRole.Base, isReportSeries: true)],
            style);
        Assert.Contains("\nAVERAGE", plot.Axes.Bottom.Label.Text);
        Assert.Contains("TIME 2 s", plot.Axes.Bottom.Label.Text);
    }

    [Fact]
    public void Multi_text_uses_one_compact_row_per_benchmark()
    {
        var metric = CoreMetricCatalog.CoreById["fps"];
        var text = ReportStatisticsLabel.BuildText(
        [
            Series(metric, [100.0, 101.0], SessionRole.Comparison, workspaceIndex: 0),
            Series(metric, [110.0, 111.0], SessionRole.Comparison, workspaceIndex: 1),
            Series(metric, [120.0, 121.0], SessionRole.Comparison, workspaceIndex: 2),
        ]);

        var lines = text.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("B1", lines[0]);
        Assert.StartsWith("B2", lines[1]);
        Assert.StartsWith("B3", lines[2]);
        Assert.All(lines, line => Assert.Contains("TIME 2 s", line));
    }

    private static MetricSeries Series(
        MetricDefinition metric,
        double[] values,
        SessionRole role,
        int workspaceIndex = 0,
        bool isReportSeries = true) =>
        new MetricSeries(
            metric,
            Enumerable.Range(0, values.Length).Select(index => (double)index).ToArray(),
            values,
            Role: role,
            WorkspaceIndex: workspaceIndex)
        {
            IsReportSeries = isReportSeries,
        };
}
