namespace FrameViewAnalyzer.Analytics.Statistics;

/// <summary>
/// Statistics for one metric series. Null values indicate the field is not
/// part of the metric's statistic set. P1/P01 follow the metric direction:
/// latency-style metrics use the high tail, others the low tail.
/// </summary>
public sealed record MetricStatistics(
    string MetricId,
    double? Avg,
    double? Min,
    double? Max,
    double? P1,
    double? P01);
