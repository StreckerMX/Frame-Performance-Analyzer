using FrameViewAnalyzer.Analytics.Bins;
using FrameViewAnalyzer.Analytics.Filtering;
using FrameViewAnalyzer.Analytics.Samples;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics;

/// <summary>Detected metadata shown on session cards and details.</summary>
public sealed record SessionMetadata(
    string Application,
    string Resolution,
    string Gpu,
    string Cpu,
    string Runtime,
    string Duration,
    string CaptureDuration,
    int FrameCount,
    int MetricCount);

/// <summary>
/// Complete, explainable analysis of one capture. Immutable after analysis;
/// series and statistics are computed on demand from this snapshot.
/// </summary>
public sealed class SessionAnalysis
{
    public required CaptureData Capture { get; init; }
    public required IReadOnlyList<MetricDefinition> Catalog { get; init; }
    public required ParsedSamples Samples { get; init; }
    public required AnalysisOptions EffectiveOptions { get; init; }
    public required IReadOnlyList<BinSummary> Bins { get; init; }
    public required IReadOnlyDictionary<int, int[]> RowsByBin { get; init; }
    public required ActiveWindow? Window { get; init; }
    public required IReadOnlySet<int> ValidBins { get; init; }
    public required FilterDiagnostics Diagnostics { get; init; }
    public SessionMetadata? Metadata { get; init; }
}
