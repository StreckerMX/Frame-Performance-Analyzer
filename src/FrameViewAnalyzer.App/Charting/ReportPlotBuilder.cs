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
/// one subplot per report metric with the selected benchmark series overlaid.
/// Headless (no WPF types), so the report can be built and saved from tests
/// without touching the interactive chart or its view models.
/// </summary>
public static class ReportPlotBuilder
{
    public sealed record ReportGroup(
        MetricDefinition Metric,
        IReadOnlyList<MetricSeries> Series,
        bool IsMultiWorkspace = false);

    /// <summary>Compact benchmark context shown above the plots.</summary>
    public sealed record ReportHeader(string Title, IReadOnlyList<string> Lines);

    /// <summary>
    /// Builds exactly one subplot per report metric. The compact context
    /// header is NOT a plot — it is drawn as text above the plots by
    /// <see cref="SavePng"/>, so the report never contains an empty
    /// default-axes chart before the real metric charts.
    /// </summary>
    public static Multiplot Build(
        IReadOnlyList<ReportGroup> groups,
        ChartStyle style,
        int pointBudget = 2000)
    {
        var multiplot = new Multiplot();

        // ScottPlot seeds a new Multiplot with one empty subplot; drop it so
        // the report contains exactly one panel per metric and never renders
        // a stray default -10..10 chart.
        multiplot.Subplots.RemoveAt(0);

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
                var color = group.IsMultiWorkspace
                    ? MultiSeriesPalette.ColorAt(series.WorkspaceIndex)
                    : series.Role == SessionRole.Base ? style.SeriesA : style.SeriesB;
                var (decimatedX, decimatedY) = Decimation.Select(series.X, series.Y, pointBudget);
                var signal = plot.Add.SignalXY(decimatedX, decimatedY);
                signal.Color = color;
                signal.LineWidth = group.IsMultiWorkspace
                    ? 1.9f
                    : series.Role == SessionRole.Base ? 2.15f : 1.8f;
                signal.LegendText = series.LabelOrDefault;
            }

            // Every panel with legend text gets the explicit report legend
            // theme — single-series and multi-series alike. ScottPlot's
            // default white legend must never leak into a dark report.
            plot.ShowLegend();
            plot.Legend.BackgroundColor = style.Background.WithAlpha(0.92);
            plot.Legend.FontColor = style.Foreground;
            plot.Legend.OutlineColor = style.Grid;
            plot.Legend.OutlineWidth = 1;

            multiplot.Subplots.Add(plot);

