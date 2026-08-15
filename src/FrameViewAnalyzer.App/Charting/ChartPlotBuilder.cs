using FrameViewAnalyzer.Analytics.Series;
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

        var showLegend = seriesList.Count > 1;
        for (var index = 0; index < seriesList.Count; index++)
        {
            var series = seriesList[index];
            var color = index == 0 ? style.SeriesA : style.SeriesB;

            var (decimatedX, decimatedY) = Decimation.Select(series.X, series.Y, pointBudget);
            var (gapX, gapY) = SeriesGeometry.InsertGapBreaks(decimatedX, decimatedY);
            var kind = ChooseKind(metric, decimatedX);

            if (kind == PlotKind.SignalXY)
            {
                var signal = plot.Add.SignalXY(gapX, gapY);
                signal.Color = color;
                signal.LineWidth = 2.15f;
                signal.LegendText = series.LabelOrDefault;
                signal.MarkerSize = showMarkers ? 4f : 0f;
                signal.MarkerColor = color;
            }
            else
            {
                var scatter = plot.Add.Scatter(gapX, gapY);
                scatter.Color = color;
                scatter.LineWidth = index == 0 ? 2.15f : 1.8f;
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
            plot.Legend.BackgroundColor = style.Background.WithAlpha(0.92);
            plot.Legend.FontColor = style.Foreground;
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
