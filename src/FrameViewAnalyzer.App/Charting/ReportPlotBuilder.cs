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

    /// <summary>Number of columns used when more than one metric is exported.</summary>
    public const int GridColumns = 2;

    /// <summary>Target height of each report row in the adaptive grid.</summary>
    public const int GridRowHeight = 520;

    /// <summary>Whitespace between adjacent report cells.</summary>
    public const int GridGap = 20;

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
    /// Renders the PNG. One metric keeps the familiar full-width presentation;
    /// multiple metrics use a compact two-column grid. When the metric count is
    /// odd, the final metric spans the complete row instead of leaving a blank
    /// cell. The requested height is retained only for header-only reports;
    /// chart reports use <see cref="RecommendedHeight"/> so selecting more
    /// metrics does not create an excessively tall image.
    /// </summary>
    public static void SavePng(
        Multiplot multiplot,
        ChartStyle style,
        ReportHeader? header,
        string path,
        int width,
        int height)
    {
        var headerHeight = header is null ? 0 : MeasureHeaderHeight(header);
        var panelCount = multiplot.Subplots.Count;
        var renderHeight = panelCount == 0
            ? System.Math.Max(height, headerHeight)
            : RecommendedHeight(panelCount, headerHeight);

        using var bitmap = new SKBitmap(width, renderHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ToSkColor(style.Background));

        RenderPanels(canvas, multiplot, width, renderHeight, headerHeight);
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
    /// Returns the compact export height for the current panel count. One
    /// metric occupies one full-width row; two or more metrics use two columns.
    /// </summary>
    public static int RecommendedHeight(int panelCount, int headerHeight)
    {
        if (panelCount <= 0)
        {
            return System.Math.Max(headerHeight, 1);
        }

        var columns = panelCount == 1 ? 1 : GridColumns;
        var rows = (panelCount + columns - 1) / columns;
        return headerHeight
            + rows * GridRowHeight
            + System.Math.Max(0, rows - 1) * GridGap;
    }

    /// <summary>
    /// Pure report geometry for the adaptive metric grid. Two or more metrics
    /// use two columns; a final unpaired metric spans the full report width.
    /// </summary>
    public static IReadOnlyList<PixelRect> ReportPanelRects(
        int width,
        int height,
        int headerHeight,
        int panelCount)
    {
        if (panelCount <= 0)
        {
            return [];
        }

        var columns = panelCount == 1 ? 1 : GridColumns;
        var rows = (panelCount + columns - 1) / columns;
        var horizontalGap = columns > 1 ? GridGap : 0;
        var totalHorizontalGap = (columns - 1) * horizontalGap;
        var columnWidth = (width - totalHorizontalGap) / columns;

        var totalVerticalGap = System.Math.Max(0, rows - 1) * GridGap;
        var contentHeight = System.Math.Max(rows, height - headerHeight - totalVerticalGap);
        var rowHeight = contentHeight / rows;

        var rects = new List<PixelRect>(panelCount);
        for (var i = 0; i < panelCount; i++)
        {
            var row = i / columns;
            var column = i % columns;
            var finalUnpaired = columns == GridColumns
                && panelCount % GridColumns == 1
                && i == panelCount - 1;

            var left = finalUnpaired ? 0 : column * (columnWidth + horizontalGap);
            var right = finalUnpaired
                ? width
                : column == columns - 1 ? width : left + columnWidth;
            var top = headerHeight + row * (rowHeight + GridGap);
            var bottom = row == rows - 1 ? height : top + rowHeight;

            rects.Add(new PixelRect(left, right, bottom, top));
        }

        return rects;
    }

    /// <summary>
    /// Renders every metric panel into its own bitmap and places it in the
    /// adaptive report grid. Per-plot canvas clears therefore remain confined
    /// to their own cell and cannot overwrite the header or neighboring plots.
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

        var rects = ReportPanelRects(width, height, headerHeight, count);
        for (var i = 0; i < count; i++)
        {
            var rect = rects[i];
            var currentWidth = System.Math.Max(1, (int)(rect.Right - rect.Left));
            var currentHeight = System.Math.Max(1, (int)(rect.Bottom - rect.Top));

            using var panelBitmap = new SKBitmap(currentWidth, currentHeight);
            using var panelCanvas = new SKCanvas(panelBitmap);
            multiplot.Subplots.GetPlot(i).Render(
                panelCanvas,
                new PixelRect(0, currentWidth, currentHeight, 0));
            canvas.DrawBitmap(panelBitmap, rect.Left, rect.Top);
        }
    }

    /// <summary>
    /// Pure report geometry: the plot region starts below the header and spans
    /// the full content width. Individual metrics may divide this region into
    /// two columns when multiple plots are exported.
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
