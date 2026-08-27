using FrameViewAnalyzer.Analytics.Series;

namespace FrameViewAnalyzer.Analytics.Tests;

public class FramePointSeriesBuilderTests
{
    private readonly CaptureAnalysisService _service = new();

    [Fact]
    public void Frame_points_are_lazy_cached_and_keep_one_point_per_valid_frame()
    {
        var session = _service.Analyze(
            TestCapture.MakeSession(seconds: 10),
            new AnalysisOptions(
                GpuThreshold: 10,
                TrimBufferSeconds: 0,
                AutoGpuThreshold: false));

        Assert.False(FramePointSeriesBuilder.IsCached(session, "fps"));

        var series = FramePointSeriesBuilder.Build(session, "fps");

        Assert.True(FramePointSeriesBuilder.IsCached(session, "fps"));
        Assert.Equal(30, series.X.Length);
        Assert.Equal(30, series.Y.Length);
        Assert.All(series.Y, value => Assert.Equal(100.0, value, precision: 8));
        Assert.True(series.X.SequenceEqual(series.X.Order()));
        Assert.InRange(series.X[0], 0.0, 0.001);
        Assert.InRange(series.X[^1], 9.49, 9.51);

        // The immutable-session cache returns the exact same completed object.
        Assert.Same(series, FramePointSeriesBuilder.Build(session, "fps"));
    }

    [Fact]
    public void Frame_points_use_compressed_analyzed_time_across_loading_gaps()
    {
        var session = _service.Analyze(
            TestCapture.MakeMultiScene(),
            new AnalysisOptions(TrimBufferSeconds: 0, AutoGpuThreshold: true));

        var series = FramePointSeriesBuilder.Build(session, "fps");

        Assert.NotEmpty(series.X);
        Assert.True(series.X.SequenceEqual(series.X.Order()));
        Assert.InRange(series.X[^1], 8.0, 9.0);
        Assert.DoesNotContain(series.X, value => value >= 9.0);
    }
}
