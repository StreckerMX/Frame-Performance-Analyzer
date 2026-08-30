using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.Analytics.Series;

/// <summary>A full-resolution metric series with x relative to the active window.</summary>
public sealed record MetricSeries(
    MetricDefinition Metric,
    double[] X,
    double[] Y,
    string? Label = null,
    SessionRole Role = SessionRole.Base,
    int WorkspaceIndex = 0,
    bool IsReference = false,
    SessionAnalysis? SourceSession = null)
{
    /// <summary>Legend/display label; falls back to the metric label.</summary>
    public string LabelOrDefault => Label ?? Metric.Label;

    /// <summary>
    /// True only for series prepared specifically for a PNG report. Rendering
    /// helpers use this marker to keep report-only presentation out of the
    /// interactive chart without changing the record's constructor contract.
    /// </summary>
    public bool IsReportSeries { get; init; }
}
