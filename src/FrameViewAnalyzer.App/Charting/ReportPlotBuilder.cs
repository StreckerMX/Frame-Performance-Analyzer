using System.IO;
using System.Runtime.CompilerServices;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Charting;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;
using ScottPlot;
using SkiaSharp;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>
/// Builds the chart plots used by PNG exports and composes normal benchmark
/// exports as a structured report: shared test context, benchmark runs,
/// methodology, performance timeline, secondary telemetry, and footer.
/// </summary>
public static class ReportPlotBuilder
{
    public sealed record ReportGroup(
        MetricDefinition Metric,
        IReadOnlyList<MetricSeries> Series,
        bool IsMultiWorkspace = false);

    /// <summary>Report title plus export semantics and optional per-run manual metadata.</summary>
    public sealed record ReportHeader(
        string Title,
        IReadOnlyList<string> Lines,
        bool UseProfessionalLayout = false,
        bool IsMultiReport = false,
        IReadOnlyDictionary<string, ManualMetadata?>? ManualMetadataByPath = null,
        bool UseFramePoints = false);

    private sealed record ReportBuildContext(IReadOnlyList<ReportGroup> Groups);

    private sealed record ReportRunContext(
        string Role,
        string Label,
        ScottPlot.Color Color,
        SessionAnalysis? Session,
        ManualMetadata? ManualMetadata);

    private sealed record ReportField(string Label, string Value);

    private sealed record MethodologyContext(
        IReadOnlyList<ReportField> Fields,
        bool GpuFilterVaries,
        bool EdgeTrimVaries,
        bool TransitionPolicyVaries);

    private sealed record ProfessionalReportContext(
        string MainTitle,
        string ReportType,
        string CommonContext,
        IReadOnlyList<ReportField> SharedConfiguration,
        IReadOnlyList<ReportRunContext> Runs,
        MethodologyContext Methodology,
        int MetricCount);

    private static readonly ConditionalWeakTable<Multiplot, ReportBuildContext> BuildContexts = new();

    /// <summary>Number of columns used for secondary metric charts.</summary>
    public const int GridColumns = 2;

    /// <summary>Height reserved for the principal full-width FPS chart.</summary>
    public const int PrimaryRowHeight = 560;

    /// <summary>Target height of each secondary chart row.</summary>
    public const int GridRowHeight = 400;

    /// <summary>Whitespace between adjacent chart cells.</summary>
    public const int GridGap = 20;

    /// <summary>Explicit separation between a legacy header and its first chart.</summary>
    public const int HeaderSeparationGap = 12;

    private const int LegacyHeaderPadding = 16;
    private const int LegacyHeaderTitleGap = 12;
    private const int LegacyHeaderBottomMargin = 4;

    private const int ReportPadding = 26;
    private const int ReportTitleHeight = 44;
    private const int ReportSectionLabelHeight = 22;
    private const int ReportSectionGap = 18;
    private const int ReportConfigHeight = 76;
    private const int ReportRunCardHeight = 198;
    private const int ReportRunCardGap = 14;
    private const int ReportMethodHeight = 92;
    private const int ReportHeaderBottomGap = 18;
    private const int ReportBodySectionHeight = 38;
    private const int ReportBodySectionGap = 16;
    private const int ReportFooterHeight = 54;

    /// <summary>
    /// Builds exactly one subplot per metric. FPS is promoted to plot zero
    /// whenever it is selected so the professional report can make it the
    /// principal performance timeline.
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

            // Direct Build() consumers still get a correctly themed legend.
            // Professional PNG composition hides these copies and uses the
            // benchmark-run cards as the report key instead.
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
    /// Saves a PNG. Standard Pair/Multi benchmark titles use the professional
    /// report compositor. Arbitrary titles retain the compact legacy renderer
    /// used by low-level regression tests and free-form callers.
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
        var professional = header is not null && ShouldUseProfessionalLayout(header);
        if (professional)
        {
            SaveProfessionalPng(multiplot, style, header!, buildContext, path, width);
            return;
        }

