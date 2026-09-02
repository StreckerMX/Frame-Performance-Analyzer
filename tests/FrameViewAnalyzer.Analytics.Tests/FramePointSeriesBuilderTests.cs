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
    
    [Fact]
    public void Precision_frame_points_remove_isolated_frame_level_fps_spikes()
    {
        var session = _service.Analyze(
            TestCapture.MakeFrameSpikeSession(),
            new AnalysisOptions(
                GpuThreshold: 10,
                TrimBufferSeconds: 0,
                AutoGpuThreshold: false,
                ExcludeTransitions: true));

        Assert.Contains(6, session.ValidBins);

        var series = FramePointSeriesBuilder.Build(session, "fps");

        Assert.Equal(119, series.Y.Length);
        Assert.DoesNotContain(series.Y, value => value > 100.0);
        Assert.All(
            series.Y,
            value => Assert.Equal(100.0, value, precision: 8));
    }

    [Fact]
    public void Raw_frame_points_preserve_isolated_frame_level_fps_spikes()
    {
        var session = _service.Analyze(
            TestCapture.MakeFrameSpikeSession(),
            new AnalysisOptions(
                GpuThreshold: 0,
                TrimBufferSeconds: 0,
                AutoGpuThreshold: false,
                ExcludeTransitions: false));

        var series = FramePointSeriesBuilder.Build(session, "fps");

        Assert.Equal(120, series.Y.Length);
        Assert.Contains(
            series.Y,
            value => Math.Abs(value - 2000.0) < 0.001);
        Assert.Equal(2000.0, series.Y.Max(), precision: 8);
    }
}
