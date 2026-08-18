using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Charting;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>
/// Maps analytics series onto a ScottPlot plot: decimation, gap breaks,
/// per-metric plot kinds, average lines, and theme styling. Pure plot
/// assembly — no WPF types beyond ScottPlot, so it is headless-testable.
/// </summary>
public static class ChartPlotBuilder
{
    private static readonly string[] MultiPalette =
    [
        "#E69F00",
        "#CC79A7",
        "#56B4E9",
        "#F0E442",
        "#D55E00",
        "#009E73",
    ];

    /// <summary>
    /// Per-metric plot kind: FPS series are uniformly one-second spaced, so
    /// gap-free FPS data renders as SignalXY (constant-time rendering);
    /// everything else, or any gap-broken series, renders as Scatter.
    /// </summary>
    public static PlotKind ChooseKind(MetricDefinition metric, IReadOnlyList<double> xs)
    {
        if (metric.Id != "fps")
        {
            return PlotKind.Scatter;
        }

        for (var i = 1; i < xs.Count; i++)
        {
            if (xs[i] - xs[i - 1] > SeriesGeometry.DefaultMinimumGapSeconds)
            {
                return PlotKind.Scatter;
            }
        }

        return PlotKind.SignalXY;
    }

    /// <summary>
    /// Pair mode preserves the theme's A/B colors. Multi mode keeps those two
    /// anchors and then uses a compact color-blind-friendly palette so every
    /// checked benchmark remains distinguishable in the legend and chart.
    /// </summary>
    public static ScottPlot.Color SeriesColor(
        ChartStyle style,
        int index,
        int seriesCount,
        SessionRole role)
    {
        if (seriesCount <= 2)
        {
            return role == SessionRole.Base ? style.SeriesA : style.SeriesB;
        }

        return index switch
        {
            0 => style.SeriesA,
            1 => style.SeriesB,
            _ => ScottPlot.Color.FromHex(MultiPalette[(index - 2) % MultiPalette.Length]),
        };
    }

    public static void Build(
        Plot plot,
        MetricDefinition metric,
        IReadOnlyList<MetricSeries> seriesList,
        ChartStyle style,
        int pointBudget,
        bool showMarkers = false)
    {
        plot.Clear();

        plot.FigureBackground.Color = style.Background;
        plot.DataBackground.Color = style.Background;
        plot.Grid.MajorLineColor = style.Grid.WithAlpha(0.55);
        plot.Grid.MajorLineWidth = 0.6f;
        plot.Axes.Color(style.Muted);

        var unitLabel = string.IsNullOrEmpty(metric.Unit)
            ? metric.Label
            : $"{metric.Label} ({metric.Unit})";
        plot.Axes.Bottom.Label.Text = "Capture time (s)";
        plot.Axes.Bottom.Label.ForeColor = style.Muted;
        plot.Axes.Left.Label.Text = unitLabel;
        plot.Axes.Left.Label.ForeColor = style.Muted;

        // Shaded omitted-load bands render underneath the series lines.
        GapOverlay.Apply(plot, seriesList, style);

        var showLegend = seriesList.Count > 1;
        for (var index = 0; index < seriesList.Count; index++)
        {
            var series = seriesList[index];
            var isReference = index == 0;
            var color = SeriesColor(style, index, seriesList.Count, series.Role);

            // Real omitted ranges must be detected from the original series
            // before decimation. LTTB/min-max intentionally skip samples, and
            // those visualization-only skips must never become NaN line breaks.
            var sourceGaps = SeriesGeometry.FindGaps(series.X);
            var (decimatedX, decimatedY) = Decimation.Select(series.X, series.Y, pointBudget);
            var (gapX, gapY) = SeriesGeometry.InsertGapBreaks(decimatedX, decimatedY, sourceGaps);
            var kind = ChooseKind(metric, decimatedX);

            if (kind == PlotKind.SignalXY)
            {
                var signal = plot.Add.SignalXY(gapX, gapY);
                signal.Color = color;
                signal.LineWidth = isReference ? 2.15f : 1.8f;
                signal.LegendText = series.LabelOrDefault;
                signal.MarkerSize = showMarkers ? 4f : 0f;
                signal.MarkerColor = color;
            }
            else
            {
                var scatter = plot.Add.Scatter(gapX, gapY);
                scatter.Color = color;
                scatter.LineWidth = isReference ? 2.15f : 1.8f;
                scatter.LegendText = series.LabelOrDefault;
                scatter.MarkerSize = showMarkers ? 4f : 0f;
                scatter.MarkerColor = color;
            }

            var average = FrameViewAnalyzer.Core.Math.Statistics.Mean(series.Y);
            if (average is not null)
            {
                var line = plot.Add.HorizontalLine(average.Value, 1.4f, color.WithAlpha(0.85));
                line.LinePattern = LinePattern.Dashed;
            }
        }

        if (showLegend)
        {
            plot.ShowLegend();
            plot.Legend.Alignment = Alignment.LowerRight;
            plot.Legend.Margin = new PixelPadding(10);
            plot.Legend.BackgroundColor = style.Background.WithAlpha(0.92);
            plot.Legend.FontColor = style.Foreground;
            plot.Legend.OutlineColor = style.Grid;
            plot.Legend.OutlineWidth = 1;
        }
        else
        {
            plot.HideLegend();
        }
    }

    public enum PlotKind
    {
        Scatter,
        SignalXY,
    }
}