            // One scaling policy, two renderers: report charts fit their axes
            // with the same adaptive full-series bounds as the interactive
            // chart. Limits are set AFTER adding the plot to the multiplot,
            // because the subplot collection re-autoscales on add and would
            // overwrite them (clipping FPS peaks).
            var fitted = ChartViewport.FullSeriesLimits(group.Series, group.Metric.Id == "fps");
            if (fitted is not null)
            {
                plot.Axes.SetLimits(fitted.Value);
            }
        }

        return multiplot;
    }

    /// <summary>
    /// Renders the PNG. The metric panels are composed manually — one direct
    /// <c>Plot.Render</c> per panel into its own bitmap, stacked vertically
    /// full-width below the reserved header band. <c>Multiplot.Render</c> is
    /// deliberately NOT used: it re-autoscales subplot axes at render time,
    /// which overwrote the fitted FPS limits and clipped valid data.
    /// The compact text header is drawn ON TOP afterwards.
    /// </summary>
    public static void SavePng(
        Multiplot multiplot,
        ChartStyle style,
        ReportHeader? header,
        string path,
        int width,
        int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        var headerHeight = header is null ? 0 : MeasureHeaderHeight(header);

        RenderPanels(canvas, multiplot, width, height, headerHeight);
        if (header is not null)
        {
            DrawHeader(canvas, header, style, width, headerHeight);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Renders every metric panel full-width and stacked vertically below the
    /// header. Each panel is rendered into its own bitmap first so per-plot
    /// canvas clears stay confined to that panel.
    /// </summary>
    public static void RenderPanels(
        SKCanvas canvas,
        Multiplot multiplot,
        int width,
        int height,
        int headerHeight)
    {
        var count = multiplot.Subplots.Count;
        if (count == 0)
        {
            return;
        }

        var panelHeight = (height - headerHeight) / count;
        for (var i = 0; i < count; i++)
        {
            var panelTop = headerHeight + i * panelHeight;
            var panelBottom = i == count - 1 ? height : panelTop + panelHeight;
            var currentHeight = panelBottom - panelTop;

            using var panelBitmap = new SKBitmap(width, currentHeight);
            using var panelCanvas = new SKCanvas(panelBitmap);
            multiplot.Subplots.GetPlot(i).Render(
                panelCanvas,
                new PixelRect(0, width, currentHeight, 0));
            canvas.DrawBitmap(panelBitmap, 0, panelTop);
        }
    }

    /// <summary>
    /// Pure report geometry: the plot region starts below the header and spans
    /// the FULL content width. Extracted for direct layout tests.
    /// </summary>
    public static PixelRect ReportContentRect(int width, int height, int headerHeight) =>
        new(0, width, height, headerHeight);

    /// <summary>Pure header band height in pixels for the given header content.</summary>
    public static int MeasureHeaderHeight(ReportHeader header)
    {
        using var titleFont = CreateTitleFont();
        using var lineFont = CreateLineFont();
        return HeaderPadding + LineHeight(titleFont) + HeaderTitleGap
            + header.Lines.Count * LineHeight(lineFont)
            + HeaderBottomMargin
            + HeaderSeparationGap;
    }

    /// <summary>Explicit separation between the last header pixel and the first panel.</summary>
    public const int HeaderSeparationGap = 12;

    private const int HeaderPadding = 16;
    private const int HeaderTitleGap = 12;
    private const int HeaderBottomMargin = 4;

    private static SKFont CreateTitleFont() => new()
    {
        Size = 22,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateLineFont() => new()
    {
        Size = 14,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal),
        Edging = SKFontEdging.Antialias,
    };

    /// <summary>Full glyph height (ascent + descent) for the given font.</summary>
    private static int LineHeight(SKFont font)
    {
        var metrics = font.Metrics;
        return (int)System.Math.Ceiling(metrics.Descent - metrics.Ascent) + 2;
    }

    private static void DrawHeader(
        SKCanvas canvas,
        ReportHeader header,
        ChartStyle style,
        int width,
        int headerHeight)
    {
        using var backgroundPaint = new SKPaint { Color = ToSkColor(style.Background) };
        canvas.DrawRect(0, 0, width, headerHeight, backgroundPaint);

        using var titleFont = CreateTitleFont();
        using var lineFont = CreateLineFont();
        using var titlePaint = new SKPaint { Color = ToSkColor(style.Foreground) };
        using var linePaint = new SKPaint { Color = ToSkColor(style.Muted) };

        var titleHeight = LineHeight(titleFont);
        var lineHeight = LineHeight(lineFont);

        // Baseline math uses the actual font metrics; the reserved band ends
        // with HeaderSeparationGap before the first panel, so the last glyph
        // pixel always stays strictly above the first chart.
        var titleTop = HeaderPadding;
        var titleBaseline = titleTop - titleFont.Metrics.Ascent;
        canvas.DrawText(header.Title, HeaderPadding, (float)titleBaseline, SKTextAlign.Left, titleFont, titlePaint);

        var linesTop = titleTop + titleHeight + HeaderTitleGap;
        for (var i = 0; i < header.Lines.Count; i++)
        {
            var lineBaseline = linesTop + i * lineHeight - lineFont.Metrics.Ascent;
            canvas.DrawText(header.Lines[i], HeaderPadding, (float)lineBaseline, SKTextAlign.Left, lineFont, linePaint);
        }
    }

    private static SKColor ToSkColor(ScottPlot.Color color) =>
        new(color.R, color.G, color.B, color.A);
}
