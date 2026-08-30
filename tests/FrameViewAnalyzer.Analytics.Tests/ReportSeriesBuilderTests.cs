using FrameViewAnalyzer.Analytics.Series;

namespace FrameViewAnalyzer.Analytics.Tests;

public class ReportSeriesBuilderTests
{
    private readonly CaptureAnalysisService _service = new();

    [Fact]
    public void Report_series_uses_summary_resolution_when_frame_points_are_off()
    {
        var session = Analyze();
        var summary = SeriesBuilder.Build(session, "fps");

        var report = ReportSeriesBuilder.Build(session, "fps", useFramePoints: false);

        Assert.True(summary.X.SequenceEqual(report.X));
        Assert.True(summary.Y.SequenceEqual(report.Y));
        Assert.True(report.Y.Length < 30);
        Assert.True(report.IsReportSeries);
        Assert.False(FramePointSeriesBuilder.IsCached(session, "fps"));
    }

    [Fact]
    public void Report_series_uses_true_frames_when_frame_points_are_on()
    {
        var session = Analyze();
        Assert.False(FramePointSeriesBuilder.IsCached(session, "fps"));

        var report = ReportSeriesBuilder.Build(session, "fps", useFramePoints: true);

        Assert.True(FramePointSeriesBuilder.IsCached(session, "fps"));
        Assert.True(report.IsReportSeries);
        Assert.Equal(30, report.Y.Length);
        Assert.All(report.Y, value => Assert.Equal(100.0, value, precision: 8));
        Assert.True(report.X.SequenceEqual(report.X.Order()));
    }

    private SessionAnalysis Analyze() =>
        _service.Analyze(
            TestCapture.MakeSession(seconds: 10),
            new AnalysisOptions(
                GpuThreshold: 10,
                TrimBufferSeconds: 0,
                AutoGpuThreshold: false));
}
