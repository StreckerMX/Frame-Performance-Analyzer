using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Core.Tests;

public class MetricCatalogTests
{
    private static CaptureData CaptureWith(
        IReadOnlyList<string> headers,
        params string[][] rows)
    {
        var columns = new string[headers.Count][];
        for (var i = 0; i < headers.Count; i++)
        {
            columns[i] = new string[rows.Length];
            for (var r = 0; r < rows.Length; r++)
            {
                columns[i][r] = rows[r][i];
            }
        }

        return new CaptureData
        {
            Path = "sample.csv",
            DisplayName = "sample",
            Kind = CsvKind.Log,
            Headers = headers,
            Columns = columns,
        };
    }

    [Fact]
    public void Dynamic_metric_ids_are_unique_and_stable()
    {
        var capture = CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "Metric A", "Metric-A"],
            ["0.0", "10.0", "1.0", "2.0"]);

        var first = MetricCatalogBuilder.Build(capture);
        var second = MetricCatalogBuilder.Build(capture);
        var dynamicFirst = first
            .Where(metric => metric.Label.StartsWith("Metric", StringComparison.Ordinal))
            .ToDictionary(metric => metric.Label, metric => metric.Id, StringComparer.Ordinal);
        var dynamicSecond = second
            .Where(metric => metric.Label.StartsWith("Metric", StringComparison.Ordinal))
            .ToDictionary(metric => metric.Label, metric => metric.Id, StringComparer.Ordinal);

        Assert.Equal(dynamicFirst, dynamicSecond);
        Assert.Equal(2, dynamicFirst.Values.Distinct().Count());
        Assert.All(dynamicFirst.Values, id => Assert.True(id.Length <= 64));
    }

    [Fact]
    public void Every_metric_has_contextual_explanation()
    {
        var capture = CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)", "Custom Sensor"],
            ["0.0", "10.0", "80.0", "12.0"]);

        foreach (var metric in MetricCatalogBuilder.Build(capture))
        {
            Assert.False(string.IsNullOrEmpty(CoreMetricCatalog.DescriptionFor(metric)));
            Assert.False(string.IsNullOrEmpty(CoreMetricCatalog.SourceFor(metric, capture.Headers)));
            Assert.False(string.IsNullOrEmpty(CoreMetricCatalog.DirectionLabelFor(metric)));
            Assert.False(string.IsNullOrEmpty(CoreMetricCatalog.ChartExplanationFor(metric)));
            Assert.False(string.IsNullOrEmpty(CoreMetricCatalog.StatisticsExplanationFor(metric.Id)));
        }
    }

    [Fact]
    public void Fps_source_matches_the_available_frametime_column()
    {
        var source = CoreMetricCatalog.SourceFor(
            CoreMetricCatalog.CoreById["fps"],
            ["MsBetweenDisplayChange"]);

        Assert.Contains("MsBetweenDisplayChange", source);
        Assert.DoesNotContain("MsBetweenPresents /", source);
    }

    [Fact]
    public void Gpu_utilization_is_contextual_not_intrinsically_better()
    {
        Assert.Equal(
            "Interpret according to context",
            CoreMetricCatalog.DirectionLabelFor(CoreMetricCatalog.CoreById["gpu0_util"]));
    }

    [Fact]
    public void Catalog_rejects_non_log_captures()
    {
        var capture = new CaptureData
        {
            Path = "summary.csv",
            DisplayName = "summary",
            Kind = CsvKind.Summary,
            Headers = ["Log Name", "Avg FPS"],
            Columns = [["Run A"], ["100"]],
        };

        Assert.Empty(MetricCatalogBuilder.Build(capture));
    }

    [Fact]
    public void Catalog_excludes_skip_and_time_columns()
    {
        var capture = CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "Application", "GPU", "CPUUtil(%)"],
            ["0.0", "10.0", "Game.exe", "RTX 5070 Ti", "45.0"]);

        var catalog = MetricCatalogBuilder.Build(capture);

        Assert.Contains(catalog, metric => metric.Id == "fps");
        Assert.Contains(catalog, metric => metric.Id == "frametime");
        Assert.Contains(catalog, metric => metric.Id == "cpu_util");
        Assert.DoesNotContain(catalog, metric => metric.Label == "Application");
        Assert.DoesNotContain(catalog, metric => metric.Label == "GPU");
    }

    [Fact]
    public void Improvement_kind_follows_direction_semantics()
    {
        Assert.Equal(
            ImprovementKind.Improvement,
            CoreMetricCatalog.ClassifyImprovement(MetricDirection.HigherIsBetter, 100.0, 110.0));
        Assert.Equal(
            ImprovementKind.Regression,
            CoreMetricCatalog.ClassifyImprovement(MetricDirection.HigherIsBetter, 100.0, 90.0));
        Assert.Equal(
            ImprovementKind.Improvement,
            CoreMetricCatalog.ClassifyImprovement(MetricDirection.LowerIsBetter, 10.0, 8.0));
        Assert.Equal(
            ImprovementKind.Regression,
            CoreMetricCatalog.ClassifyImprovement(MetricDirection.LowerIsBetter, 10.0, 12.0));
        Assert.Equal(
            ImprovementKind.None,
            CoreMetricCatalog.ClassifyImprovement(MetricDirection.Undefined, 10.0, 12.0));
        Assert.Equal(
            ImprovementKind.None,
            CoreMetricCatalog.ClassifyImprovement(MetricDirection.HigherIsBetter, 100.0, 100.0));
        Assert.Equal(
            ImprovementKind.None,
            CoreMetricCatalog.ClassifyImprovement(MetricDirection.HigherIsBetter, null, 110.0));
        Assert.Equal(
            ImprovementKind.None,
            CoreMetricCatalog.ClassifyImprovement(MetricDirection.HigherIsBetter, 100.0, null));
    }
}
