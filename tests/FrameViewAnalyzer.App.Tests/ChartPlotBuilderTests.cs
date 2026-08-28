using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;
using ScottPlot.Plottables;

namespace FrameViewAnalyzer.App.Tests;

public class ChartPlotBuilderTests
{
    private static MetricDefinition Fps => CoreMetricCatalog.CoreById["fps"];

    private static MetricSeries Series(
        double[] xs,
        double[] ys,
        string? label = null,
        SessionRole role = SessionRole.Base) =>
        new(Fps, xs, ys, label, role);

    private static MetricSeries Series(
        MetricDefinition metric,
        double[] xs,
        double[] ys,
        string? label = null,
        SessionRole role = SessionRole.Base) =>
        new(metric, xs, ys, label, role);

    private static readonly ChartStyle Style = ChartStyle.FromApplicationResources();

    [Fact]
    public void Fps_series_renders_as_signal_xy_on_the_analyzed_timeline()
    {
        var plot = new Plot();
        var xs = Enumerable.Range(0, 20).Select(i => (double)i).ToArray();
        var ys = Enumerable.Range(0, 20).Select(i => 100.0).ToArray();

        ChartPlotBuilder.Build(plot, Fps, [Series(xs, ys)], Style, pointBudget: 200);

        Assert.Contains(plot.PlottableList, plottable => plottable is SignalXY);
        Assert.DoesNotContain(plot.PlottableList, plottable => plottable is Scatter);
        Assert.Contains(plot.PlottableList, plottable => plottable is HorizontalLine);
        Assert.Equal("Analyzed time (s)", plot.Axes.Bottom.Label.Text);
    }

    [Fact]
    public void Legacy_time_gaps_are_not_shaded_or_line_broken()
    {
        var plot = new Plot();
        var xs = new double[] { 0, 1, 2, 10, 11, 12 };
        var ys = new double[] { 100, 101, 102, 110, 111, 112 };

        ChartPlotBuilder.Build(plot, Fps, [Series(xs, ys)], Style, pointBudget: 200);

        Assert.Contains(plot.PlottableList, plottable => plottable is SignalXY);
        Assert.DoesNotContain(plot.PlottableList, plottable => plottable is Scatter);
        Assert.DoesNotContain(plot.PlottableList, plottable => plottable is HorizontalSpan);
    }

    [Fact]
    public void Kind_selection_uses_signal_xy_for_fps_and_scatter_for_other_metrics()
    {
        Assert.Equal(
            ChartPlotBuilder.PlotKind.SignalXY,
            ChartPlotBuilder.ChooseKind(Fps, [0.0, 1.0, 2.0, 3.0]));
        Assert.Equal(
            ChartPlotBuilder.PlotKind.SignalXY,
            ChartPlotBuilder.ChooseKind(Fps, [0.0, 1.0, 10.0, 11.0]));
        Assert.Equal(
            ChartPlotBuilder.PlotKind.Scatter,
            ChartPlotBuilder.ChooseKind(CoreMetricCatalog.CoreById["frametime"], [0.0, 1.0, 2.0]));
    }

