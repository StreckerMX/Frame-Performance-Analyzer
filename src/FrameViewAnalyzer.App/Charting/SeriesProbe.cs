using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core.Charting;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>
/// Pure tooltip point selection: the nearest real sample of each plotted
/// series within a tolerance of the cursor X. No interpolation — every value
/// comes from an actual sample. Kept outside the WPF mouse handlers so the
/// selection rules are unit-testable.
/// </summary>
public static class SeriesProbe
{
    public readonly record struct Hit(MetricSeries Series, int Index, double X, double Y);

    /// <summary>Qualifying series in plot order; empty when none is near.</summary>
    public static IReadOnlyList<Hit> Select(
        IReadOnlyList<MetricSeries> seriesList,
        double cursorX,
        double tolerance)
    {
        var hits = new List<Hit>();
        foreach (var series in seriesList)
        {
            if (series.X.Length == 0)
            {
                continue;
            }

            var index = SeriesGeometry.NearestIndex(series.X, cursorX);
            if (index < 0 || System.Math.Abs(series.X[index] - cursorX) > tolerance)
            {
                continue;
            }

            hits.Add(new Hit(series, index, series.X[index], series.Y[index]));
        }

        return hits;
    }

    /// <summary>
    /// Deterministic crosshair anchor: the first qualifying hit in plot
    /// order, i.e. Base when Base qualifies, otherwise the first comparison
    /// series that does.
    /// </summary>
    public static Hit? Anchor(IReadOnlyList<Hit> hits) => hits.Count > 0 ? hits[0] : null;
}
