using System.IO;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using SkiaSharp;

namespace FrameViewAnalyzer.App.Tests;

public class ReportHeaderVisibilityTests
{
    private static ReportPlotBuilder.ReportGroup FpsGroup() => new(
        CoreMetricCatalog.CoreById["fps"],
        [
            new MetricSeries(
                CoreMetricCatalog.CoreById["fps"],
                [0.0, 1.0, 2.0],
                [100.0, 120.0, 90.0],
                Role: SessionRole.Base),
        ]);

    private static ReportPlotBuilder.ReportHeader Header() => new(
        "Night Run",
        ["3840x2160  ·  RTX 5090  ·  Ryzen 7", "Base: GTA5 Enhanced"]);

    private static string TempPng() =>
        Path.Combine(Path.GetTempPath(), "fva-hdr-" + Guid.NewGuid().ToString("N") + ".png");

    private static int CountBrightRows(SKBitmap bitmap, int yStart, int yEnd)
    {
        var count = 0;
        for (var y = yStart; y < yEnd && y < bitmap.Height; y += 2)
        {
            for (var x = 10; x < bitmap.Width; x += 6)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 8 || pixel.Green > 8 || pixel.Blue > 8)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    [Fact]
    public void Header_text_is_visible_in_the_header_region()
    {
        var path = TempPng();
        try
        {
            var style = ChartStyle.FromApplicationResources();
            var multiplot = ReportPlotBuilder.Build([FpsGroup()], style);
            ReportPlotBuilder.SavePng(multiplot, style, Header(), path, 800, 630);

            using var bitmap = SKBitmap.Decode(path);
            Assert.NotNull(bitmap);

            var headerHeight = ReportPlotBuilder.MeasureHeaderHeight(Header());
            var headerRows = CountBrightRows(bitmap, 10, headerHeight);
            Assert.True(headerRows > 3,
                $"Header region has no visible text ({headerRows} bright rows, height {headerHeight}).");
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Header_is_the_only_content_when_there_are_no_metric_plots()
    {
        // With zero metric groups, the header text is the sole content: if the
        // plot renderer were overwriting the top of the canvas, this band
        // would be empty and the report would be a black rectangle.
        var path = TempPng();
        try
        {
            var style = ChartStyle.FromApplicationResources();
            var multiplot = ReportPlotBuilder.Build([], style);
            ReportPlotBuilder.SavePng(multiplot, style, Header(), path, 800, 300);

            using var bitmap = SKBitmap.Decode(path);
            Assert.NotNull(bitmap);

            var headerHeight = ReportPlotBuilder.MeasureHeaderHeight(Header());
            var headerRows = CountBrightRows(bitmap, 10, headerHeight);
            var belowRows = CountBrightRows(bitmap, headerHeight + 4, bitmap.Height);
            Assert.True(headerRows > 3,
                $"Header text missing with no plots ({headerRows} bright rows).");
            Assert.Equal(0, belowRows);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
