using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.RangeAnalysis;

/// <summary>
/// Pure calculations that locate meaningful time ranges in a metric series.
/// Every method returns a <see cref="TimeRange"/> or null when no meaningful
/// region can be determined, mirroring the Python reference exactly.
/// </summary>
public sealed class RangeAnalysisService : IRangeAnalysisService
{
    private readonly record struct Window(double Start, double End, double Mean, double? Std);

    public TimeRange? WorstPerformanceRegion(
        IReadOnlyList<ChartPoint> points,
        bool? higherIsBetter,
        double windowSeconds = RangeAnalysisDefaults.DefaultWindowSeconds)
    {
        if (higherIsBetter is null)
        {
            return null;
        }

        var windows = WindowStats(points, windowSeconds, wantStd: false);
        if (windows.Count == 0)
        {
            return null;
        }

        var best = windows[0];
        foreach (var window in windows)
        {
            var better = higherIsBetter.Value ? window.Mean < best.Mean : window.Mean > best.Mean;
            if (better)
            {
                best = window;
            }
        }

        return new TimeRange(best.Start, best.End);
    }

    public TimeRange? MostStableRegion(
        IReadOnlyList<ChartPoint> points,
        double windowSeconds = RangeAnalysisDefaults.DefaultWindowSeconds)
    {
        var windows = WindowStats(points, windowSeconds, wantStd: true);
        if (windows.Count == 0)
        {
            return null;
        }

        var best = windows[0];
        foreach (var window in windows)
        {
            var standardDeviation = window.Std ?? double.PositiveInfinity;
            if (standardDeviation < (best.Std ?? double.PositiveInfinity))
            {
                best = window;
            }
        }

        return new TimeRange(best.Start, best.End);
    }

    public TimeRange? LargestDropRegion(
        IReadOnlyList<ChartPoint> points,
        bool? higherIsBetter)
    {
        if (higherIsBetter is null)
        {
            return null;
        }

        var ordered = points.OrderBy(point => point.X).ToList();
        var count = ordered.Count;
        if (count < RangeAnalysisDefaults.DropMinGapSamples + 1)
        {
            return null;
        }

        var ys = ordered.Select(point => point.Y).ToArray();
        var span = ys.Max() - ys.Min();
        if (span <= 0.0)
        {
            return null;
        }

        var threshold = System.Math.Max(
            RangeAnalysisDefaults.DropMinAbsolute,
            span * RangeAnalysisDefaults.DropMinFractionOfRange);
        double? best = null;
        var bestStart = 0;
        var bestEnd = 0;

        if (higherIsBetter.Value)
        {
            var peakIndex = 0;
            var peak = ys[0];
            for (var index = 1; index < count; index++)
            {
                if (ys[index] > peak)
                {
                    peakIndex = index;
                    peak = ys[index];
                }

                var drop = peak - ys[index];
                if (drop > threshold
                    && index - peakIndex >= RangeAnalysisDefaults.DropMinGapSamples
                    && (best is null || drop > best))
                {
                    best = drop;
                    bestStart = peakIndex;
                    bestEnd = index;
                }
            }
        }
        else
        {
            var valleyIndex = 0;
            var valley = ys[0];
            for (var index = 1; index < count; index++)
            {
                if (ys[index] < valley)
                {
                    valleyIndex = index;
                    valley = ys[index];
                }

                var rise = ys[index] - valley;
                if (rise > threshold
                    && index - valleyIndex >= RangeAnalysisDefaults.DropMinGapSamples
                    && (best is null || rise > best))
                {
                    best = rise;
                    bestStart = valleyIndex;
                    bestEnd = index;
                }
            }
        }

        return best is null
            ? null
            : new TimeRange(ordered[bestStart].X, ordered[bestEnd].X);
    }

