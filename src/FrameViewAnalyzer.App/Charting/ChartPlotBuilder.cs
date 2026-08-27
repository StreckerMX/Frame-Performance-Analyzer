using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Charting;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>
/// Maps analytics series onto a ScottPlot plot: visualization-only decimation,
/// per-metric plot kinds, average lines, and theme styling. Pure plot assembly
/// with no WPF types beyond ScottPlot, so it is headless-testable.
/// </summary>
public static class ChartPlotBuilder
{
    /// <summary>
    /// Per-metric plot kind: FPS series are uniformly one-second spaced, so
    /// they render as SignalXY (constant-time rendering). Other metrics render
    /// as Scatter because individual bins can be absent for that metric.
    /// </summary>
    public static PlotKind ChooseKind(MetricDefinition metric, IReadOnlyList<double> xs) =>
        metric.Id == "fps" ? PlotKind.SignalXY : PlotKind.Scatter;

    /// <summary>
    /// Pair mode preserves the theme's Base/Comparison colors. Multi mode uses
    /// the shared benchmark palette, with every checked benchmark styled as an
    /// equal peer rather than giving the first line reference semantics.
    /// </summary>
    public static ScottPlot.Color SeriesColor(
        ChartStyle style,
        int index,
        int seriesCount,
        SessionRole role,
        bool isMultiWorkspace = false)
    {
        if (!isMultiWorkspace && seriesCount <= 2)
        {
            return role == SessionRole.Base ? style.SeriesA : style.SeriesB;
        }

        return MultiSeriesPalette.ColorAt(index);
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

        // ScottPlot's major grid lines are generated from the same major ticks
        // that receive numeric axis labels. Keep only those lines so every
        // visible number on X/Y has one matching grid line and there are no
        // unlabeled minor-grid stripes between them.
        plot.Grid.MajorLineColor = style.Grid.WithAlpha(0.55);
        plot.Grid.MajorLineWidth = 0.6f;
        plot.Grid.MinorLineWidth = 0f;
        plot.Axes.Color(style.Muted);

        var unitLabel = string.IsNullOrEmpty(metric.Unit)
            ? metric.Label
            : $"{metric.Label} ({metric.Unit})";
        plot.Axes.Bottom.Label.Text = "Analyzed time (s)";
        plot.Axes.Bottom.Label.ForeColor = style.Muted;
        plot.Axes.Left.Label.Text = unitLabel;
        plot.Axes.Left.Label.ForeColor = style.Muted;

        var showLegend = seriesList.Count > 1;
        var isMultiWorkspace = seriesList.Count > 1
            && seriesList.All(series => !series.IsReference && series.Role == SessionRole.Comparison);

        for (var index = 0; index < seriesList.Count; index++)
        {
            var series = seriesList[index];
            var color = SeriesColor(
                style,
                series.WorkspaceIndex,
                seriesList.Count,
                series.Role,
                isMultiWorkspace);
            var lineWidth = isMultiWorkspace
                ? 1.9f
                : series.Role == SessionRole.Base ? 2.15f : 1.8f;

            var (renderX, renderY) = Decimation.Select(series.X, series.Y, pointBudget);
            var kind = ChooseKind(metric, renderX);

            if (kind == PlotKind.SignalXY)
            {
                var signal = plot.Add.SignalXY(renderX, renderY);
                signal.Color = color;
                signal.LineWidth = lineWidth;
                signal.LegendText = series.LabelOrDefault;
                signal.MarkerSize = showMarkers ? 4f : 0f;
                signal.MarkerColor = color;
            }
            else
            {
                var scatter = plot.Add.Scatter(renderX, renderY);
                scatter.Color = color;
                scatter.LineWidth = lineWidth;
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
