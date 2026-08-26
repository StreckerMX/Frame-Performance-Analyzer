using System.IO;
using System.Runtime.CompilerServices;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Charting;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;
using SkiaSharp;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>
/// Renders benchmark PNG reports. The chart assembly remains headless and the
/// export renderer can enrich real application exports with the source session
/// context carried by each MetricSeries.
/// </summary>
public static class ReportPlotBuilder
{
    public sealed record ReportGroup(
        MetricDefinition Metric,
        IReadOnlyList<MetricSeries> Series,
        bool IsMultiWorkspace = false);

    /// <summary>Report title plus legacy/free-form header lines.</summary>
    public sealed record ReportHeader(string Title, IReadOnlyList<string> Lines);

    private sealed record ReportBuildContext(IReadOnlyList<ReportGroup> Groups);

    private sealed record ReportRunContext(
        string Role,
        string Label,
        ScottPlot.Color Color,
        SessionAnalysis? Session);

    private sealed record ProfessionalReportContext(
        string MainTitle,
        string ReportType,
        string CommonContext,
        IReadOnlyList<ReportRunContext> Runs,
        string Methodology,
        int MetricCount);

    private static readonly ConditionalWeakTable<Multiplot, ReportBuildContext> BuildContexts = new();

    /// <summary>Number of columns used for secondary metrics.</summary>
    public const int GridColumns = 2;

    /// <summary>Height reserved for the principal full-width FPS chart.</summary>
    public const int PrimaryRowHeight = 560;

    /// <summary>Target height of each secondary report row.</summary>
    public const int GridRowHeight = 400;

    /// <summary>Whitespace between adjacent report cells.</summary>
    public const int GridGap = 20;

    private const int ProfessionalFooterHeight = 42;
    private const int ProfessionalHeaderPadding = 24;
    private const int ProfessionalTitleHeight = 40;
    private const int ProfessionalContextHeight = 24;
    private const int ProfessionalRunCardHeight = 104;
    private const int ProfessionalRunCardGap = 12;
    private const int ProfessionalMethodHeight = 42;
    private const int ProfessionalHeaderGap = 18;
    private const int ProfessionalMaxRunColumns = 4;

    // Legacy header geometry is retained for direct renderer tests and callers
    // which provide arbitrary titles rather than the normal benchmark-report
    // title. Real BENCHMARK COMPARISON exports use the professional layout.
    private const int LegacyHeaderPadding = 16;
    private const int LegacyHeaderTitleGap = 12;
    private const int LegacyHeaderBottomMargin = 4;

    /// <summary>Explicit separation between the header and first chart.</summary>
    public const int HeaderSeparationGap = 12;