    public TimeRange? LargestAbDifferenceRegion(
        IReadOnlyList<ChartPoint> basePoints,
        IReadOnlyList<ChartPoint> comparisonPoints,
        double windowSeconds = RangeAnalysisDefaults.DefaultWindowSeconds)
    {
        var orderedBase = basePoints.OrderBy(point => point.X).ToList();
        var orderedComparison = comparisonPoints.OrderBy(point => point.X).ToList();
        if (orderedBase.Count == 0 || orderedComparison.Count == 0)
        {
            return null;
        }

        var baseXs = orderedBase.Select(point => point.X).ToArray();
        var baseYs = orderedBase.Select(point => point.Y).ToArray();
        var comparisonXs = orderedComparison.Select(point => point.X).ToArray();
        var comparisonYs = orderedComparison.Select(point => point.Y).ToArray();

        var starts = baseXs.Concat(comparisonXs).Distinct().OrderBy(x => x).ToArray();
        var bestDifference = double.NegativeInfinity;
        TimeRange? best = null;

        foreach (var start in starts)
        {
            var limit = start + windowSeconds;
            var baseLeft = LowerBound(baseXs, start);
            var baseRight = LowerBound(baseXs, limit);
            var comparisonLeft = LowerBound(comparisonXs, start);
            var comparisonRight = LowerBound(comparisonXs, limit);
            var baseCount = baseRight - baseLeft;
            var comparisonCount = comparisonRight - comparisonLeft;
            if (baseCount < RangeAnalysisDefaults.MinSamplesPerWindow
                || comparisonCount < RangeAnalysisDefaults.MinSamplesPerWindow)
            {
                continue;
            }

            var baseMean = Mean(baseYs, baseLeft, baseRight);
            var comparisonMean = Mean(comparisonYs, comparisonLeft, comparisonRight);
            var difference = System.Math.Abs(baseMean - comparisonMean);
            var end = System.Math.Max(baseXs[baseRight - 1], comparisonXs[comparisonRight - 1]);
            if (difference > bestDifference)
            {
                bestDifference = difference;
                best = new TimeRange(start, end);
            }
        }

        return best;
    }

    /// <summary>
    /// Rolling fixed-size windows (start, end, mean, std) per start. Windows
    /// with fewer than <see cref="RangeAnalysisDefaults.MinSamplesPerWindow"/>
    /// samples are dropped so sparse or gapped stretches never fake a region.
    /// </summary>
    private static List<Window> WindowStats(
        IReadOnlyList<ChartPoint> points,
        double windowSeconds,
        bool wantStd)
    {
        var ordered = points.OrderBy(point => point.X).ToList();
        var count = ordered.Count;
        if (count == 0 || ordered[^1].X - ordered[0].X < windowSeconds)
        {
            return [];
        }

        var xs = ordered.Select(point => point.X).ToArray();
        var ys = ordered.Select(point => point.Y).ToArray();
        var windows = new List<Window>();
        for (var left = 0; left < count; left++)
        {
            var start = xs[left];
            var right = LowerBound(xs, start + windowSeconds);
            var windowCount = right - left;
            if (windowCount < RangeAnalysisDefaults.MinSamplesPerWindow)
            {
                continue;
            }

            var mean = Mean(ys, left, right);
            double? standardDeviation = null;
            if (wantStd)
            {
                var variance = 0.0;
                for (var index = left; index < right; index++)
                {
                    var delta = ys[index] - mean;
                    variance += delta * delta;
                }

                variance /= windowCount;
                standardDeviation = System.Math.Sqrt(variance);
            }

            windows.Add(new Window(start, xs[right - 1], mean, standardDeviation));
        }

        return windows;
    }

    private static double Mean(double[] ys, int start, int end)
    {
        var total = 0.0;
        for (var index = start; index < end; index++)
        {
            total += ys[index];
        }

        return total / (end - start);
    }

    /// <summary>First index whose value is ≥ the target (bisect_left).</summary>
    private static int LowerBound(double[] xs, double value)
    {
        var low = 0;
        var high = xs.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (xs[middle] < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
