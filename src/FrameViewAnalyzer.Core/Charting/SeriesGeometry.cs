namespace FrameViewAnalyzer.Core.Charting;

/// <summary>
/// Pure series geometry used by the chart layer: gap-aware point sequences
/// so scenes separated by an excluded load are never joined by a line.
/// </summary>
public static class SeriesGeometry
{
    public const double DefaultMinimumGapSeconds = 1.5;

    /// <summary>Gaps at least this long are labeled "N s omitted" on the chart.</summary>
    public const double LabelThresholdSeconds = 3.0;

    /// <summary>One omitted-time span between two consecutive points.</summary>
    public readonly record struct GapSpan(double Start, double End)
    {
        public double DurationSeconds => End - Start;
    }

    /// <summary>
    /// Spans where consecutive x values are farther apart than
    /// <paramref name="minimumGapSeconds"/>, i.e. the omitted load ranges
    /// that InsertGapBreaks renders as line breaks.
    /// </summary>
    public static IReadOnlyList<GapSpan> FindGaps(
        IReadOnlyList<double> xs,
        double minimumGapSeconds = DefaultMinimumGapSeconds)
    {
        var gaps = new List<GapSpan>();
        for (var i = 1; i < xs.Count; i++)
        {
            if (xs[i] - xs[i - 1] > minimumGapSeconds)
            {
                gaps.Add(new GapSpan(xs[i - 1], xs[i]));
            }
        }

        return gaps;
    }

    /// <summary>
    /// Union of overlapping or adjacent spans, ordered by start. Used so
    /// multi-series plots shade each omitted range exactly once.
    /// </summary>
    public static IReadOnlyList<GapSpan> MergeOverlapping(IReadOnlyList<GapSpan> spans)
    {
        if (spans.Count <= 1)
        {
            return spans;
        }

        var sorted = spans.OrderBy(span => span.Start).ToList();
        var merged = new List<GapSpan>(sorted.Count) { sorted[0] };
        foreach (var span in sorted.Skip(1))
        {
            var last = merged[^1];
            if (span.Start <= last.End)
            {
                merged[^1] = new GapSpan(last.Start, System.Math.Max(last.End, span.End));
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged;
    }

    /// <summary>
    /// Inserts NaN breaks wherever consecutive x values are farther apart
    /// than <paramref name="minimumGapSeconds"/> (excluded loads appear as
    /// gaps instead of interpolated lines).
    /// </summary>
    public static (double[] Xs, double[] Ys) InsertGapBreaks(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        double minimumGapSeconds = DefaultMinimumGapSeconds)
    {
        return InsertGapBreaks(xs, ys, FindGaps(xs, minimumGapSeconds));
    }

    /// <summary>
    /// Inserts NaN breaks only when the rendered sequence crosses a gap that
    /// was detected in the original full-resolution series. This is used
    /// after chart-only decimation so skipped samples never masquerade as
    /// omitted loading-screen time.
    /// </summary>
    public static (double[] Xs, double[] Ys) InsertGapBreaks(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        IReadOnlyList<GapSpan> sourceGaps)
    {
        if (xs.Count == 0)
        {
            return ([], []);
        }

        if (xs.Count != ys.Count)
        {
            throw new ArgumentException("X and Y point counts must match.");
        }

        if (sourceGaps.Count == 0)
        {
            return ([.. xs], [.. ys]);
        }

        var gaps = sourceGaps.Count <= 1
            ? sourceGaps
            : sourceGaps.OrderBy(gap => gap.Start).ToArray();
        var resultXs = new List<double>(xs.Count + sourceGaps.Count);
        var resultYs = new List<double>(ys.Count + sourceGaps.Count);
        var gapIndex = 0;

        for (var i = 0; i < xs.Count; i++)
        {
            if (i > 0)
            {
                var previousX = xs[i - 1];
                var currentX = xs[i];

                while (gapIndex < gaps.Count && gaps[gapIndex].End <= previousX)
                {
                    gapIndex++;
                }

                if (gapIndex < gaps.Count
                    && gaps[gapIndex].Start >= previousX
                    && gaps[gapIndex].End <= currentX)
                {
                    resultXs.Add(double.NaN);
                    resultYs.Add(double.NaN);
                }
            }

            resultXs.Add(xs[i]);
            resultYs.Add(ys[i]);
        }

        return (resultXs.ToArray(), resultYs.ToArray());
    }

    /// <summary>
    /// Index of the point nearest to <paramref name="x"/> in an ascending
    /// series (binary search; ties resolve to the earlier point).
    /// Returns -1 for empty input.
    /// </summary>
    public static int NearestIndex(IReadOnlyList<double> xs, double x)
    {
        if (xs.Count == 0)
        {
            return -1;
        }

        if (x <= xs[0])
        {
            return 0;
        }

        if (x >= xs[^1])
        {
            return xs.Count - 1;
        }

        var low = 0;
        var high = xs.Count - 1;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (xs[middle] <= x)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return System.Math.Abs(xs[low] - x) <= System.Math.Abs(xs[high] - x) ? low : high;
    }
}
