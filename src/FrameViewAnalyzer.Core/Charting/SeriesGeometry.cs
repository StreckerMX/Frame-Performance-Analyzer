namespace FrameViewAnalyzer.Core.Charting;

/// <summary>
/// Pure series geometry used by the chart layer: gap-aware point sequences
/// so scenes separated by an excluded load are never joined by a line.
/// </summary>
public static class SeriesGeometry
{
    public const double DefaultMinimumGapSeconds = 1.5;

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
        if (xs.Count == 0)
        {
            return ([], []);
        }

        var resultXs = new List<double>(xs.Count);
        var resultYs = new List<double>(ys.Count);

        for (var i = 0; i < xs.Count; i++)
        {
            if (i > 0 && xs[i] - xs[i - 1] > minimumGapSeconds)
            {
                resultXs.Add(double.NaN);
                resultYs.Add(double.NaN);
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
