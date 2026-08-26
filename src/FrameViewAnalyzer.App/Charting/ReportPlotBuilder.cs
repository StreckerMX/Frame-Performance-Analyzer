using System.IO;
using System.Runtime.CompilerServices;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
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

    private sealed record ReportBuildContext(IReadOnlyList<ReportGroup> Groups);

    private sealed record ReportLegendEntry(string Label, ScottPlot.Color Color);

    private sealed record ReportKpi(string Label, string Value);

    private static readonly ConditionalWeakTable<Multiplot, ReportBuildContext> BuildContexts = new();

    /// <summary>Number of columns used for secondary metrics.</summary>
    public const int GridColumns = 2;

    /// <summary>Height reserved for the principal full-width FPS chart.</summary>
    public const int PrimaryRowHeight = 560;

    /// <summary>Target height of each secondary report row.</summary>
    public const int GridRowHeight = 400;

    /// <summary>Whitespace between adjacent report cells.</summary>
    public const int GridGap = 20;

    /// <summary>
    /// Builds exactly one subplot per report metric. FPS is promoted to the
    /// first plot whenever it is selected so SavePng can give it the principal
    /// full-width row. Other metrics retain their original selection order.
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

        var orderedGroups = groups
            .OrderBy(group => group.Metric.Id == "fps" ? 0 : 1)
            .ToList();

        foreach (var group in orderedGroups)
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

            // Exported reports are often viewed fitted-to-window, so use
            // slightly larger typography than the interactive chart.
            plot.Axes.Title.Label.FontSize = group.Metric.Id == "fps" ? 20 : 18;
            plot.Axes.Left.Label.FontSize = 14;
            plot.Axes.Bottom.Label.FontSize = 14;
            plot.Axes.Left.TickLabelStyle.FontSize = 11;
            plot.Axes.Bottom.TickLabelStyle.FontSize = 11;

            // Omitted-load bands are part of the report context too.
            GapOverlay.Apply(plot, group.Series, style);

            foreach (var series in group.Series)
            {
                var color = SeriesColor(group, series, style);
                var (decimatedX, decimatedY) = Decimation.Select(series.X, series.Y, pointBudget);
                var signal = plot.Add.SignalXY(decimatedX, decimatedY);
                signal.Color = color;
                signal.LineWidth = group.IsMultiWorkspace
                    ? 1.9f
                    : series.Role == SessionRole.Base ? 2.15f : 1.8f;
                signal.LegendText = series.LabelOrDefault;
            }

            // Keep legends configured here for direct Build() consumers and
            // regression tests. SavePng replaces them with one global report
            // legend when the export series carry explicit benchmark labels.
            plot.ShowLegend();
            plot.Legend.BackgroundColor = style.Background.WithAlpha(0.92);
            plot.Legend.FontColor = style.Foreground;
            plot.Legend.FontSize = 12;
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

        BuildContexts.Add(multiplot, new ReportBuildContext(orderedGroups));
        return multiplot;
    }

    /// <summary>
    /// Renders the PNG. FPS, when present, becomes the principal full-width
    /// chart. Secondary metrics use a compact two-column grid and an unpaired
    /// final metric spans the complete last row. Explicit benchmark labels are
    /// promoted into one global header legend, and Pair FPS exports gain a
    /// compact Average / 1% Low / 0.1% Low / Max / Min summary row.
    /// </summary>
    public static void SavePng(
        Multiplot multiplot,
        ChartStyle style,
        ReportHeader? header,
        string path,
        int width,
        int height)
    {
        BuildContexts.TryGetValue(multiplot, out var context);
        var legendEntries = BuildGlobalLegend(context, style);
        var kpis = BuildFpsKpis(context);
        var headerHeight = header is null
            ? 0
            : MeasureHeaderHeight(header, legendEntries.Count, kpis.Count);
        var panelCount = multiplot.Subplots.Count;
        var hasPrimaryFps = HasPrimaryFps(context);
        var renderHeight = panelCount == 0
            ? System.Math.Max(height, headerHeight)
            : RecommendedHeight(panelCount, headerHeight, hasPrimaryFps);

        // A single report-level legend is easier to read and leaves more data
        // area inside every chart. Preserve per-panel legends only for legacy /
        // test plots that do not carry explicit benchmark labels.
        if (legendEntries.Count > 0)
        {
            for (var i = 0; i < panelCount; i++)
            {
                multiplot.Subplots.GetPlot(i).HideLegend();
            }
        }

        using var bitmap = new SKBitmap(width, renderHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ToSkColor(style.Background));

        RenderPanels(canvas, multiplot, width, renderHeight, headerHeight, hasPrimaryFps);
        if (header is not null)
        {
            DrawHeader(canvas, header, style, width, headerHeight, legendEntries, kpis);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Returns the compact export height. A lone chart remains large. If FPS
    /// is the primary plot it gets its own taller row before the secondary
    /// two-column grid.
    /// </summary>
    public static int RecommendedHeight(
        int panelCount,
        int headerHeight,
        bool hasPrimaryFps = false)
    {
        if (panelCount <= 0)
        {
            return System.Math.Max(headerHeight, 1);
        }

        if (panelCount == 1)
        {
            return headerHeight + PrimaryRowHeight;
        }

        if (hasPrimaryFps)
        {
            var secondaryCount = panelCount - 1;
            var secondaryRows = (secondaryCount + GridColumns - 1) / GridColumns;
            return headerHeight
                + PrimaryRowHeight
                + GridGap
                + secondaryRows * GridRowHeight
                + System.Math.Max(0, secondaryRows - 1) * GridGap;
        }

        var rows = (panelCount + GridColumns - 1) / GridColumns;
        return headerHeight
            + rows * GridRowHeight
            + System.Math.Max(0, rows - 1) * GridGap;
    }

    /// <summary>
    /// Pure report geometry for the adaptive metric grid. If FPS is primary,
    /// panel zero spans the full first row and all remaining panels begin below
    /// it in two columns. A final unpaired secondary metric spans the full row.
    /// </summary>
    public static IReadOnlyList<PixelRect> ReportPanelRects(
        int width,
        int height,
        int headerHeight,
        int panelCount,
        bool hasPrimaryFps = false)
    {
        if (panelCount <= 0)
        {
            return [];
        }

        if (panelCount == 1)
        {
            return [new PixelRect(0, width, height, headerHeight)];
        }

        if (hasPrimaryFps)
        {
            var rects = new List<PixelRect>(panelCount)
            {
                new(0, width, headerHeight + PrimaryRowHeight, headerHeight),
            };

            var secondaryCount = panelCount - 1;
            var rows = (secondaryCount + GridColumns - 1) / GridColumns;
            var secondaryTop = headerHeight + PrimaryRowHeight + GridGap;
            AddGridRects(
                rects,
                width,
                height,
                secondaryTop,
                secondaryCount,
                rows,
                sourceIndexOffset: 1);
            return rects;
        }

        var plainRows = (panelCount + GridColumns - 1) / GridColumns;
        var plainRects = new List<PixelRect>(panelCount);
        AddGridRects(
            plainRects,
            width,
            height,
            headerHeight,
            panelCount,
            plainRows,
            sourceIndexOffset: 0);
        return plainRects;
    }

    private static void AddGridRects(
        List<PixelRect> rects,
        int width,
        int height,
        int gridTop,
        int itemCount,
        int rows,
        int sourceIndexOffset)
    {
        var totalHorizontalGap = GridGap;
        var columnWidth = (width - totalHorizontalGap) / GridColumns;
        var totalVerticalGap = System.Math.Max(0, rows - 1) * GridGap;
        var contentHeight = System.Math.Max(rows, height - gridTop - totalVerticalGap);
        var rowHeight = contentHeight / rows;

        for (var localIndex = 0; localIndex < itemCount; localIndex++)
        {
            var row = localIndex / GridColumns;
            var column = localIndex % GridColumns;
            var finalUnpaired = itemCount % GridColumns == 1
                && localIndex == itemCount - 1;

            var left = finalUnpaired ? 0 : column * (columnWidth + GridGap);
            var right = finalUnpaired
                ? width
                : column == GridColumns - 1 ? width : left + columnWidth;
            var top = gridTop + row * (rowHeight + GridGap);
            var bottom = row == rows - 1 ? height : top + rowHeight;

            _ = sourceIndexOffset; // documents that these rects map after the primary panel.
            rects.Add(new PixelRect(left, right, bottom, top));
        }
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
        int headerHeight,
        bool hasPrimaryFps = false)
    {
        var count = multiplot.Subplots.Count;
        if (count == 0)
        {
            return;
        }

        var rects = ReportPanelRects(width, height, headerHeight, count, hasPrimaryFps);
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
    /// the full content width. Individual secondary metrics may divide this
    /// region into two columns.
    /// </summary>
    public static PixelRect ReportContentRect(int width, int height, int headerHeight) =>
        new(0, width, height, headerHeight);

    /// <summary>Pure header band height without report-derived legend/KPI rows.</summary>
    public static int MeasureHeaderHeight(ReportHeader header) =>
        MeasureHeaderHeight(header, legendEntryCount: 0, kpiCount: 0);

    private static int MeasureHeaderHeight(
        ReportHeader header,
        int legendEntryCount,
        int kpiCount)
    {
        using var titleFont = CreateTitleFont();
        using var lineFont = CreateLineFont();
        var lines = EffectiveHeaderLines(header, legendEntryCount > 0);
        var height = HeaderPadding
            + LineHeight(titleFont)
            + HeaderTitleGap
            + lines.Count * LineHeight(lineFont);

        if (legendEntryCount > 0)
        {
            var legendRows = (legendEntryCount + LegendMaxColumns - 1) / LegendMaxColumns;
            height += HeaderSectionGap + legendRows * LegendRowHeight;
        }

        if (kpiCount > 0)
        {
            height += HeaderSectionGap + KpiRowHeight;
        }

        return height + HeaderBottomMargin + HeaderSeparationGap;
    }

    /// <summary>Explicit separation between the last header pixel and the first panel.</summary>
    public const int HeaderSeparationGap = 8;

    private const int HeaderPadding = 12;
    private const int HeaderTitleGap = 6;
    private const int HeaderSectionGap = 10;
    private const int HeaderBottomMargin = 0;
    private const int LegendMaxColumns = 4;
    private const int LegendRowHeight = 24;
    private const int KpiRowHeight = 58;
    private const int KpiGap = 8;

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

    private static SKFont CreateLegendFont() => new()
    {
        Size = 14,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateKpiLabelFont() => new()
    {
        Size = 11,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateKpiValueFont() => new()
    {
        Size = 17,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
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
        int headerHeight,
        IReadOnlyList<ReportLegendEntry> legendEntries,
        IReadOnlyList<ReportKpi> kpis)
    {
        using var backgroundPaint = new SKPaint { Color = ToSkColor(style.Background) };
        canvas.DrawRect(0, 0, width, headerHeight, backgroundPaint);

        using var titleFont = CreateTitleFont();
        using var lineFont = CreateLineFont();
        using var titlePaint = new SKPaint { Color = ToSkColor(style.Foreground) };
        using var linePaint = new SKPaint { Color = ToSkColor(style.Muted) };

        var titleHeight = LineHeight(titleFont);
        var lineHeight = LineHeight(lineFont);
        var lines = EffectiveHeaderLines(header, legendEntries.Count > 0);

        var titleTop = HeaderPadding;
        var titleBaseline = titleTop - titleFont.Metrics.Ascent;
        canvas.DrawText(header.Title, HeaderPadding, (float)titleBaseline, SKTextAlign.Left, titleFont, titlePaint);

        var cursorY = titleTop + titleHeight + HeaderTitleGap;
        foreach (var line in lines)
        {
            var lineBaseline = cursorY - lineFont.Metrics.Ascent;
            canvas.DrawText(line, HeaderPadding, (float)lineBaseline, SKTextAlign.Left, lineFont, linePaint);
            cursorY += lineHeight;
        }

        if (legendEntries.Count > 0)
        {
            cursorY += HeaderSectionGap;
            DrawGlobalLegend(canvas, legendEntries, style, width, cursorY);
            var legendRows = (legendEntries.Count + LegendMaxColumns - 1) / LegendMaxColumns;
            cursorY += legendRows * LegendRowHeight;
        }

        if (kpis.Count > 0)
        {
            cursorY += HeaderSectionGap;
            DrawKpis(canvas, kpis, style, width, cursorY);
        }
    }

    private static void DrawGlobalLegend(
        SKCanvas canvas,
        IReadOnlyList<ReportLegendEntry> entries,
        ChartStyle style,
        int width,
        int top)
    {
        using var font = CreateLegendFont();
        using var textPaint = new SKPaint { Color = ToSkColor(style.Foreground) };
        var columns = System.Math.Min(LegendMaxColumns, entries.Count);
        var cellWidth = (width - HeaderPadding * 2f) / columns;

        for (var i = 0; i < entries.Count; i++)
        {
            var row = i / LegendMaxColumns;
            var column = i % LegendMaxColumns;
            if (entries.Count < LegendMaxColumns)
            {
                row = 0;
                column = i;
            }

            var x = HeaderPadding + column * cellWidth;
            var y = top + row * LegendRowHeight + LegendRowHeight / 2f;
            using var swatchPaint = new SKPaint
            {
                Color = ToSkColor(entries[i].Color),
                StrokeWidth = 3,
                IsAntialias = true,
            };
            canvas.DrawLine(x, y, x + 28, y, swatchPaint);

            var maxChars = System.Math.Max(10, (int)((cellWidth - 42) / 7.5));
            var label = Truncate(entries[i].Label, maxChars);
            var baseline = y - (font.Metrics.Ascent + font.Metrics.Descent) / 2f;
            canvas.DrawText(label, x + 36, baseline, SKTextAlign.Left, font, textPaint);
        }
    }

    private static void DrawKpis(
        SKCanvas canvas,
        IReadOnlyList<ReportKpi> kpis,
        ChartStyle style,
        int width,
        int top)
    {
        using var labelFont = CreateKpiLabelFont();
        using var valueFont = CreateKpiValueFont();
        using var labelPaint = new SKPaint { Color = ToSkColor(style.Muted) };
        using var valuePaint = new SKPaint { Color = ToSkColor(style.Foreground) };
        using var fillPaint = new SKPaint { Color = ToSkColor(style.Grid.WithAlpha(0.10)) };
        using var borderPaint = new SKPaint
        {
            Color = ToSkColor(style.Grid.WithAlpha(0.85)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true,
        };

        var count = kpis.Count;
        var availableWidth = width - HeaderPadding * 2 - (count - 1) * KpiGap;
        var cardWidth = availableWidth / (float)count;
        for (var i = 0; i < count; i++)
        {
            var left = HeaderPadding + i * (cardWidth + KpiGap);
            var rect = new SKRect(left, top, left + cardWidth, top + KpiRowHeight);
            canvas.DrawRoundRect(rect, 5, 5, fillPaint);
            canvas.DrawRoundRect(rect, 5, 5, borderPaint);

            var labelBaseline = top + 8 - labelFont.Metrics.Ascent;
            canvas.DrawText(kpis[i].Label, left + 10, labelBaseline, SKTextAlign.Left, labelFont, labelPaint);

            var valueBaseline = top + 30 - valueFont.Metrics.Ascent;
            canvas.DrawText(kpis[i].Value, left + 10, valueBaseline, SKTextAlign.Left, valueFont, valuePaint);
        }
    }

    private static IReadOnlyList<string> EffectiveHeaderLines(ReportHeader header, bool hasGlobalLegend)
    {
        if (!hasGlobalLegend)
        {
            return header.Lines;
        }

        // Base / Comparison / Benchmark lines are represented by the colored
        // global legend, so suppress their duplicate text rows in the header.
        return header.Lines
            .Where(line => !line.StartsWith("Base:", StringComparison.Ordinal)
                && !line.StartsWith("Comparison:", StringComparison.Ordinal)
                && !line.StartsWith("Benchmark:", StringComparison.Ordinal))
            .ToList();
    }

    private static IReadOnlyList<ReportLegendEntry> BuildGlobalLegend(
        ReportBuildContext? context,
        ChartStyle style)
    {
        if (context is null || context.Groups.Count == 0)
        {
            return [];
        }

        // Prefer the metric that contains the most selected benchmarks in case
        // one optional telemetry metric is missing from a particular capture.
        var group = context.Groups
            .OrderByDescending(candidate => candidate.Series.Count)
            .FirstOrDefault(candidate => candidate.Series.Count > 0
                && candidate.Series.All(series => !string.IsNullOrWhiteSpace(series.Label)));
        if (group is null)
        {
            return [];
        }

        return group.Series
            .Select(series => new ReportLegendEntry(
                series.Label!,
                SeriesColor(group, series, style)))
            .ToList();
    }

    private static IReadOnlyList<ReportKpi> BuildFpsKpis(ReportBuildContext? context)
    {
        var fps = context?.Groups.FirstOrDefault(group => group.Metric.Id == "fps");
        if (fps is null
            || fps.IsMultiWorkspace
            || fps.Series.Count is < 1 or > 2
            || fps.Series.Any(series => string.IsNullOrWhiteSpace(series.Label)))
        {
            return [];
        }

        var stats = fps.Series
            .Select(series => StatisticsCalculator.Compute(fps.Metric, series.Y))
            .ToList();

        string Values(Func<MetricStatistics, double?> selector)
        {
            var formatted = stats
                .Select(item => selector(item) is { } value ? value.ToString("F1") : "--")
                .ToList();
            return formatted.Count == 2
                ? $"{formatted[0]} → {formatted[1]}"
                : formatted[0];
        }

        return
        [
            new ReportKpi("AVERAGE", Values(item => item.Avg)),
            new ReportKpi("1% LOW", Values(item => item.P1)),
            new ReportKpi("0.1% LOW", Values(item => item.P01)),
            new ReportKpi("MAX", Values(item => item.Max)),
            new ReportKpi("MIN", Values(item => item.Min)),
        ];
    }

    private static bool HasPrimaryFps(ReportBuildContext? context) =>
        context is { Groups.Count: > 0 }
        && context.Groups[0].Metric.Id == "fps";

    private static ScottPlot.Color SeriesColor(
        ReportGroup group,
        MetricSeries series,
        ChartStyle style) =>
        group.IsMultiWorkspace
            ? MultiSeriesPalette.ColorAt(series.WorkspaceIndex)
            : series.Role == SessionRole.Base ? style.SeriesA : style.SeriesB;

    private static string Truncate(string value, int maximumCharacters)
    {
        if (value.Length <= maximumCharacters)
        {
            return value;
        }

        return maximumCharacters <= 1
            ? "…"
            : value[..(maximumCharacters - 1)] + "…";
    }

    private static SKColor ToSkColor(ScottPlot.Color color) =>
        new(color.R, color.G, color.B, color.A);
}
