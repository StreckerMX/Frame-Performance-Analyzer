using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.Analytics.Series;

/// <summary>A full-resolution metric series with x relative to the active window.</summary>
public sealed record MetricSeries(MetricDefinition Metric, double[] X, double[] Y);
