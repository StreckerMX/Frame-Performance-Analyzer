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
}
