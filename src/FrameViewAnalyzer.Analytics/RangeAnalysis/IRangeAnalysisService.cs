using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.RangeAnalysis;

/// <summary>
/// Locates meaningful time ranges in a metric series for the Analyze menu:
/// worst-performance window, most-stable window, largest performance drop,
/// and largest A/B divergence. Mirrors the Python reference algorithms.
/// </summary>
public interface IRangeAnalysisService
{
    TimeRange? WorstPerformanceRegion(
        IReadOnlyList<ChartPoint> points,
        bool? higherIsBetter,
        double windowSeconds = RangeAnalysisDefaults.DefaultWindowSeconds);

    TimeRange? MostStableRegion(
        IReadOnlyList<ChartPoint> points,
        double windowSeconds = RangeAnalysisDefaults.DefaultWindowSeconds);

    TimeRange? LargestDropRegion(
        IReadOnlyList<ChartPoint> points,
        bool? higherIsBetter);

    TimeRange? LargestAbDifferenceRegion(
        IReadOnlyList<ChartPoint> basePoints,
        IReadOnlyList<ChartPoint> comparisonPoints,
        double windowSeconds = RangeAnalysisDefaults.DefaultWindowSeconds);
}
