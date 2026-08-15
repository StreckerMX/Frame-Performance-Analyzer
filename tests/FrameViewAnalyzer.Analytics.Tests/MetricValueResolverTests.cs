using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.Analytics.Tests;

public class MetricValueResolverTests
{
    [Fact]
    public void Resolved_columns_match_per_row_resolution()
    {
        var capture = TestCapture.CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)", "FPS"],
            [
                ["0.0", "10.0", "80.0", "100.0"],
                ["0.5", "", "81.0", "120.0"],
                ["1.0", "20.0", "80.0", "50.0"],
                ["1.5", "10.0", "80.0", ""],
            ]);

        foreach (var metricId in new[] { "fps", "frametime", "gpu0_util" })
        {
            var metric = CoreMetricCatalog.CoreById[metricId];
            var columns = MetricValueResolver.MetricColumns.Resolve(capture, metric);
            for (var row = 0; row < capture.RowCount; row++)
            {
                Assert.Equal(
                    MetricValueResolver.GetMetricValue(capture, metric, row),
                    MetricValueResolver.GetMetricValue(capture, metric, row, columns));
            }
        }
    }

    [Fact]
    public void Frametime_falls_back_to_the_fps_column()
    {
        var capture = TestCapture.CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)", "FPS"],
            [
                ["0.0", "", "80.0", "120.0"],
                ["0.5", "10.0", "80.0", "100.0"],
            ]);
        var metric = CoreMetricCatalog.CoreById["frametime"];
        var columns = MetricValueResolver.MetricColumns.Resolve(capture, metric);

        Assert.Equal(1000.0 / 120.0, MetricValueResolver.GetMetricValue(capture, metric, 0, columns));
        Assert.Equal(10.0, MetricValueResolver.GetMetricValue(capture, metric, 1, columns));
    }
}
