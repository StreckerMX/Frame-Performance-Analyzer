using System.IO;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Charting;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;
using SkiaSharp;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>
/// Renders the multi-chart PNG report: a compact context header followed by
/// one subplot per report metric with the Base/Comparison series overlaid.
/// Headless (no WPF types), so the report can be built and saved from tests
/// without touching the interactive chart or its view models.
/// </summary>
public static class ReportPlotBuilder
{
    public sealed record ReportGroup(
        MetricDefinition Metric,
        IReadOnlyList<MetricSeries> Series);

    /// <summary>Compact benchmark context shown above the plots.</summary>
    public sealed record ReportHeader(string Title, IReadOnlyList<string> Lines);

    public static Multiplot Build(
        IReadOnlyList<ReportGroup> groups,
        ChartStyle style,
        ReportHeader? header = null,
        int pointBudget = 2000)
    {
        var multiplot = new Multiplot();
        if (header is not null)
        {
            var headerPlot = new Plot();
            headerPlot.FigureBackground.Color = style.Background;
            headerPlot.DataBackground.Color = style.Background;
            headerPlot.HideAxesAndGrid();
            headerPlot.Title(string.Join("\n", new[] { header.Title }.Concat(header.Lines)), 16);
            multiplot.Subplots.Add(headerPlot);
        }

        foreach (var group in groups)
        {
            var plot = new Plot();
            plot.FigureBackground.Color = style.Background;
            plot.DataBackground.Color = style.Background;
            plot.Grid.MajorLineColor = style.Grid.WithAlpha(0.55);
            plot.Grid.MajorLineWidth = 0.6f;
            plot.Axes.Color(style.Muted);
            plot.Axes.Bottom.Label.Text = "Capture time (s)";
            plot.Axes.Bottom.Label.ForeColor = style.Muted;
            var unitLabel = string.IsNullOrEmpty(group.Metric.Unit)
                ? group.Metric.Label
                : $"{group.Metric.Label} ({group.Metric.Unit})";
            plot.Axes.Left.Label.Text = unitLabel;
            plot.Axes.Left.Label.ForeColor = style.Muted;
            plot.Title(group.Metric.Label);

            // Omitted-load bands are part of the report context too.
            GapOverlay.Apply(plot, group.Series, style);

            foreach (var series in group.Series)
            {
                var color = series.Role == SessionRole.Base ? style.SeriesA : style.SeriesB;
                var (decimatedX, decimatedY) = Decimation.Select(series.X, series.Y, pointBudget);
                var signal = plot.Add.SignalXY(decimatedX, decimatedY);
                signal.Color = color;
                signal.LineWidth = series.Role == SessionRole.Base ? 2.15f : 1.8f;
                signal.LegendText = series.LabelOrDefault;
            }

            if (group.Series.Count > 1)
            {
                plot.ShowLegend();
                plot.Legend.BackgroundColor = style.Background.WithAlpha(0.92);
                plot.Legend.FontColor = style.Foreground;
            }

            multiplot.Subplots.Add(plot);
        }

        return multiplot;
    }

    public static void SavePng(Multiplot multiplot, string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        multiplot.Render(canvas, new PixelRect(0, width, height, 0));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }
}
