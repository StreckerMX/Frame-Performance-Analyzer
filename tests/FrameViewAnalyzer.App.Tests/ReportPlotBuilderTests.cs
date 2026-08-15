using System.IO;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;

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

    [Fact]
    public void Build_creates_one_subplot_per_group()
    {
        var multiplot = ReportPlotBuilder.Build(
            [FpsGroup()],
            ChartStyle.FromApplicationResources());

        Assert.True(multiplot.Subplots.Count >= 1);
        Assert.NotNull(multiplot.Subplots.GetPlot(0));
    }

    [Fact]
    public void Save_png_writes_a_non_empty_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "fva-report-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var multiplot = ReportPlotBuilder.Build(
                [FpsGroup()],
                ChartStyle.FromApplicationResources());

            ReportPlotBuilder.SavePng(multiplot, path, 800, 520);

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
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
}
