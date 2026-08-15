using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.Analytics.Statistics;

/// <summary>
/// Statistics over the visible zoom range. Analytics always use the
/// full-resolution series; the chart only changes which slice is shown.
/// </summary>
public static class VisibleRangeCalculator
{
    /// <summary>Values whose x falls inside [minX, maxX] (inclusive bounds).</summary>
    public static IReadOnlyList<double> FilterValues(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        double minX,
        double maxX)
    {
        var values = new List<double>();
        for (var i = 0; i < xs.Count; i++)
        {
            if (xs[i] >= minX && xs[i] <= maxX)
            {
                values.Add(ys[i]);
            }
        }

        return values;
    }

    /// <summary>Statistics plus the visible point count for one slice.</summary>
    public static (MetricStatistics Stats, int PointCount) Compute(
        MetricDefinition metric,
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        double minX,
        double maxX)
    {
        var values = FilterValues(xs, ys, minX, maxX);
        return (StatisticsCalculator.Compute(metric, values), values.Count);
    }
}
