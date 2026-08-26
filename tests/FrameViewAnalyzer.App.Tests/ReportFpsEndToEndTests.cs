using System.IO;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Models;
using SkiaSharp;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// End-to-end FPS report reproduction: builds a session through the real
/// export pipeline (SeriesBuilder + ReportPlotBuilder) and renders the
/// multiplot, then verifies the adaptive FPS panel limits still contain every
/// full-resolution value.
/// </summary>
public class ReportFpsEndToEndTests
{
    [Fact]
    public void Rendered_fps_panel_keeps_full_resolution_limits()
    {
        var baseSession = Session(peakFps: 142.6);
        var comparisonSession = Session(peakFps: 110.8);

        var baseSeries = SeriesBuilder.Build(baseSession, "fps") with { Role = SessionRole.Base };
        var comparisonSeries = SeriesBuilder.Build(comparisonSession, "fps") with
        {
            Label = "Comparison",
            Role = SessionRole.Comparison,
        };

        var group = new ReportPlotBuilder.ReportGroup(
            baseSession.Catalog.First(metric => metric.Id == "fps"),
            [baseSeries, comparisonSeries]);

        var style = ChartStyle.FromApplicationResources();
        var multiplot = ReportPlotBuilder.Build([group], style);

        // Render the panels with the report renderer, then read back the
        // axes: rendering must not overwrite the fitted full-resolution
        // limits (the old Multiplot.Render path re-autoscaled them).
        using var bitmap = new SKBitmap(800, 600);
        using var canvas = new SKCanvas(bitmap);
        ReportPlotBuilder.RenderPanels(canvas, multiplot, 800, 600, 0);

        var limits = multiplot.Subplots.GetPlot(0).Axes.GetLimits();
        var baseMin = baseSeries.Y.Min();
        var comparisonMin = comparisonSeries.Y.Min();
        var baseMax = baseSeries.Y.Max();
        var comparisonMax = comparisonSeries.Y.Max();
        var globalMin = System.Math.Min(baseMin, comparisonMin);

        Assert.True(limits.Bottom > 0, "Adaptive FPS reports should not force a zero baseline.");
        Assert.True(limits.Bottom <= globalMin, "Bottom must contain the full-resolution minimum.");
        Assert.True(limits.Top > 142.6, $"FPS top clipped after render: {limits.Top}");
        Assert.True(limits.VerticalSpan >= ChartViewport.MinimumFpsVerticalSpan);
        Assert.True(baseMax >= 140, $"Synthetic base peak lost: {baseMax}");
        Assert.True(comparisonMax >= 108, $"Synthetic comparison peak lost: {comparisonMax}");
        Assert.True(limits.Top > baseMax, "Top must contain the full-resolution base maximum.");
    }

    /// <summary>One-second bins whose harmonic FPS climbs to the given peak.</summary>
    private static SessionAnalysis Session(double peakFps)
    {
        var rows = new List<string[]>();
        for (var second = 0; second < 120; second++)
        {
            var fps = second < 60
                ? 72.0 + (peakFps - 72.0) * second / 59.0
                : peakFps - (peakFps - 100.0) * (second - 60) / 59.0;
            var frameMs = 1000.0 / fps;
            // Four frames per second: bins carry at least MinFramesPerBin
            // frames, so the analyzer keeps every bin.
            for (var frame = 0; frame < 4; frame++)
            {
                rows.Add([(second + frame * 0.25).ToString("F2"), frameMs.ToString("F3"), "80.0"]);
            }
        }

        var capture = CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            [.. rows]);

        return new CaptureAnalysisService().Analyze(
            capture,
            new AnalysisOptions(
                GpuThreshold: 25,
                TrimBufferSeconds: 1,
                AutoGpuThreshold: false,
                ExcludeTransitions: false));
    }

    private static CaptureData CaptureWith(string[] headers, string[][] rows)
    {
        var columns = new string[headers.Length][];
        for (var i = 0; i < headers.Length; i++)
        {
            columns[i] = new string[rows.Length];
            for (var r = 0; r < rows.Length; r++)
            {
                columns[i][r] = rows[r][i];
            }
        }

        return new CaptureData
        {
            Path = "capture.csv",
            DisplayName = "capture",
            Kind = CsvKind.Log,
            Headers = headers,
            Columns = columns,
        };
    }
}
