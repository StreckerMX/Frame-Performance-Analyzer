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
    public void Build_creates_the_header_plus_one_subplot_per_group()
    {
        var multiplot = ReportPlotBuilder.Build(
            [FpsGroup(), FrametimeGroup()],
            ChartStyle.FromApplicationResources(),
            Header());

        Assert.True(multiplot.Subplots.Count >= 3);
        Assert.NotNull(multiplot.Subplots.GetPlot(0));
    }

    [Fact]
    public void Single_session_report_renders_a_valid_png()
    {
        var path = TempPng();
        try
        {
            var multiplot = ReportPlotBuilder.Build(
                [new ReportPlotBuilder.ReportGroup(
                    CoreMetricCatalog.CoreById["fps"],
                    [FpsGroup().Series[0]])],
                ChartStyle.FromApplicationResources(),
                Header());

            ReportPlotBuilder.SavePng(multiplot, path, 800, 520);

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
            var multiplot = ReportPlotBuilder.Build(
                [FpsGroup(), FrametimeGroup()],
                ChartStyle.FromApplicationResources(),
                Header());

            ReportPlotBuilder.SavePng(multiplot, path, 800, 1040);

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
            ChartStyle.FromApplicationResources(),
            Header());
        Assert.True(multiplot.Subplots.Count >= 2);

        Assert.Equal(beforeSelected, viewModel.SelectedMetric);
        Assert.Equal(beforeHasData, viewModel.HasData);
        Assert.Equal(beforeMetrics, viewModel.Metrics.Count);
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
