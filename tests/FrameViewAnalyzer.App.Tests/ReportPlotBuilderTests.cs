using System.IO;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using SkiaSharp;

namespace FrameViewAnalyzer.App.Tests;

public class ReportPlotBuilderTests
{
    private static ReportPlotBuilder.ReportGroup FpsGroup() => new(
        CoreMetricCatalog.CoreById["fps"],
        [
            new MetricSeries(
                CoreMetricCatalog.CoreById["fps"],
                [0.0, 1.0, 2.0],
                [100.0, 120.0, 90.0],
                Role: SessionRole.Base),
            new MetricSeries(
                CoreMetricCatalog.CoreById["fps"],
                [0.0, 1.0, 2.0],
                [80.0, 90.0, 70.0],
                "Comparison",
                SessionRole.Comparison),
        ]);

    private static ReportPlotBuilder.ReportGroup FrametimeGroup() => new(
        CoreMetricCatalog.CoreById["frametime"],
        [
            new MetricSeries(
                CoreMetricCatalog.CoreById["frametime"],
                [0.0, 1.0, 2.0],
                [10.0, 9.0, 11.0],
                Role: SessionRole.Base),
        ]);

    private static ReportPlotBuilder.ReportHeader Header() => new(
        "Night Run",
        ["3840x2160  ·  RTX 5090  ·  Ryzen 7", "DLSS Quality"]);

    private static string TempPng() =>
        Path.Combine(Path.GetTempPath(), "fva-report-" + Guid.NewGuid().ToString("N") + ".png");

    [Fact]
    public void Build_creates_exactly_one_subplot_per_group_with_no_header_plot()
    {
        var multiplot = ReportPlotBuilder.Build(
            [FpsGroup(), FrametimeGroup()],
            ChartStyle.FromApplicationResources());

        // The header is drawn as text by SavePng — it must never occupy a
        // plot panel, so there is no empty default-axes chart in the report.
        Assert.Equal(2, multiplot.Subplots.Count);
        Assert.NotNull(multiplot.Subplots.GetPlot(0));
        Assert.NotNull(multiplot.Subplots.GetPlot(1));
    }

    [Fact]
    public void Single_session_report_renders_a_valid_png()
    {
        var path = TempPng();
        try
        {
            var style = ChartStyle.FromApplicationResources();
            var multiplot = ReportPlotBuilder.Build(
                [new ReportPlotBuilder.ReportGroup(
                    CoreMetricCatalog.CoreById["fps"],
                    [FpsGroup().Series[0]])],
                style);

            ReportPlotBuilder.SavePng(multiplot, style, Header(), path, 800, 630);

            AssertValidPng(path);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Base_and_comparison_report_renders_a_valid_png()
    {
        var path = TempPng();
        try
        {
            var style = ChartStyle.FromApplicationResources();
            var multiplot = ReportPlotBuilder.Build(
                [FpsGroup(), FrametimeGroup()],
                style);

            ReportPlotBuilder.SavePng(multiplot, style, Header(), path, 800, 1150);

            AssertValidPng(path);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Report_generation_does_not_mutate_the_chart_view_model()
    {
        var viewModel = new ChartViewModel();
        var beforeSelected = viewModel.SelectedMetric;
        var beforeHasData = viewModel.HasData;
        var beforeMetrics = viewModel.Metrics.Count;

        var multiplot = ReportPlotBuilder.Build(
            [FpsGroup()],
            ChartStyle.FromApplicationResources());
        Assert.Equal(1, multiplot.Subplots.Count);

        Assert.Equal(beforeSelected, viewModel.SelectedMetric);
        Assert.Equal(beforeHasData, viewModel.HasData);
        Assert.Equal(beforeMetrics, viewModel.Metrics.Count);
    }

    [Fact]
    public void Report_panels_span_the_full_width_with_content_on_the_right()
    {
        var path = TempPng();
        try
        {
            var style = ChartStyle.FromApplicationResources();
            var multiplot = ReportPlotBuilder.Build(
                [FpsGroup(), FrametimeGroup()],
                style);

            ReportPlotBuilder.SavePng(multiplot, style, Header(), path, 800, 1150);

            using var bitmap = SKBitmap.Decode(path);
            Assert.NotNull(bitmap);

            // Chart backgrounds are near-black; grid lines, series, and axis
            // labels are brighter. Count scan rows below the header that have
            // ANY non-background pixel in the rightmost 40% of the image —
            // narrow left-column charts would leave that region empty.
            var rowsWithRightSideContent = 0;
            for (var y = 200; y < bitmap.Height; y += 20)
            {
                for (var x = (int)(bitmap.Width * 0.6); x < bitmap.Width; x += 10)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.Red > 8 || pixel.Green > 8 || pixel.Blue > 8)
                    {
                        rowsWithRightSideContent++;
                        break;
                    }
                }
            }

            Assert.True(rowsWithRightSideContent > 5,
                $"Right side of the report is empty ({rowsWithRightSideContent} rows with content) — panels are not full-width.");
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static void AssertValidPng(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 8);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);

        using var bitmap = SKBitmap.Decode(path);
        Assert.NotNull(bitmap);
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
