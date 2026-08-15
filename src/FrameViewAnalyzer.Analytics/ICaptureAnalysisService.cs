using FrameViewAnalyzer.Analytics.Samples;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics;

/// <summary>
/// Stateless analysis entry point, consumable from WPF, tests, or a future
/// CLI. No UI or IO dependencies.
/// </summary>
public interface ICaptureAnalysisService
{
    /// <summary>Full analysis of a FrameView log capture.</summary>
    SessionAnalysis Analyze(CaptureData capture, AnalysisOptions? options = null);

    /// <summary>Applies new filters without re-parsing or re-discovering.</summary>
    SessionAnalysis Reanalyze(SessionAnalysis previous, AnalysisOptions options);

    double ComputeAutoGpuThreshold(ParsedSamples samples);
}
