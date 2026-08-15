using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.Analytics.Series;

/// <summary>A full-resolution metric series with x relative to the active window.</summary>
public sealed record MetricSeries(
    MetricDefinition Metric,
    double[] X,
    double[] Y,
    string? Label = null,
    SessionRole Role = SessionRole.Base)
{
    /// <summary>Legend/display label; falls back to the metric label.</summary>
    public string LabelOrDefault => Label ?? Metric.Label;
}
