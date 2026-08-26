using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class ChartAvailableMetricsTests
{
    [Fact]
    public void Chart_selector_hides_catalog_metrics_without_analyzed_points_and_restores_them_when_available()
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

        Assert.Contains(filtered.Catalog, metric => metric.Label == "GhostSensor");

        var viewModel = new ChartViewModel();
        viewModel.SetSessions(filtered, null);

        Assert.Contains(viewModel.Metrics, metric => metric.Id == "fps");
        Assert.DoesNotContain(viewModel.Metrics, metric => metric.Label == "GhostSensor");
        Assert.All(viewModel.Metrics, metric =>
            Assert.NotEmpty(FrameViewAnalyzer.Analytics.Series.SeriesBuilder.Values(filtered, metric.Id)));

        var reanalyzed = service.Reanalyze(
            filtered,
            new AnalysisOptions(
                GpuThreshold: 0,
                TrimBufferSeconds: 0,
                AutoGpuThreshold: false,
                ExcludeTransitions: false));
        viewModel.SetSessions(reanalyzed, null);

        Assert.Contains(viewModel.Metrics, metric => metric.Label == "GhostSensor");
    }
}
