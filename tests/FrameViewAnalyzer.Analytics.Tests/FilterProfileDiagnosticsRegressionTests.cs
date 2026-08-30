using FrameViewAnalyzer.Analytics.Bins;
using FrameViewAnalyzer.Analytics.Filtering;

namespace FrameViewAnalyzer.Analytics.Tests;

public sealed class FilterProfileDiagnosticsRegressionTests
{
    [Fact]
    public void Fully_rejected_capture_reports_actual_rejection_counters()
    {
        var summaries = Enumerable.Range(0, 6)
            .Select(index => new BinSummary(
                Index: index,
                Start: index,
                GpuUtil: 10.0,
                Fps: 60.0,
                FrameCount: 60))
            .ToList();

        var profile = FilterProfileDetector.Detect(
            summaries,
            threshold: 50.0,
            trimBufferSeconds: 1.0,
            excludeTransitions: true);

        Assert.Null(profile.Window);
        Assert.Empty(profile.ValidBins);
        Assert.Equal(6, profile.Diagnostics.TotalBins);
        Assert.Equal(6, profile.Diagnostics.BelowGpuBins);
        Assert.Equal(0, profile.Diagnostics.FpsOutlierBins);
        Assert.Equal(0, profile.Diagnostics.TransitionEdgeBins);
        Assert.Equal(0, profile.Diagnostics.VisibleBins);
    }
}