    /// <summary>
    /// Builds exactly one subplot per metric. FPS is promoted to the first plot
    /// whenever selected, making it the principal full-width chart in exports.
    /// </summary>
    public static Multiplot Build(
        IReadOnlyList<ReportGroup> groups,
        ChartStyle style,
        int pointBudget = 2000)
    {
        var multiplot = new Multiplot();
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

            plot.Axes.Title.Label.FontSize = group.Metric.Id == "fps" ? 20 : 18;
            plot.Axes.Left.Label.FontSize = 14;
            plot.Axes.Bottom.Label.FontSize = 14;
            plot.Axes.Left.TickLabelStyle.FontSize = 11;
            plot.Axes.Bottom.TickLabelStyle.FontSize = 11;

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

            // Keep a correctly themed legend on the Plot itself for direct
            // Build() consumers. SavePng hides it when the professional report
            // header can act as the benchmark key instead.
            plot.ShowLegend();
            plot.Legend.BackgroundColor = style.Background.WithAlpha(0.92);
            plot.Legend.FontColor = style.Foreground;
            plot.Legend.FontSize = 12;
            plot.Legend.OutlineColor = style.Grid;
            plot.Legend.OutlineWidth = 1;

            multiplot.Subplots.Add(plot);

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
    /// Saves a PNG. Normal Pair/Multi report titles use the structured report
    /// layout: shared title/context, benchmark cards, methodology, FPS-first
    /// chart hierarchy, and a restrained footer. Arbitrary titles retain the
    /// compact legacy header so renderer regression tests remain stable.
    /// </summary>
    public static void SavePng(
        Multiplot multiplot,
        ChartStyle style,
        ReportHeader? header,
        string path,
        int width,
        int height)
    {
        BuildContexts.TryGetValue(multiplot, out var buildContext);
        var panelCount = multiplot.Subplots.Count;
        var hasPrimaryFps = HasPrimaryFps(buildContext);
        var professional = header is not null && IsBenchmarkReportTitle(header.Title);

        if (professional)
        {
            SaveProfessionalPng(
                multiplot,
                style,
                header!,
                buildContext,
                path,
                width,
                panelCount,
                hasPrimaryFps);
            return;
        }

        SaveLegacyPng(
            multiplot,
            style,
            header,
            path,
            width,
            height,
            panelCount,
            hasPrimaryFps);
    }

    private static void SaveProfessionalPng(
        Multiplot multiplot,
        ChartStyle style,
        ReportHeader header,
        ReportBuildContext? buildContext,
        string path,
        int width,
        int panelCount,
        bool hasPrimaryFps)
    {
        var context = BuildProfessionalContext(header, buildContext, style);
        var headerHeight = MeasureProfessionalHeaderHeight(context, width);
        var bodyBottom = panelCount == 0
            ? headerHeight
            : RecommendedHeight(panelCount, headerHeight, hasPrimaryFps);
        var renderHeight = bodyBottom + ProfessionalFooterHeight;

        // Benchmark cards are the report legend, so chart-local copies are
        // redundant and consume valuable plotting area.
        if (context.Runs.Count > 0)
        {
            for (var i = 0; i < panelCount; i++)
            {
                multiplot.Subplots.GetPlot(i).HideLegend();
            }
        }

        using var bitmap = new SKBitmap(width, renderHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ToSkColor(style.Background));

        if (panelCount > 0)
        {
            RenderPanels(canvas, multiplot, width, bodyBottom, headerHeight, hasPrimaryFps);
        }

        DrawProfessionalHeader(canvas, context, style, width, headerHeight);
        DrawProfessionalFooter(canvas, context, style, width, bodyBottom, renderHeight);

        SaveBitmap(bitmap, path);
    }

    private static void SaveLegacyPng(
        Multiplot multiplot,
        ChartStyle style,
        ReportHeader? header,
        string path,
        int width,
        int requestedHeight,
        int panelCount,
        bool hasPrimaryFps)
    {
        var headerHeight = header is null ? 0 : MeasureHeaderHeight(header);
        var renderHeight = panelCount == 0
            ? System.Math.Max(requestedHeight, headerHeight)
            : RecommendedHeight(panelCount, headerHeight, hasPrimaryFps);

        using var bitmap = new SKBitmap(width, renderHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ToSkColor(style.Background));

        if (panelCount > 0)
        {
            RenderPanels(canvas, multiplot, width, renderHeight, headerHeight, hasPrimaryFps);
        }

        if (header is not null)
        {
            DrawLegacyHeader(canvas, header, style, width, headerHeight);
        }

        SaveBitmap(bitmap, path);
    }

    private static void SaveBitmap(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Returns the compact export height. A lone chart remains large. If FPS
    /// is primary it gets its own taller row before the secondary grid.
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
    /// Pure report geometry. FPS can occupy the complete first row and
    /// secondary metrics use two columns; an unpaired final metric spans wide.
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
            AddGridRects(rects, width, height, secondaryTop, secondaryCount, rows);
            return rects;
        }

        var plainRows = (panelCount + GridColumns - 1) / GridColumns;
        var plainRects = new List<PixelRect>(panelCount);
        AddGridRects(plainRects, width, height, headerHeight, panelCount, plainRows);
        return plainRects;
    }

