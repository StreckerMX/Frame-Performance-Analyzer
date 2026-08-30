using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core.Charting;
using ScottPlot;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>
/// Draws shaded omitted-time bands over chart gaps so a chart never implies
/// continuous data through an excluded load: each gap span (at least the
/// minimum gap rule) gets a translucent band, and spans at least
/// SeriesGeometry.LabelThresholdSeconds long get a rotated "N s omitted"
/// label near the top of the data range. Works identically for interactive
/// charts and PNG reports; analytics never consume this visualization.
/// </summary>
public static class GapOverlay
{
    public static void Apply(
        Plot plot,
        IEnumerable<MetricSeries> seriesList,
        ChartStyle style)
    {
        var list = seriesList.ToList();
        if (list.Count == 0)
        {
            return;
        }

        var spans = new List<SeriesGeometry.GapSpan>();
        foreach (var series in list)
        {
            spans.AddRange(SeriesGeometry.FindGaps(series.X));
        }

        var yTop = list
            .SelectMany(series => series.Y)
            .DefaultIfEmpty()
            .Max();

        foreach (var span in SeriesGeometry.MergeOverlapping(spans))
        {
            var shaded = plot.Add.HorizontalSpan(span.Start, span.End);
            shaded.FillColor = style.Muted.WithAlpha(0.14);

            if (span.DurationSeconds >= SeriesGeometry.LabelThresholdSeconds)
            {
                var label = plot.Add.Text(
                    $"{span.DurationSeconds:F0} s omitted",
                    (span.Start + span.End) / 2.0,
                    yTop);
                label.Alignment = Alignment.LowerCenter;
                label.LabelFontColor = style.Muted;
                label.LabelFontSize = 11;
                label.LabelRotation = -90;
            }
        }

        // Report-only series carry an explicit marker so this compact KPI text
        // never leaks into the interactive chart. ScottPlot measures multiline
        // axis labels as part of layout, naturally reserving just enough room
        // below each exported graph without adding another report card.
        ReportStatisticsLabel.Apply(plot, list, style);
    }
}