        SaveLegacyPng(multiplot, style, header, path, width, height, buildContext);
    }

    private static void SaveProfessionalPng(
        Multiplot multiplot,
        ChartStyle style,
        ReportHeader header,
        ReportBuildContext? buildContext,
        string path,
        int width)
    {
        var context = BuildProfessionalContext(header, buildContext, style);
        var headerHeight = MeasureProfessionalHeaderHeight(context);
        var bodyLayout = BuildProfessionalBodyLayout(
            width,
            headerHeight,
            multiplot.Subplots.Count,
            HasPrimaryFps(buildContext));
        var renderHeight = bodyLayout.Bottom + ReportFooterHeight;

        if (context.Runs.Count > 0)
        {
            for (var i = 0; i < multiplot.Subplots.Count; i++)
            {
                multiplot.Subplots.GetPlot(i).HideLegend();
            }
        }

        using var bitmap = new SKBitmap(width, renderHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ToSkColor(style.Background));

        DrawProfessionalHeader(canvas, context, style, width, headerHeight);
        DrawProfessionalBodyLabels(canvas, style, width, bodyLayout);
        RenderPanels(canvas, multiplot, bodyLayout.PanelRects);
        DrawProfessionalFooter(canvas, context, style, width, bodyLayout.Bottom, renderHeight);

        SaveBitmap(bitmap, path);
    }

    private static void SaveLegacyPng(
        Multiplot multiplot,
        ChartStyle style,
        ReportHeader? header,
        string path,
        int width,
        int requestedHeight,
        ReportBuildContext? buildContext)
    {
        var headerHeight = header is null ? 0 : MeasureHeaderHeight(header);
        var panelCount = multiplot.Subplots.Count;
        var renderHeight = panelCount == 0
            ? System.Math.Max(requestedHeight, headerHeight)
            : RecommendedHeight(panelCount, headerHeight, HasPrimaryFps(buildContext));

        using var bitmap = new SKBitmap(width, renderHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ToSkColor(style.Background));

        if (panelCount > 0)
        {
            RenderPanels(
                canvas,
                multiplot,
                width,
                renderHeight,
                headerHeight,
                HasPrimaryFps(buildContext));
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
    /// Legacy/adaptive chart height used outside the professional compositor.
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
    /// Legacy/adaptive panel geometry. FPS may occupy the first full-width row
    /// and secondary metrics use two columns.
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

    /// <summary>Renders legacy/adaptive panels into isolated bitmap cells.</summary>
    public static void RenderPanels(
        SKCanvas canvas,
        Multiplot multiplot,
        int width,
        int height,
        int headerHeight,
        bool hasPrimaryFps = false)
    {
        var rects = ReportPanelRects(
            width,
            height,
            headerHeight,
            multiplot.Subplots.Count,
            hasPrimaryFps);
        RenderPanels(canvas, multiplot, rects);
    }

    private static void RenderPanels(
        SKCanvas canvas,
        Multiplot multiplot,
        IReadOnlyList<PixelRect> rects)
    {
        var count = System.Math.Min(multiplot.Subplots.Count, rects.Count);
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

    /// <summary>Full legacy content rectangle below the caller-provided header.</summary>
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
        canvas.DrawText(
            header.Title,
            LegacyHeaderPadding,
            titleBaseline,
            SKTextAlign.Left,
            titleFont,
            titlePaint);

        var linesTop = titleTop + titleHeight + LegacyHeaderTitleGap;
        for (var i = 0; i < header.Lines.Count; i++)
        {
            var baseline = linesTop + i * lineHeight - lineFont.Metrics.Ascent;
            canvas.DrawText(
                header.Lines[i],
                LegacyHeaderPadding,
                baseline,
                SKTextAlign.Left,
                lineFont,
                linePaint);
        }
    }

    private static ProfessionalReportContext BuildProfessionalContext(
        ReportHeader header,
        ReportBuildContext? buildContext,
        ChartStyle style)
    {
        var runs = BuildRunContexts(buildContext, style, header.ManualMetadataByPath);
        var sessions = runs
            .Select(run => run.Session)
            .Where(session => session is not null)
            .Cast<SessionAnalysis>()
            .ToList();

        var isMulti = header.IsMultiReport
            || buildContext?.Groups.FirstOrDefault()?.IsMultiWorkspace == true
            || header.Title.Contains("MULTI BENCHMARK COMPARISON", StringComparison.OrdinalIgnoreCase);
        var reportType = isMulti ? "MULTI BENCHMARK COMPARISON" : "BENCHMARK COMPARISON";
        var isDefaultTitle = string.Equals(header.Title.Trim(), reportType, StringComparison.OrdinalIgnoreCase);

        var commonApplication = CommonRunValue(runs, RunApplication, DisplayText.CleanGameName);
        var commonResolution = CommonRunValue(runs, RunResolution);
        var commonGpu = CommonRunValue(runs, RunGpu, DisplayText.CompactHardware);
        var commonCpu = CommonRunValue(runs, RunCpu, DisplayText.CompactHardware);
        var commonDriver = CommonRunValue(runs, run => run.ManualMetadata?.DriverVersion);

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

        var config = new List<ReportField>();
        AddConfig(config, "APPLICATION", commonApplication);
        AddConfig(config, "RESOLUTION", commonResolution);
        AddConfig(config, "GPU", commonGpu);
        AddConfig(config, "CPU", commonCpu);
        AddConfig(config, "DRIVER", commonDriver);
        if (config.Count == 0)
        {
            config.Add(new ReportField("SHARED TEST CONTEXT", "Varies by benchmark"));
        }

        return new ProfessionalReportContext(
            mainTitle,
            reportType,
            string.Join("  ·  ", commonParts),
            config,
            runs,
            BuildMethodology(sessions, header.UseFramePoints),
            buildContext?.Groups.Count ?? 0);
    }

    private static void AddConfig(List<ReportField> fields, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new ReportField(label, value!));
        }
    }

    private static IReadOnlyList<ReportRunContext> BuildRunContexts(
        ReportBuildContext? context,
        ChartStyle style,
        IReadOnlyDictionary<string, ManualMetadata?>? manualMetadataByPath)
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
                ReportRunDisplayLabel(series.LabelOrDefault),
                SeriesColor(group, series, style),
                series.SourceSession,
                ResolveManualMetadata(series, manualMetadataByPath)));
        }

        return runs;
    }

    private static ManualMetadata? ResolveManualMetadata(
        MetricSeries series,
        IReadOnlyDictionary<string, ManualMetadata?>? manualMetadataByPath)
    {
        var path = series.SourceSession?.Capture.Path;
        if (manualMetadataByPath is null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return manualMetadataByPath.TryGetValue(path, out var manual) ? manual : null;
    }

    private static string? RunApplication(ReportRunContext run) =>
        IsUseful(run.ManualMetadata?.Game)
            ? run.ManualMetadata!.Game
            : run.Session?.Metadata?.Application;

    private static string? RunResolution(ReportRunContext run) =>
        IsUseful(run.ManualMetadata?.Resolution)
            ? run.ManualMetadata!.Resolution
            : run.Session?.Metadata?.Resolution;

    private static string? RunGpu(ReportRunContext run) => run.Session?.Metadata?.Gpu;

    private static string? RunCpu(ReportRunContext run) => run.Session?.Metadata?.Cpu;

    private static string? CommonRunValue(
        IReadOnlyList<ReportRunContext> runs,
        Func<ReportRunContext, string?> selector,
        Func<string, string>? transform = null)
    {
        if (runs.Count == 0)
        {
            return null;
        }

        var values = new List<string>(runs.Count);
        foreach (var run in runs)
        {
            var raw = selector(run);
            if (!IsUseful(raw))
            {
                return null;
            }

            var value = transform is null ? raw!.Trim() : transform(raw!);
            if (!IsUseful(value))
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

    /// <summary>
    /// Removes Pair picker role prefixes because the report card already has a
    /// dedicated BASE/COMPARISON eyebrow.
    /// </summary>
    internal static string ReportRunDisplayLabel(string label)
    {
        foreach (var prefix in new[] { "Base — ", "Comparison — ", "Base - ", "Comparison - " })
        {
            if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return label[prefix.Length..].Trim();
            }
        }

        return label.Trim();
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

    private static MethodologyContext BuildMethodology(
        IReadOnlyList<SessionAnalysis> sessions,
        bool useFramePoints)
    {
        if (sessions.Count == 0)
        {
            return new MethodologyContext(
                [new ReportField("ANALYSIS", "Current Frame Performance Analyzer session state")],
                false,
                false,
                false);
        }

        var gpuValues = sessions.Select(session => GpuFilterText(session.EffectiveOptions)).ToList();
        var trimValues = sessions.Select(session => EdgeTrimText(session.EffectiveOptions)).ToList();
        var transitionValues = sessions.Select(session => TransitionPolicyText(session.EffectiveOptions)).ToList();

        var gpuVaries = !AllEqual(gpuValues);
        var trimVaries = !AllEqual(trimValues);
        var transitionsVary = !AllEqual(transitionValues);

        return new MethodologyContext(
        [
            new ReportField("GPU ACTIVITY FILTER", gpuVaries ? "Per benchmark" : gpuValues[0]),
            new ReportField("EDGE TRIM", trimVaries ? "Per benchmark" : trimValues[0]),
            new ReportField("LOADS / TRANSITIONS", transitionsVary ? "Per benchmark" : transitionValues[0]),
            new ReportField(
                "CHART RESOLUTION",
                useFramePoints ? "Per-frame analyzed data (where available)" : "1 analyzed value per second"),
        ],
        gpuVaries,
        trimVaries,
        transitionsVary);
    }

    private static bool AllEqual(IReadOnlyList<string> values) =>
        values.Count <= 1
        || values.Skip(1).All(value => string.Equals(value, values[0], StringComparison.Ordinal));

    private static string GpuFilterText(AnalysisOptions options) =>
        options.AutoGpuThreshold
            ? $"Automatic · ≥ {options.GpuThreshold:F0}% average GPU utilization"
            : $"Manual · ≥ {options.GpuThreshold:F0}% average GPU utilization";

    private static string EdgeTrimText(AnalysisOptions options) =>
        options.TrimBufferSeconds > 0
            ? $"{options.TrimBufferSeconds:F1} s from each detected edge"
            : "None";

    private static string TransitionPolicyText(AnalysisOptions options) =>
        options.ExcludeTransitions ? "Excluded" : "Included";

    private static int MeasureProfessionalHeaderHeight(ProfessionalReportContext context)
    {
        var columns = ReportRunColumns(context.Runs.Count);
        var runRows = context.Runs.Count == 0
            ? 0
            : (context.Runs.Count + columns - 1) / columns;

        var height = ReportPadding
            + ReportTitleHeight
            + ReportSectionGap
            + ReportSectionLabelHeight
            + ReportConfigHeight;

        if (runRows > 0)
        {
            height += ReportSectionGap
                + ReportSectionLabelHeight
                + runRows * ReportRunCardHeight
                + System.Math.Max(0, runRows - 1) * ReportRunCardGap;
        }

        height += ReportSectionGap
            + ReportSectionLabelHeight
            + ReportMethodHeight
            + ReportHeaderBottomGap;
        return height;
    }

    /// <summary>
    /// Adaptive benchmark-card policy: 2→2, 3→3, 4→2×2, 5→3+2,
    /// 6→3×2, and 7–8→4×2.
    /// </summary>
    internal static int ReportRunColumns(int runCount) => runCount switch
    {
        <= 1 => 1,
        2 => 2,
        3 => 3,
        4 => 2,
        5 or 6 => 3,
        _ => 4,
    };

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
        using var titlePaint = new SKPaint { Color = ToSkColor(style.Foreground) };
        using var accentPaint = new SKPaint { Color = ToSkColor(style.SeriesA) };

        var y = ReportPadding;
        canvas.DrawText(
            Truncate(context.MainTitle, 64),
            ReportPadding,
            y - titleFont.Metrics.Ascent,
            SKTextAlign.Left,
            titleFont,
            titlePaint);
        canvas.DrawText(
            context.ReportType,
            width - ReportPadding,
            y - typeFont.Metrics.Ascent + 5,
            SKTextAlign.Right,
            typeFont,
            accentPaint);

        y += ReportTitleHeight;

        y += ReportSectionGap;
        DrawSectionLabel(canvas, "TEST CONFIGURATION", style, y);
        y += ReportSectionLabelHeight;
        DrawConfigurationPanel(canvas, context.SharedConfiguration, style, width, y);
        y += ReportConfigHeight;

        if (context.Runs.Count > 0)
        {
            y += ReportSectionGap;
            DrawSectionLabel(canvas, "BENCHMARK RUNS", style, y);
            y += ReportSectionLabelHeight;
            DrawRunCards(canvas, context, style, width, y);
            var columns = ReportRunColumns(context.Runs.Count);
            var rows = (context.Runs.Count + columns - 1) / columns;
            y += rows * ReportRunCardHeight
                + System.Math.Max(0, rows - 1) * ReportRunCardGap;
        }

        y += ReportSectionGap;
        DrawSectionLabel(canvas, "ANALYSIS METHODOLOGY", style, y);
        y += ReportSectionLabelHeight;
        DrawMethodologyPanel(canvas, context.Methodology, style, width, y);

        using var dividerPaint = new SKPaint
        {
            Color = ToSkColor(style.Grid.WithAlpha(0.85)),
            StrokeWidth = 1,
            IsAntialias = true,
        };
        canvas.DrawLine(
            ReportPadding,
            headerHeight - 4,
            width - ReportPadding,
            headerHeight - 4,
            dividerPaint);
    }

    private static void DrawSectionLabel(
        SKCanvas canvas,
        string text,
        ChartStyle style,
        int top)
    {
        using var font = CreateSectionLabelFont();
        using var paint = new SKPaint { Color = ToSkColor(style.SeriesA) };
        canvas.DrawText(
            text,
            ReportPadding,
            top + 2 - font.Metrics.Ascent,
            SKTextAlign.Left,
            font,
            paint);
    }

    private static void DrawConfigurationPanel(
        SKCanvas canvas,
        IReadOnlyList<ReportField> fields,
        ChartStyle style,
        int width,
        int top)
    {
        DrawPanelBackground(canvas, style, ReportPadding, top, width - ReportPadding, top + ReportConfigHeight);

        using var labelFont = CreateFieldLabelFont();
        using var valueFont = CreateFieldValueFont();
        using var labelPaint = new SKPaint { Color = ToSkColor(style.Muted) };
        using var valuePaint = new SKPaint { Color = ToSkColor(style.Foreground) };

        var count = System.Math.Max(1, fields.Count);
        var innerWidth = width - ReportPadding * 2 - 28;
        var cellWidth = innerWidth / (float)count;
        for (var i = 0; i < fields.Count; i++)
        {
            var left = ReportPadding + 14 + i * cellWidth;
            canvas.DrawText(
                fields[i].Label,
                left,
                top + 13 - labelFont.Metrics.Ascent,
                SKTextAlign.Left,
                labelFont,
                labelPaint);
            canvas.DrawText(
                Truncate(fields[i].Value, System.Math.Max(12, (int)(cellWidth / 8))),
                left,
                top + 39 - valueFont.Metrics.Ascent,
                SKTextAlign.Left,
                valueFont,
                valuePaint);
        }
    }

    private static void DrawRunCards(
        SKCanvas canvas,
        ProfessionalReportContext context,
        ChartStyle style,
        int width,
        int top)
    {
        var columns = ReportRunColumns(context.Runs.Count);
        var availableWidth = width
            - ReportPadding * 2
            - (columns - 1) * ReportRunCardGap;
        var cardWidth = availableWidth / (float)columns;

        var commonApplication = CommonRunValue(context.Runs, RunApplication, DisplayText.CleanGameName);
        var commonResolution = CommonRunValue(context.Runs, RunResolution);
        var commonGpu = CommonRunValue(context.Runs, RunGpu, DisplayText.CompactHardware);
        var commonCpu = CommonRunValue(context.Runs, RunCpu, DisplayText.CompactHardware);

        using var roleFont = CreateRunRoleFont();
        using var nameFont = CreateRunNameFont();
        using var detailFont = CreateRunDetailFont();
        using var rolePaint = new SKPaint { Color = ToSkColor(style.Muted) };
        using var namePaint = new SKPaint { Color = ToSkColor(style.Foreground) };
        using var detailPaint = new SKPaint { Color = ToSkColor(style.Muted) };

        for (var i = 0; i < context.Runs.Count; i++)
        {
            var row = i / columns;
            var column = i % columns;
            var left = ReportPadding + column * (cardWidth + ReportRunCardGap);
            var cardTop = top + row * (ReportRunCardHeight + ReportRunCardGap);
            DrawPanelBackground(canvas, style, left, cardTop, left + cardWidth, cardTop + ReportRunCardHeight);

            using var accent = new SKPaint
            {
                Color = ToSkColor(context.Runs[i].Color),
                StrokeWidth = 4,
                IsAntialias = true,
            };
            canvas.DrawLine(left + 1, cardTop + 2, left + cardWidth - 1, cardTop + 2, accent);

            canvas.DrawText(
                context.Runs[i].Role,
                left + 13,
                cardTop + 14 - roleFont.Metrics.Ascent,
                SKTextAlign.Left,
                roleFont,
                rolePaint);
            canvas.DrawText(
                Truncate(context.Runs[i].Label, System.Math.Max(22, (int)(cardWidth / 8.2))),
                left + 13,
                cardTop + 38 - nameFont.Metrics.Ascent,
                SKTextAlign.Left,
                nameFont,
                namePaint);

            var run = context.Runs[i];
            var session = run.Session;
            var contextLine = RunContextLine(
                run,
                commonApplication,
                commonResolution,
                commonGpu,
                commonCpu);
            var manualLines = ReportManualMetadataLines(run.ManualMetadata);
            var dataLines = RunDataLines(session);
            var analysisLine = RunAnalysisLine(session, context.Methodology);

            var cursor = cardTop + 66;
            if (contextLine.Length > 0)
            {
                DrawDetailLine(canvas, contextLine, left, cardWidth, cursor, detailFont, detailPaint);
                cursor += 19;
            }

            foreach (var line in manualLines)
            {
                if (cursor > cardTop + ReportRunCardHeight - 12)
                {
                    break;
                }

                DrawDetailLine(canvas, line, left, cardWidth, cursor, detailFont, detailPaint);
                cursor += 19;
            }

            foreach (var line in dataLines)
            {
                if (cursor > cardTop + ReportRunCardHeight - 12)
                {
                    break;
                }

                DrawDetailLine(canvas, line, left, cardWidth, cursor, detailFont, detailPaint);
                cursor += 19;
            }

            if (analysisLine.Length > 0 && cursor <= cardTop + ReportRunCardHeight - 12)
            {
                DrawDetailLine(canvas, analysisLine, left, cardWidth, cursor, detailFont, detailPaint);
            }
        }
    }

    private static void DrawDetailLine(
        SKCanvas canvas,
        string text,
        float left,
        float cardWidth,
        float top,
        SKFont font,
        SKPaint paint)
    {
        canvas.DrawText(
            Truncate(text, System.Math.Max(24, (int)(cardWidth / 7.1))),
            left + 13,
            top - font.Metrics.Ascent,
            SKTextAlign.Left,
            font,
            paint);
    }

    private static string RunContextLine(
        ReportRunContext run,
        string? commonApplication,
        string? commonResolution,
        string? commonGpu,
        string? commonCpu)
    {
        var parts = new List<string>();
        var application = RunApplication(run);
        var resolution = RunResolution(run);
        var gpu = RunGpu(run);
        var cpu = RunCpu(run);

        if (commonApplication is null && IsUseful(application))
        {
            parts.Add(DisplayText.CleanGameName(application!));
        }

        if (commonResolution is null && IsUseful(resolution))
        {
            parts.Add(resolution!);
        }

        if (commonGpu is null && IsUseful(gpu))
        {
            parts.Add(DisplayText.CompactHardware(gpu!));
        }

        if (commonCpu is null && IsUseful(cpu))
        {
            parts.Add(DisplayText.CompactHardware(cpu!));
        }

        return string.Join("  ·  ", parts);
    }

    /// <summary>Human-authored metadata lines shown inside each benchmark card.</summary>
    internal static IReadOnlyList<string> ReportManualMetadataLines(ManualMetadata? manual)
    {
        if (manual is null)
        {
            return [];
        }

        var lines = new List<string>();
        var config = new List<string>();
        if (IsUseful(manual.GraphicsPreset))
        {
            config.Add(manual.GraphicsPreset.Trim());
        }

        if (IsUseful(manual.Upscaler))
        {
            var upscaler = manual.Upscaler.Trim();
            if (IsUseful(manual.UpscalerQuality))
            {
                upscaler += " " + manual.UpscalerQuality.Trim();
            }

            config.Add(upscaler);
        }

        if (IsUseful(manual.FrameGeneration))
        {
            config.Add(manual.FrameGeneration.Trim());
        }

        if (IsUseful(manual.RayTracing))
        {
            config.Add(manual.RayTracing.Trim());
        }

        if (config.Count > 0)
        {
            lines.Add(string.Join("  ·  ", config));
        }

        var technical = new List<string>();
        if (IsUseful(manual.DriverVersion))
        {
            technical.Add("Driver " + manual.DriverVersion.Trim());
        }

        var tags = manual.Tags.Where(IsUseful).Select(tag => tag.Trim()).ToList();
        if (tags.Count > 0)
        {
            technical.Add("Tags: " + string.Join(", ", tags));
        }

        if (technical.Count > 0)
        {
            lines.Add(string.Join("  ·  ", technical));
        }

        if (IsUseful(manual.Notes))
        {
            lines.Add("Notes: " + manual.Notes.Trim());
        }

        return lines;
    }

    private static bool IsUseful(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value != "--";

    private static IReadOnlyList<string> RunDataLines(SessionAnalysis? session)
    {
        if (session is null)
        {
            return ["Benchmark data"];
        }

        var diagnostics = session.Diagnostics;
        var total = diagnostics.TotalBins;
        var valid = diagnostics.VisibleBins;
        var percent = total > 0 ? valid * 100.0 / total : 0.0;

        var first = total > 0
            ? $"{valid:N0} / {total:N0} s analyzed  ·  {percent:F1}% valid"
            : "Analyzed range unavailable";

        if (session.Metadata is not { } metadata)
        {
            return [first];
        }

        var secondParts = new List<string>();
        if (metadata.FrameCount > 0)
        {
            secondParts.Add($"{metadata.FrameCount:N0} recorded frames");
        }

        if (metadata.MetricCount > 0)
        {
            secondParts.Add($"{metadata.MetricCount:N0} telemetry metrics");
        }

        return secondParts.Count > 0
            ? [first, string.Join("  ·  ", secondParts)]
            : [first];
    }

    private static string RunAnalysisLine(
        SessionAnalysis? session,
        MethodologyContext methodology)
    {
        if (session is null
            || (!methodology.GpuFilterVaries
                && !methodology.EdgeTrimVaries
                && !methodology.TransitionPolicyVaries))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        var options = session.EffectiveOptions;
        if (methodology.GpuFilterVaries)
        {
            parts.Add(options.AutoGpuThreshold
                ? $"Auto GPU ≥ {options.GpuThreshold:F0}%"
                : $"GPU ≥ {options.GpuThreshold:F0}%");
        }

        if (methodology.EdgeTrimVaries)
        {
            parts.Add(options.TrimBufferSeconds > 0
                ? $"Trim {options.TrimBufferSeconds:F1} s"
                : "No trim");
        }

        if (methodology.TransitionPolicyVaries)
        {
            parts.Add(options.ExcludeTransitions ? "Loads excluded" : "Loads included");
        }

        return string.Join("  ·  ", parts);
    }

    private static void DrawMethodologyPanel(
        SKCanvas canvas,
        MethodologyContext methodology,
        ChartStyle style,
        int width,
        int top)
    {
        DrawPanelBackground(canvas, style, ReportPadding, top, width - ReportPadding, top + ReportMethodHeight);

        using var labelFont = CreateFieldLabelFont();
        using var valueFont = CreateMethodValueFont();
        using var labelPaint = new SKPaint { Color = ToSkColor(style.Muted) };
        using var valuePaint = new SKPaint { Color = ToSkColor(style.Foreground) };

        var columns = methodology.Fields.Count <= 2 ? methodology.Fields.Count : 2;
        columns = System.Math.Max(1, columns);
        var rows = (methodology.Fields.Count + columns - 1) / columns;
        var innerWidth = width - ReportPadding * 2 - 28;
        var cellWidth = innerWidth / (float)columns;
        var rowHeight = (ReportMethodHeight - 16) / (float)System.Math.Max(1, rows);

        for (var i = 0; i < methodology.Fields.Count; i++)
        {
            var row = i / columns;
            var column = i % columns;
            var left = ReportPadding + 14 + column * cellWidth;
            var cellTop = top + 8 + row * rowHeight;
            canvas.DrawText(
                methodology.Fields[i].Label,
                left,
                cellTop + 1 - labelFont.Metrics.Ascent,
                SKTextAlign.Left,
                labelFont,
                labelPaint);
            canvas.DrawText(
                Truncate(methodology.Fields[i].Value, System.Math.Max(24, (int)(cellWidth / 7.4))),
                left,
                cellTop + 20 - valueFont.Metrics.Ascent,
                SKTextAlign.Left,
                valueFont,
                valuePaint);
        }
    }

    private static void DrawPanelBackground(
        SKCanvas canvas,
        ChartStyle style,
        float left,
        float top,
        float right,
        float bottom)
    {
        using var fillPaint = new SKPaint { Color = ToSkColor(style.Grid.WithAlpha(0.08)) };
        using var borderPaint = new SKPaint
        {
            Color = ToSkColor(style.Grid.WithAlpha(0.8)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true,
        };
        var rect = new SKRect(left, top, right, bottom);
        canvas.DrawRoundRect(rect, 7, 7, fillPaint);
        canvas.DrawRoundRect(rect, 7, 7, borderPaint);
    }

    private sealed record ProfessionalBodyLayout(
        int PerformanceLabelTop,
        int SecondaryLabelTop,
        IReadOnlyList<PixelRect> PanelRects,
        int Bottom);

    private static ProfessionalBodyLayout BuildProfessionalBodyLayout(
        int width,
        int headerHeight,
        int panelCount,
        bool hasPrimaryFps)
    {
        if (panelCount <= 0)
        {
            return new ProfessionalBodyLayout(headerHeight, -1, [], headerHeight);
        }

        var rects = new List<PixelRect>(panelCount);
        var y = headerHeight;
        var performanceLabelTop = y;
        y += ReportBodySectionHeight;

        if (hasPrimaryFps)
        {
            rects.Add(new PixelRect(0, width, y + PrimaryRowHeight, y));
            y += PrimaryRowHeight;

            var secondaryCount = panelCount - 1;
            if (secondaryCount == 0)
            {
                return new ProfessionalBodyLayout(performanceLabelTop, -1, rects, y);
            }

            y += ReportBodySectionGap;
            var secondaryLabelTop = y;
            y += ReportBodySectionHeight;
            var rows = (secondaryCount + GridColumns - 1) / GridColumns;
            var gridBottom = y
                + rows * GridRowHeight
                + System.Math.Max(0, rows - 1) * GridGap;
            AddGridRects(rects, width, gridBottom, y, secondaryCount, rows);
            return new ProfessionalBodyLayout(performanceLabelTop, secondaryLabelTop, rects, gridBottom);
        }

        var plainRows = (panelCount + GridColumns - 1) / GridColumns;
        var plainBottom = y
            + plainRows * GridRowHeight
            + System.Math.Max(0, plainRows - 1) * GridGap;
        AddGridRects(rects, width, plainBottom, y, panelCount, plainRows);
        return new ProfessionalBodyLayout(performanceLabelTop, -1, rects, plainBottom);
    }

    private static void DrawProfessionalBodyLabels(
        SKCanvas canvas,
        ChartStyle style,
        int width,
        ProfessionalBodyLayout layout)
    {
        DrawBodySectionLabel(canvas, style, width, layout.PerformanceLabelTop, "PERFORMANCE TIMELINE");
        if (layout.SecondaryLabelTop >= 0)
        {
            DrawBodySectionLabel(canvas, style, width, layout.SecondaryLabelTop, "SECONDARY TELEMETRY");
        }
    }

    private static void DrawBodySectionLabel(
        SKCanvas canvas,
        ChartStyle style,
        int width,
        int top,
        string label)
    {
        using var font = CreateBodySectionFont();
        using var paint = new SKPaint { Color = ToSkColor(style.SeriesA) };
        using var divider = new SKPaint
        {
            Color = ToSkColor(style.Grid.WithAlpha(0.7)),
            StrokeWidth = 1,
            IsAntialias = true,
        };

        canvas.DrawText(
            label,
            ReportPadding,
            top + 8 - font.Metrics.Ascent,
            SKTextAlign.Left,
            font,
            paint);
        canvas.DrawLine(
            ReportPadding,
            top + ReportBodySectionHeight - 6,
            width - ReportPadding,
            top + ReportBodySectionHeight - 6,
            divider);
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
            ReportPadding,
            top + 12,
            width - ReportPadding,
            top + 12,
            divider);
        var baseline = top + 28 - font.Metrics.Ascent;
        canvas.DrawText(
            $"Frame Performance Analyzer  ·  {context.Runs.Count} benchmark(s)  ·  {context.MetricCount} chart(s)",
            ReportPadding,
            baseline,
            SKTextAlign.Left,
            font,
            paint);
        canvas.DrawText(
            $"Generated {DateTime.Now:yyyy-MM-dd HH:mm}",
            width - ReportPadding,
            baseline,
            SKTextAlign.Right,
            font,
            paint);
        _ = bottom;
    }

    internal static bool ShouldUseProfessionalLayout(ReportHeader header) =>
        header.UseProfessionalLayout || IsBenchmarkReportTitle(header.Title);

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
        Size = 31,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateProfessionalTypeFont() => new()
    {
        Size = 11,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateProfessionalContextFont() => new()
    {
        Size = 14,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateSectionLabelFont() => new()
    {
        Size = 10,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateFieldLabelFont() => new()
    {
        Size = 9,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateFieldValueFont() => new()
    {
        Size = 14,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateRunRoleFont() => new()
    {
        Size = 9,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateRunNameFont() => new()
    {
        Size = 15,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateRunDetailFont() => new()
    {
        Size = 10,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateMethodValueFont() => new()
    {
        Size = 11,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateBodySectionFont() => new()
    {
        Size = 10,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
        Edging = SKFontEdging.Antialias,
    };

    private static SKFont CreateFooterFont() => new()
    {
        Size = 11,
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