    [Fact]
    public void Grid_uses_only_major_lines_that_match_labeled_ticks()
    {
        var plot = new Plot();
        ChartPlotBuilder.Build(
            plot,
            Fps,
            [Series([0.0, 1.0, 2.0], [100.0, 101.0, 102.0])],
            Style,
            pointBudget: 200);

        Assert.True(plot.Grid.XAxisStyle.MajorLineStyle.Width >= 1f);
        Assert.True(plot.Grid.YAxisStyle.MajorLineStyle.Width >= 1f);
        Assert.True(plot.Grid.XAxisStyle.MajorLineStyle.AntiAlias);
        Assert.True(plot.Grid.YAxisStyle.MajorLineStyle.AntiAlias);
        Assert.False(plot.Grid.XAxisStyle.MinorLineStyle.IsVisible);
        Assert.False(plot.Grid.YAxisStyle.MinorLineStyle.IsVisible);
        Assert.Equal(0f, plot.Grid.XAxisStyle.MinorLineStyle.Width);
        Assert.Equal(0f, plot.Grid.YAxisStyle.MinorLineStyle.Width);
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
    public void Decimation_respects_the_point_budget_for_scatter_metrics()
    {
        var plot = new Plot();
        var metric = CoreMetricCatalog.CoreById["frametime"];
        var xs = Enumerable.Range(0, 10_000).Select(i => (double)i / 10.0).ToArray();
        var ys = Enumerable.Range(0, 10_000).Select(i => 10 + 3 * System.Math.Sin(i / 100.0)).ToArray();

        ChartPlotBuilder.Build(plot, metric, [Series(metric, xs, ys)], Style, pointBudget: 100);

        var scatter = plot.PlottableList.OfType<Scatter>().Single();
        var pointCount = scatter.Data.GetScatterPoints().Count;
        Assert.True(pointCount < 500, $"expected decimation below 500 points, got {pointCount}");
    }

    [Fact]
    public void Style_falls_back_to_the_dark_palette_without_an_application()
    {
        var style = ChartStyle.FromApplicationResources();

        Assert.NotNull(style);
        Assert.Equal(ScottPlot.Color.FromHex("#000000"), style.Background);
    }

    [Fact]
    public void Base_only_metric_uses_the_series_a_color()
    {
        var plot = new Plot();
        var xs = Enumerable.Range(0, 10).Select(i => (double)i).ToArray();
        var ys = Enumerable.Range(0, 10).Select(i => 100.0).ToArray();

        ChartPlotBuilder.Build(
            plot,
            Fps,
            [Series(xs, ys, role: SessionRole.Base)],
            Style,
            pointBudget: 200);

        var signal = plot.PlottableList.OfType<SignalXY>().Single();
        Assert.Equal(Style.SeriesA.ARGB, signal.Color.ARGB);
        var average = plot.PlottableList.OfType<HorizontalLine>().Single();
        Assert.Equal(Style.SeriesA.WithAlpha(0.85).ARGB, average.LineColor.ARGB);
    }

    [Fact]
    public void Comparison_only_metric_uses_the_series_b_color_even_as_the_only_series()
    {
        var plot = new Plot();
        var xs = Enumerable.Range(0, 10).Select(i => (double)i).ToArray();
        var ys = Enumerable.Range(0, 10).Select(i => 60.0).ToArray();

        ChartPlotBuilder.Build(
            plot,
            Fps,
            [Series(xs, ys, "Comparison", SessionRole.Comparison)],
            Style,
            pointBudget: 200);

        var signal = plot.PlottableList.OfType<SignalXY>().Single();
        Assert.Equal(Style.SeriesB.ARGB, signal.Color.ARGB);
        var average = plot.PlottableList.OfType<HorizontalLine>().Single();
        Assert.Equal(Style.SeriesB.WithAlpha(0.85).ARGB, average.LineColor.ARGB);
    }

    [Fact]
    public void Shared_metric_uses_series_a_then_series_b_by_session_role()
    {
        var plot = new Plot();
        var xs = Enumerable.Range(0, 10).Select(i => (double)i).ToArray();
        var ys = Enumerable.Range(0, 10).Select(i => 100.0).ToArray();

        ChartPlotBuilder.Build(
            plot,
            Fps,
            [
                Series(xs, ys, "Base", SessionRole.Base),
                Series(xs, ys.Select(v => v - 40.0).ToArray(), "Comparison", SessionRole.Comparison),
            ],
            Style,
            pointBudget: 200);

        var signals = plot.PlottableList.OfType<SignalXY>().ToList();
        Assert.Equal(2, signals.Count);
        Assert.Equal(Style.SeriesA.ARGB, signals[0].Color.ARGB);
        Assert.Equal(Style.SeriesB.ARGB, signals[1].Color.ARGB);
        Assert.Equal("Base", signals[0].LegendText);
        Assert.Equal("Comparison", signals[1].LegendText);

        var averages = plot.PlottableList.OfType<HorizontalLine>().ToList();
        Assert.Equal(2, averages.Count);
        Assert.Equal(Style.SeriesA.WithAlpha(0.85).ARGB, averages[0].LineColor.ARGB);
        Assert.Equal(Style.SeriesB.WithAlpha(0.85).ARGB, averages[1].LineColor.ARGB);
    }
}
