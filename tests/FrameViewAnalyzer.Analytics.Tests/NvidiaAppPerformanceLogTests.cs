using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

public class NvidiaAppPerformanceLogTests
{
    [Fact]
    public void Sampled_fps_is_averaged_per_second_and_not_treated_as_per_frame_data()
    {
        var capture = MakeCapture();
        var service = new CaptureAnalysisService();

        var session = service.Analyze(
            capture,
            new AnalysisOptions(
                GpuThreshold: 0,
                TrimBufferSeconds: 0,
                AutoGpuThreshold: false,
                ExcludeTransitions: false));

        var fps = SeriesBuilder.Build(session, "fps");

        Assert.Equal(6, fps.Y.Length);
        Assert.Equal(90.0, fps.Y[0], precision: 6); // arithmetic mean of 100 and 80
        Assert.All(session.Bins, bin => Assert.InRange(bin.FrameCount, 1, 2));
    }

    [Fact]
    public void Low_rate_nvidia_telemetry_columns_remain_chartable()
    {
        var session = new CaptureAnalysisService().Analyze(
            MakeCapture(),
            new AnalysisOptions(0, 0, AutoGpuThreshold: false, ExcludeTransitions: false));

        var onePercentLow = Assert.Single(
            session.Catalog,
            metric => metric.Label == "FPS 1(%) Low");
        Assert.Equal("FPS", onePercentLow.Unit);
        Assert.Equal("Performance", onePercentLow.Category);
        Assert.Equal(MetricDirection.HigherIsBetter, onePercentLow.Direction);

        var series = SeriesBuilder.Build(session, onePercentLow.Id);
        Assert.Equal(6, series.Y.Length);
        Assert.Equal(70.0, series.Y[0], precision: 6);

        var gpu = SeriesBuilder.Build(session, "gpu1_util");
        Assert.Equal(6, gpu.Y.Length);
    }

    private static CaptureData MakeCapture()
    {
        var headers = new[]
        {
            CaptureSourceDetector.NvidiaAppTimeHeader,
            "PID",
            "FPS",
            "FPS 1(%) Low",
            "Render Latency(MSec)",
            "Average PC Latency(MSec)",
            "CPU Utilization(%)",
            "GPU1 Utilization(%)",
            "GPU1 Temperature(Degrees celsius)",
        };

        var rows = new List<string[]>();
        for (var second = 0; second < 6; second++)
        {
            rows.Add(
            [
                $"{second + 0.1:0.0}", "24728", "100", "72", "8.0", "30", "40", "90", "55",
            ]);
            rows.Add(
            [
                $"{second + 0.6:0.0}", "24728", "80", "68", "10.0", "34", "42", "94", "57",
            ]);
        }

        return TestCapture.CaptureWith(headers, rows);
    }
}