    private static void AddGridRects(
        List<PixelRect> rects,
        int width,
        int height,
        int gridTop,
        int itemCount,
        int rows)
    {
        var columnWidth = (width - GridGap) / GridColumns;
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

            rects.Add(new PixelRect(left, right, bottom, top));
        }
    }

    /// <summary>Renders each metric into its own isolated bitmap cell.</summary>
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

    /// <summary>Full plot content rectangle below a caller-provided header.</summary>
    public static PixelRect ReportContentRect(int width, int height, int headerHeight) =>
        new(0, width, height, headerHeight);

    /// <summary>Legacy/free-form header measurement retained as public API.</summary>
    public static int MeasureHeaderHeight(ReportHeader header)
    {
        using var titleFont = CreateLegacyTitleFont();
        using var lineFont = CreateLegacyLineFont();
        return LegacyHeaderPadding
            + LineHeight(titleFont)
            + LegacyHeaderTitleGap
            + header.Lines.Count * LineHeight(lineFont)
            + LegacyHeaderBottomMargin
            + HeaderSeparationGap;
    }

    private static void DrawLegacyHeader(
        SKCanvas canvas,
        ReportHeader header,
        ChartStyle style,
        int width,
        int headerHeight)
    {
        using var backgroundPaint = new SKPaint { Color = ToSkColor(style.Background) };
        canvas.DrawRect(0, 0, width, headerHeight, backgroundPaint);

        using var titleFont = CreateLegacyTitleFont();
        using var lineFont = CreateLegacyLineFont();
        using var titlePaint = new SKPaint { Color = ToSkColor(style.Foreground) };
        using var linePaint = new SKPaint { Color = ToSkColor(style.Muted) };

        var titleHeight = LineHeight(titleFont);
        var lineHeight = LineHeight(lineFont);
        var titleTop = LegacyHeaderPadding;
        var titleBaseline = titleTop - titleFont.Metrics.Ascent;
        canvas.DrawText(header.Title, LegacyHeaderPadding, titleBaseline, SKTextAlign.Left, titleFont, titlePaint);

        var linesTop = titleTop + titleHeight + LegacyHeaderTitleGap;
        for (var i = 0; i < header.Lines.Count; i++)
        {
            var baseline = linesTop + i * lineHeight - lineFont.Metrics.Ascent;
            canvas.DrawText(header.Lines[i], LegacyHeaderPadding, baseline, SKTextAlign.Left, lineFont, linePaint);
        }
    }

    private static ProfessionalReportContext BuildProfessionalContext(
        ReportHeader header,
        ReportBuildContext? buildContext,
        ChartStyle style)
    {
        var runs = BuildRunContexts(buildContext, style);
        var sessions = runs
            .Select(run => run.Session)
            .Where(session => session is not null)
            .Cast<SessionAnalysis>()
            .ToList();

        var isMulti = buildContext?.Groups.FirstOrDefault()?.IsMultiWorkspace == true
            || header.Title.Contains("MULTI BENCHMARK COMPARISON", StringComparison.OrdinalIgnoreCase);
        var reportType = isMulti ? "MULTI BENCHMARK COMPARISON" : "BENCHMARK COMPARISON";
        var isDefaultTitle = string.Equals(header.Title.Trim(), reportType, StringComparison.OrdinalIgnoreCase);

        var commonApplication = CommonSessionValue(
            sessions,
            session => session.Metadata?.Application,
            value => DisplayText.CleanGameName(value));
        var commonResolution = CommonSessionValue(sessions, session => session.Metadata?.Resolution);
        var commonGpu = CommonSessionValue(
            sessions,
            session => session.Metadata?.Gpu,
            DisplayText.CompactHardware);
        var commonCpu = CommonSessionValue(
            sessions,
            session => session.Metadata?.Cpu,
            DisplayText.CompactHardware);

        var mainTitle = isDefaultTitle && commonApplication is not null
            ? commonApplication
            : header.Title.Trim();

        var commonParts = new List<string>();
        foreach (var value in new[] { commonResolution, commonGpu, commonCpu })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                commonParts.Add(value!);
            }
        }

        var methodology = BuildMethodology(sessions);
        return new ProfessionalReportContext(
            mainTitle,
            reportType,
            string.Join("  ·  ", commonParts),
            runs,
            methodology,
            buildContext?.Groups.Count ?? 0);
    }

    private static IReadOnlyList<ReportRunContext> BuildRunContexts(
        ReportBuildContext? context,
        ChartStyle style)
    {
        if (context is null || context.Groups.Count == 0)
        {
            return [];
        }

        var group = context.Groups
            .OrderByDescending(candidate => candidate.Series.Count)
            .FirstOrDefault(candidate => candidate.Series.Count > 0);
        if (group is null)
        {
            return [];
        }

        var runs = new List<ReportRunContext>(group.Series.Count);
        for (var i = 0; i < group.Series.Count; i++)
        {
            var series = group.Series[i];
            var role = group.IsMultiWorkspace
                ? $"BENCHMARK {i + 1}"
                : series.Role == SessionRole.Base ? "BASE" : "COMPARISON";
            runs.Add(new ReportRunContext(
                role,
                series.LabelOrDefault,
                SeriesColor(group, series, style),
                series.SourceSession));
        }

        return runs;
    }

    private static string? CommonSessionValue(
        IReadOnlyList<SessionAnalysis> sessions,
        Func<SessionAnalysis, string?> selector,
        Func<string, string>? transform = null)
    {
        if (sessions.Count == 0)
        {
            return null;
        }

        var values = new List<string>(sessions.Count);
        foreach (var session in sessions)
        {
            var raw = selector(session);
            if (string.IsNullOrWhiteSpace(raw) || raw == "--")
            {
                return null;
            }

            var value = transform is null ? raw.Trim() : transform(raw);
            if (string.IsNullOrWhiteSpace(value) || value == "--")
            {
                return null;
            }

            values.Add(value);
        }

        var first = values[0];
        return values.All(value => string.Equals(value, first, StringComparison.OrdinalIgnoreCase))
            ? first
            : null;
    }

    private static string BuildMethodology(IReadOnlyList<SessionAnalysis> sessions)
    {
        if (sessions.Count == 0)
        {
            return "FrameView Analyzer report · charts use the analysis state active at export time";
        }

        var first = sessions[0].EffectiveOptions;
        var common = sessions.All(session => session.EffectiveOptions == first);
        if (!common)
        {
            return "Analysis settings vary by benchmark · each run uses its saved GPU filter, edge trim, and transition rules";
        }

        var gpu = first.AutoGpuThreshold
            ? $"Automatic GPU activity ≥ {first.GpuThreshold:F0}%"
            : $"Manual GPU activity ≥ {first.GpuThreshold:F0}%";
        var trim = first.TrimBufferSeconds > 0
            ? $"edge trim {first.TrimBufferSeconds:F1} s"
            : "no edge trim";
        var transitions = first.ExcludeTransitions
            ? "loading/transition exclusion on"
            : "loads/transitions included";
        return $"{gpu}  ·  {trim}  ·  {transitions}";
    }

    private static int MeasureProfessionalHeaderHeight(ProfessionalReportContext context, int width)
    {
        _ = width;
        var columns = ProfessionalRunColumns(context.Runs.Count);
        var rows = context.Runs.Count == 0
            ? 0
            : (context.Runs.Count + columns - 1) / columns;

        var height = ProfessionalHeaderPadding
            + ProfessionalTitleHeight
            + ProfessionalContextHeight;
        if (rows > 0)
        {
            height += ProfessionalHeaderGap
                + rows * ProfessionalRunCardHeight
                + System.Math.Max(0, rows - 1) * ProfessionalRunCardGap;
        }

        height += ProfessionalHeaderGap + ProfessionalMethodHeight + HeaderSeparationGap;
        return height;
    }

    private static int ProfessionalRunColumns(int runCount)
    {
        if (runCount <= 1)
        {
            return 1;
        }

        if (runCount == 2)
        {
            return 2;
        }

        return System.Math.Min(ProfessionalMaxRunColumns, runCount);
    }

    private static void DrawProfessionalHeader(
        SKCanvas canvas,
        ProfessionalReportContext context,
        ChartStyle style,
        int width,
        int headerHeight)
    {
        using var backgroundPaint = new SKPaint { Color = ToSkColor(style.Background) };
        canvas.DrawRect(0, 0, width, headerHeight, backgroundPaint);

        using var titleFont = CreateProfessionalTitleFont();
        using var typeFont = CreateProfessionalTypeFont();
        using var contextFont = CreateProfessionalContextFont();
        using var titlePaint = new SKPaint { Color = ToSkColor(style.Foreground) };
        using var mutedPaint = new SKPaint { Color = ToSkColor(style.Muted) };
        using var accentPaint = new SKPaint { Color = ToSkColor(style.SeriesA) };

        var x = ProfessionalHeaderPadding;
        var y = ProfessionalHeaderPadding;
        var titleBaseline = y - titleFont.Metrics.Ascent;
        canvas.DrawText(
            Truncate(context.MainTitle, 62),
            x,
            titleBaseline,
            SKTextAlign.Left,
            titleFont,
            titlePaint);

        // Report type acts as a quiet eyebrow on the right, giving the title
        // area hierarchy without repeating the old wall of metadata text.
        var typeBaseline = y - typeFont.Metrics.Ascent + 5;
        canvas.DrawText(
            context.ReportType,
            width - ProfessionalHeaderPadding,
            typeBaseline,
            SKTextAlign.Right,
            typeFont,
            accentPaint);

        y += ProfessionalTitleHeight;
        var contextText = context.CommonContext.Length > 0
            ? context.CommonContext
            : $"{context.Runs.Count} benchmark run(s)  ·  {context.MetricCount} selected metric(s)";
        var contextBaseline = y - contextFont.Metrics.Ascent;
        canvas.DrawText(
            Truncate(contextText, 120),
            x,
            contextBaseline,
            SKTextAlign.Left,
            contextFont,
            mutedPaint);
        y += ProfessionalContextHeight;

        if (context.Runs.Count > 0)
        {
            y += ProfessionalHeaderGap;
            DrawRunCards(canvas, context, style, width, y);
            var columns = ProfessionalRunColumns(context.Runs.Count);
            var rows = (context.Runs.Count + columns - 1) / columns;
            y += rows * ProfessionalRunCardHeight
                + System.Math.Max(0, rows - 1) * ProfessionalRunCardGap;
        }

        y += ProfessionalHeaderGap;
        DrawMethodology(canvas, context, style, width, y);

        using var dividerPaint = new SKPaint
        {
            Color = ToSkColor(style.Grid.WithAlpha(0.9)),
            StrokeWidth = 1,
            IsAntialias = true,
        };
        canvas.DrawLine(
            ProfessionalHeaderPadding,
            headerHeight - HeaderSeparationGap / 2f,
            width - ProfessionalHeaderPadding,
            headerHeight - HeaderSeparationGap / 2f,
            dividerPaint);
    }

    private static void DrawRunCards(
        SKCanvas canvas,
        ProfessionalReportContext context,
        ChartStyle style,
        int width,
        int top)
    {
        var columns = ProfessionalRunColumns(context.Runs.Count);
        var rows = (context.Runs.Count + columns - 1) / columns;
        _ = rows;
        var availableWidth = width
            - ProfessionalHeaderPadding * 2
            - (columns - 1) * ProfessionalRunCardGap;
        var cardWidth = availableWidth / (float)columns;

        var sessions = context.Runs
            .Select(run => run.Session)
            .Where(session => session is not null)
            .Cast<SessionAnalysis>()
            .ToList();
        var commonApplication = CommonSessionValue(
            sessions,
            session => session.Metadata?.Application,
            DisplayText.CleanGameName);
        var commonResolution = CommonSessionValue(sessions, session => session.Metadata?.Resolution);
        var commonGpu = CommonSessionValue(
            sessions,
            session => session.Metadata?.Gpu,
            DisplayText.CompactHardware);
        var commonCpu = CommonSessionValue(
            sessions,
            session => session.Metadata?.Cpu,
            DisplayText.CompactHardware);

        using var roleFont = CreateRunRoleFont();
        using var nameFont = CreateRunNameFont();
        using var detailFont = CreateRunDetailFont();
        using var rolePaint = new SKPaint { Color = ToSkColor(style.Muted) };
        using var namePaint = new SKPaint { Color = ToSkColor(style.Foreground) };
        using var detailPaint = new SKPaint { Color = ToSkColor(style.Muted) };
        using var fillPaint = new SKPaint { Color = ToSkColor(style.Grid.WithAlpha(0.08)) };
        using var borderPaint = new SKPaint
        {
            Color = ToSkColor(style.Grid.WithAlpha(0.8)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true,
        };

        for (var i = 0; i < context.Runs.Count; i++)
        {
            var row = i / columns;
            var column = i % columns;
            var left = ProfessionalHeaderPadding + column * (cardWidth + ProfessionalRunCardGap);
            var cardTop = top + row * (ProfessionalRunCardHeight + ProfessionalRunCardGap);
            var rect = new SKRect(
                left,
                cardTop,
                left + cardWidth,
                cardTop + ProfessionalRunCardHeight);
            canvas.DrawRoundRect(rect, 7, 7, fillPaint);
            canvas.DrawRoundRect(rect, 7, 7, borderPaint);

            using var accent = new SKPaint
            {
                Color = ToSkColor(context.Runs[i].Color),
                StrokeWidth = 4,
                IsAntialias = true,
            };
            canvas.DrawLine(left + 1, cardTop + 2, left + cardWidth - 1, cardTop + 2, accent);

            var roleBaseline = cardTop + 13 - roleFont.Metrics.Ascent;
            canvas.DrawText(
                context.Runs[i].Role,
                left + 12,
                roleBaseline,
                SKTextAlign.Left,
                roleFont,
                rolePaint);

            var nameBaseline = cardTop + 34 - nameFont.Metrics.Ascent;
            canvas.DrawText(
                Truncate(context.Runs[i].Label, System.Math.Max(18, (int)(cardWidth / 9))),
                left + 12,
                nameBaseline,
                SKTextAlign.Left,
                nameFont,
                namePaint);

            var session = context.Runs[i].Session;
            var contextLine = RunContextLine(
                session,
                commonApplication,
                commonResolution,
                commonGpu,
                commonCpu);
            var dataLine = RunDataLine(session);

            if (contextLine.Length > 0)
            {
                var contextBaseline = cardTop + 59 - detailFont.Metrics.Ascent;
                canvas.DrawText(
                    Truncate(contextLine, System.Math.Max(22, (int)(cardWidth / 7))),
                    left + 12,
                    contextBaseline,
                    SKTextAlign.Left,
                    detailFont,
                    detailPaint);
            }

            var dataBaseline = cardTop + 80 - detailFont.Metrics.Ascent;
            canvas.DrawText(
                Truncate(dataLine, System.Math.Max(22, (int)(cardWidth / 7))),
                left + 12,
                dataBaseline,
                SKTextAlign.Left,
                detailFont,
                detailPaint);
        }
    }

    private static string RunContextLine(
        SessionAnalysis? session,
        string? commonApplication,
        string? commonResolution,
        string? commonGpu,
        string? commonCpu)
    {
        if (session?.Metadata is not { } metadata)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (commonApplication is null && !string.IsNullOrWhiteSpace(metadata.Application) && metadata.Application != "--")
        {
            parts.Add(DisplayText.CleanGameName(metadata.Application));
        }

        if (commonResolution is null && !string.IsNullOrWhiteSpace(metadata.Resolution) && metadata.Resolution != "--")
        {
            parts.Add(metadata.Resolution);
        }

        if (commonGpu is null && !string.IsNullOrWhiteSpace(metadata.Gpu) && metadata.Gpu != "--")
        {
            parts.Add(DisplayText.CompactHardware(metadata.Gpu));
        }

        if (commonCpu is null && !string.IsNullOrWhiteSpace(metadata.Cpu) && metadata.Cpu != "--")
        {
            parts.Add(DisplayText.CompactHardware(metadata.Cpu));
        }

        return string.Join("  ·  ", parts);
    }

    private static string RunDataLine(SessionAnalysis? session)
    {
        if (session is null)
        {
            return "Benchmark data";
        }

        var diagnostics = session.Diagnostics;
        var total = diagnostics.TotalBins;
        var valid = diagnostics.VisibleBins;
        var percent = total > 0 ? valid * 100.0 / total : 0.0;
        var parts = new List<string>();
        if (total > 0)
        {
            parts.Add($"{valid:N0}/{total:N0} s valid ({percent:F1}%)");
        }

        if (session.Metadata is { } metadata)
        {
            if (metadata.FrameCount > 0)
            {
                parts.Add($"{metadata.FrameCount:N0} frames");
            }

            if (metadata.MetricCount > 0)
            {
                parts.Add($"{metadata.MetricCount:N0} telemetry metrics");
            }
        }

        return parts.Count > 0 ? string.Join("  ·  ", parts) : "Benchmark data";
    }

    private static void DrawMethodology(
        SKCanvas canvas,
        ProfessionalReportContext context,
        ChartStyle style,
        int width,
        int top)
    {
        using var labelFont = CreateMethodLabelFont();
        using var textFont = CreateMethodTextFont();
        using var labelPaint = new SKPaint { Color = ToSkColor(style.SeriesA) };
        using var textPaint = new SKPaint { Color = ToSkColor(style.Muted) };
        using var linePaint = new SKPaint
        {
            Color = ToSkColor(style.Grid.WithAlpha(0.75)),
            StrokeWidth = 1,
            IsAntialias = true,
        };

        var baseline = top + 6 - labelFont.Metrics.Ascent;
        canvas.DrawText(
            "ANALYSIS METHOD",
            ProfessionalHeaderPadding,
            baseline,
            SKTextAlign.Left,
            labelFont,
            labelPaint);

        var textBaseline = top + 6 - textFont.Metrics.Ascent;
        canvas.DrawText(
            Truncate(context.Methodology, 150),
            ProfessionalHeaderPadding + 145,
            textBaseline,
            SKTextAlign.Left,
            textFont,
            textPaint);

        canvas.DrawLine(
            ProfessionalHeaderPadding,
            top + ProfessionalMethodHeight - 5,
            width - ProfessionalHeaderPadding,
            top + ProfessionalMethodHeight - 5,
            linePaint);
    }

    private static void DrawProfessionalFooter(
        SKCanvas canvas,
        ProfessionalReportContext context,
        ChartStyle style,
        int width,
        int top,
        int bottom)
    {
        using var font = CreateFooterFont();
        using var paint = new SKPaint { Color = ToSkColor(style.Muted) };
        using var divider = new SKPaint
        {
            Color = ToSkColor(style.Grid.WithAlpha(0.75)),
            StrokeWidth = 1,
            IsAntialias = true,
        };

        canvas.DrawLine(
            ProfessionalHeaderPadding,
            top + 8,
            width - ProfessionalHeaderPadding,
            top + 8,
            divider);
        var baseline = top + 20 - font.Metrics.Ascent;
        canvas.DrawText(
            $"FrameView Analyzer  ·  {context.Runs.Count} benchmark(s)  ·  {context.MetricCount} chart(s)",
            ProfessionalHeaderPadding,
            baseline,
            SKTextAlign.Left,
            font,
            paint);
        canvas.DrawText(
            $"Generated {DateTime.Now:yyyy-MM-dd HH:mm}",
            width - ProfessionalHeaderPadding,
            baseline,
            SKTextAlign.Right,
            font,
            paint);
        _ = bottom;
    }

    private static bool IsBenchmarkReportTitle(string title) =>
        title.Contains("BENCHMARK COMPARISON", StringComparison.OrdinalIgnoreCase);

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

    private static SKFont CreateLegacyTitleFont() => new()
    {
        Size = 22,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateLegacyLineFont() => new()
    {
        Size = 14,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateProfessionalTitleFont() => new()
    {
        Size = 30,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateProfessionalTypeFont() => new()
    {
        Size = 12,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateProfessionalContextFont() => new()
    {
        Size = 15,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateRunRoleFont() => new()
    {
        Size = 10,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateRunNameFont() => new()
    {
        Size = 16,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateRunDetailFont() => new()
    {
        Size = 11,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateMethodLabelFont() => new()
    {
        Size = 10,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateMethodTextFont() => new()
    {
        Size = 12,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateFooterFont() => new()
    {
        Size = 10,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal),
        Edging = SKFontEdging.Antialias,
    };

    private static int LineHeight(SKFont font)
    {
        var metrics = font.Metrics;
        return (int)System.Math.Ceiling(metrics.Descent - metrics.Ascent) + 2;
    }

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
