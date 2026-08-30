using FrameViewAnalyzer.Analytics.Bins;
using FrameViewAnalyzer.Analytics.Filtering;

namespace FrameViewAnalyzer.Analytics.Tests;

public class PrecisionTimelineFilterTests
{
    [Fact]
    public void Smart_edge_filter_removes_only_the_unstable_preload_second()
    {
        var summaries = new List<BinSummary>
        {
            Bin(0, 250, 95),
            Bin(1, 252, 95),
            Bin(2, 248, 94),
            Bin(3, 251, 95),
            // Transition contamination: FPS rises while GPU activity falls,
            // but not enough to cross the global robust FPS fence.
            Bin(4, 390, 65),
            Bin(5, 20, 10),
            Bin(6, 18, 9),
            Bin(7, 22, 11),
            Bin(8, 260, 95),
            Bin(9, 262, 96),
            Bin(10, 258, 94),
            Bin(11, 261, 95),
            Bin(12, 259, 95),
        };

        var profile = FilterProfileDetector.Detect(
            summaries,
            threshold: 53,
            trimBufferSeconds: 0,
            excludeTransitions: true);

        Assert.DoesNotContain(4, profile.ValidBins);
        Assert.DoesNotContain(5, profile.ValidBins);
        Assert.DoesNotContain(6, profile.ValidBins);
        Assert.DoesNotContain(7, profile.ValidBins);
        Assert.Equal(1, profile.Diagnostics.TransitionEdgeBins);
        Assert.Equal(3, profile.Diagnostics.BelowGpuBins);
        Assert.Equal(0, profile.Diagnostics.FpsOutlierBins);
        Assert.Equal(9, profile.Diagnostics.VisibleBins);
        Assert.Equal(
            [0, 1, 2, 3, 8, 9, 10, 11, 12],
            profile.ValidBins.Order().ToArray());
    }

    [Fact]
    public void Smart_edge_filter_does_not_trim_a_normal_scene_boundary()
    {
        var summaries = new List<BinSummary>
        {
            Bin(0, 250, 95),
            Bin(1, 252, 95),
            Bin(2, 248, 94),
            Bin(3, 251, 95),
            Bin(4, 255, 93),
            Bin(5, 20, 10),
            Bin(6, 18, 9),
            Bin(7, 22, 11),
            Bin(8, 260, 95),
            Bin(9, 262, 96),
            Bin(10, 258, 94),
            Bin(11, 261, 95),
            Bin(12, 259, 95),
        };

        var profile = FilterProfileDetector.Detect(
            summaries,
            threshold: 53,
            trimBufferSeconds: 0,
            excludeTransitions: true);

        Assert.Contains(4, profile.ValidBins);
        Assert.Equal(0, profile.Diagnostics.TransitionEdgeBins);
        Assert.Equal(10, profile.Diagnostics.VisibleBins);
    }

    private static BinSummary Bin(int second, double fps, double gpu) =>
        new(second, second, gpu, fps, FrameCount: 240);
}
