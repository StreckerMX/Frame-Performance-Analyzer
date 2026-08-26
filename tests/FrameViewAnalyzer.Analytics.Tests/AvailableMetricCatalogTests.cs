using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

public class AvailableMetricCatalogTests
{
    [Fact]
    public void Analysis_hides_metrics_without_points_after_active_filters_and_can_restore_them_on_reanalysis()
    {
        var rows = new List<string[]>();
        for (var second = 0; second < 6; second++)
        {
            foreach (var offset in new[] { 0.0, 0.25, 0.5 })
            {
                rows.Add(
                [
                    (second + offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "10.0",
                    second == 0 ? "0.0" : "80.0",
                    second == 0 ? "123.0" : "NA",
                ]);
            }
        }

        var capture = new CaptureData
        {
            Path = "capture.csv",
            DisplayName = "capture",
            Kind = CsvKind.Log,
            Headers = ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)", "GhostSensor"],
            Columns =
            [
                rows.Select(row => row[0]).ToArray(),
                rows.Select(row => row[1]).ToArray(),
                rows.Select(row => row[2]).ToArray(),
                rows.Select(row => row[3]).ToArray(),
            ],
        };

        var service = new CaptureAnalysisService();
        var filtered = service.Analyze(
            capture,
            new AnalysisOptions(
                GpuThreshold: 10,
                TrimBufferSeconds: 0,
                AutoGpuThreshold: false,
                ExcludeTransitions: false));

        Assert.DoesNotContain(filtered.Catalog, metric => metric.Label == "GhostSensor");
        Assert.Equal(filtered.Catalog.Count, filtered.Metadata?.MetricCount);

        var reanalyzed = service.Reanalyze(
            filtered,
            new AnalysisOptions(
                GpuThreshold: 0,
                TrimBufferSeconds: 0,
                AutoGpuThreshold: false,
                ExcludeTransitions: false));

        Assert.Contains(reanalyzed.Catalog, metric => metric.Label == "GhostSensor");
    }
}
