using System.IO;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;
using SkiaSharp;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for the report legend theme, session-role header
/// lines, font-metric header measurement, and the header/first-chart
/// separation boundary.
/// </summary>
public class ReportPresentationTests
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

    [Fact]
    public void Single_series_legend_uses_the_explicit_report_theme()
    {
        var style = ChartStyle.FromApplicationResources();
        var multiplot = ReportPlotBuilder.Build([FpsGroup()], style);
        var plot = multiplot.Subplots.GetPlot(0);

        Assert.True(plot.Legend.IsVisible);
        Assert.Equal(style.Background.WithAlpha(0.92), plot.Legend.BackgroundColor);
        Assert.Equal(style.Foreground, plot.Legend.FontColor);
        Assert.Equal(style.Grid, plot.Legend.OutlineColor);
    }

    [Fact]
    public void Role_lines_identify_base_and_comparison_explicitly()
    {
        Assert.Equal("Base: GTA5 Enhanced", ExportReport.SessionRoleLine(SessionRole.Base, "GTA5 Enhanced"));
        Assert.Equal("Comparison: GTA5 Enhanced", ExportReport.SessionRoleLine(SessionRole.Comparison, "GTA5 Enhanced"));
    }

    [Fact]
    public void Measured_header_height_grows_with_its_lines()
    {
        var empty = ReportPlotBuilder.MeasureHeaderHeight(
            new ReportPlotBuilder.ReportHeader("T", []));
        var withBase = ReportPlotBuilder.MeasureHeaderHeight(
            new ReportPlotBuilder.ReportHeader("T", ["Base: GTA5 Enhanced"]));
        var allSessions = ReportPlotBuilder.MeasureHeaderHeight(
            new ReportPlotBuilder.ReportHeader("T", ["Base: GTA5 Enhanced", "Comparison: GTA5 Enhanced"]));

        Assert.True(empty > 0);
        Assert.True(withBase > empty);
        Assert.True(allSessions > withBase);
        Assert.True(allSessions >= empty + ReportPlotBuilder.HeaderSeparationGap);
    }

    [Fact]
    public void First_panel_starts_below_header_plus_separation_gap()
    {
        var header = new ReportPlotBuilder.ReportHeader(
            "Night Run",
            ["Base: GTA5 Enhanced", "Comparison: GTA5 Enhanced"]);
        var headerHeight = ReportPlotBuilder.MeasureHeaderHeight(header);

        var rect = ReportPlotBuilder.ReportContentRect(1600, 2000, headerHeight);

        Assert.Equal(headerHeight, rect.Top);
        Assert.True(rect.Top >= ReportPlotBuilder.HeaderSeparationGap);
        Assert.Equal(2000, rect.Bottom);
        Assert.Equal(1600, rect.Right - rect.Left);
    }

    [Fact]
    public void Header_and_first_chart_do_not_overlap_in_a_real_render()
    {
        var style = ChartStyle.FromApplicationResources();
        var groups = new[]
        {
            FpsGroup(),
            new ReportPlotBuilder.ReportGroup(
                CoreMetricCatalog.CoreById["frametime"],
                FpsGroup().Series),
        };
        var header = new ReportPlotBuilder.ReportHeader(
            "GTA5 Enhanced — Night Run",
            ["2560x1440 · NVIDIA GeForce RTX 5070 Ti", "Base: GTA5 Enhanced", "Comparison: GTA5 Enhanced"]);

        var path = Path.Combine(Path.GetTempPath(), "fva-boundary-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var multiplot = ReportPlotBuilder.Build(groups, style);
            ReportPlotBuilder.SavePng(multiplot, style, header, path, 1600, 2 * 520 + 110);

            using var bitmap = SKBitmap.Decode(path);
            Assert.NotNull(bitmap);

            var headerHeight = ReportPlotBuilder.MeasureHeaderHeight(header);

            // Last bright row inside the header band vs first bright row
            // inside the first panel: the gap between them must be at least
            // a few pixels (no header/title overlap).
            var lastHeaderRow = -1;
            var firstPanelRow = -1;
            for (var y = 0; y < bitmap.Height; y++)
            {
                var bright = false;
                for (var x = 0; x < bitmap.Width; x += 8)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.Red > 8 || pixel.Green > 8 || pixel.Blue > 8)
                    {
                        bright = true;
                        break;
                    }
                }

                if (bright && y < headerHeight)
                {
                    lastHeaderRow = y;
                }

                if (bright && y >= headerHeight && firstPanelRow < 0)
                {
                    firstPanelRow = y;
                }
            }

            Assert.True(lastHeaderRow > 0, "Header text missing.");
            Assert.True(firstPanelRow > headerHeight, "No panel content below the header.");
            Assert.True(firstPanelRow - lastHeaderRow >= 4,
                $"Header overlaps the first panel: header ends {lastHeaderRow}, panel starts {firstPanelRow}.");
        }
        finally
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
}
