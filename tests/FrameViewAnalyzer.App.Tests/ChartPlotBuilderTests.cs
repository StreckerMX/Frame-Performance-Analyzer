using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;
using ScottPlot.Plottables;

namespace FrameViewAnalyzer.App.Tests;

public class ChartPlotBuilderTests
{
    private static MetricDefinition Fps => CoreMetricCatalog.CoreById["fps"];

    private static MetricSeries Series(double[] xs, double[] ys, string? label = null) =>
        new(Fps, xs, ys, label);

    private static readonly ChartStyle Style = ChartStyle.FromApplicationResources();

    [Fact]
    public void Gap_free_fps_series_renders_as_signal_xy()
    {
        var plot = new Plot();
        var xs = Enumerable.Range(0, 20).Select(i => (double)i).ToArray();
        var ys = Enumerable.Range(0, 20).Select(i => 100.0).ToArray();

        ChartPlotBuilder.Build(plot, Fps, [Series(xs, ys)], Style, pointBudget: 200);

        Assert.Contains(plot.PlottableList, plottable => plottable is SignalXY);
        Assert.DoesNotContain(plot.PlottableList, plottable => plottable is Scatter);
        Assert.Contains(plot.PlottableList, plottable => plottable is HorizontalLine);
    }

    [Fact]
    public void Gap_broken_series_renders_as_scatter()
    {
        var plot = new Plot();
        var xs = new double[] { 0, 1, 2, 10, 11, 12 };
        var ys = new double[] { 100, 101, 102, 110, 111, 112 };

        ChartPlotBuilder.Build(plot, Fps, [Series(xs, ys)], Style, pointBudget: 200);

        Assert.Contains(plot.PlottableList, plottable => plottable is Scatter);
        Assert.DoesNotContain(plot.PlottableList, plottable => plottable is SignalXY);
    }

    [Fact]
    public void Kind_selection_prefers_signal_xy_only_for_uniform_fps()
    {
        Assert.Equal(
            ChartPlotBuilder.PlotKind.SignalXY,
            ChartPlotBuilder.ChooseKind(Fps, [0.0, 1.0, 2.0, 3.0]));
        Assert.Equal(
            ChartPlotBuilder.PlotKind.Scatter,
            ChartPlotBuilder.ChooseKind(Fps, [0.0, 1.0, 10.0, 11.0]));
        Assert.Equal(
            ChartPlotBuilder.PlotKind.Scatter,
            ChartPlotBuilder.ChooseKind(CoreMetricCatalog.CoreById["frametime"], [0.0, 1.0, 2.0]));
    }

    [Fact]
    public void Multiple_series_enable_the_legend()
    {
        var plot = new Plot();
        var xs = Enumerable.Range(0, 10).Select(i => (double)i).ToArray();
        var ys = Enumerable.Range(0, 10).Select(i => 90.0).ToArray();

        ChartPlotBuilder.Build(
            plot,
            Fps,
            [Series(xs, ys, "Base"), Series(xs, ys.Select(v => v + 5).ToArray(), "Comparison")],
            Style,
            pointBudget: 200);

        Assert.True(plot.Legend.IsVisible);
    }

    [Fact]
    public void Decimation_respects_the_point_budget()
    {
        var plot = new Plot();
        var xs = Enumerable.Range(0, 10_000).Select(i => (double)i / 10.0).ToArray();
        var ys = Enumerable.Range(0, 10_000).Select(i => 50 + 30 * System.Math.Sin(i / 100.0)).ToArray();

        ChartPlotBuilder.Build(plot, Fps, [Series(xs, ys)], Style, pointBudget: 100);

        var scatter = plot.PlottableList.OfType<Scatter>().Single();
        var pointCount = scatter.Data.GetScatterPoints().Count;
        Assert.True(pointCount < 500, $"expected decimation below 500 points, got {pointCount}");
    }

    [Fact]
    public void Style_falls_back_to_the_dark_palette_without_an_application()
    {
        var style = ChartStyle.FromApplicationResources();

        Assert.NotNull(style);
        Assert.Equal(ScottPlot.Color.FromHex("#080808"), style.Background);
    }
}
