using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;
using ScottPlot.WPF;

namespace FrameViewAnalyzer.App.Tests;

public class SessionChartViewMetricRangePersistenceTests
{
    private static MetricSeries Series(
        MetricDefinition metric,
        string label,
        double[] values) =>
        new(
            metric,
            [0.0, 2.0, 4.0, 6.0, 8.0, 10.0],
            values,
            label,
            SessionRole.Base,
            WorkspaceIndex: 0);

    private static AxisLimits LimitsOf(SessionChartView view)
    {
        var plot = (WpfPlot)view.FindName("ChartHost")!;
        return plot.Plot.Axes.GetLimits();
    }

    [Fact]
    public void Metric_change_preserves_the_selected_time_window()
    {
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var view = new SessionChartView();
            var fps = CoreMetricCatalog.CoreById["fps"];
            var frameTime = CoreMetricCatalog.CoreById["frametime"];

            view.ShowData(fps, [Series(fps, "Base", [120, 118, 100, 98, 110, 112])]);
            view.ApplyInteractions(wheelZoomEnabled: true, panEnabled: false, framePointsEnabled: false);
            view.BeginRangeSelection(2.0);
            view.UpdateRangeSelection(4.0);
            view.EndRangeSelection(6.0);

            var selected = LimitsOf(view);
            Assert.Equal(2.0, selected.Left, precision: 4);
            Assert.Equal(6.0, selected.Right, precision: 4);

            view.ShowData(frameTime, [Series(frameTime, "Base", [8.3, 8.5, 10.0, 10.2, 9.1, 8.9])]);

            var switched = LimitsOf(view);
            Assert.Equal(2.0, switched.Left, precision: 4);
            Assert.Equal(6.0, switched.Right, precision: 4);
            Assert.True(switched.Bottom < 8.5);
            Assert.True(switched.Top > 10.2);
        });
    }

    [Fact]
    public void Different_workspace_resets_to_the_new_full_range()
    {
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var view = new SessionChartView();
            var fps = CoreMetricCatalog.CoreById["fps"];
            var frameTime = CoreMetricCatalog.CoreById["frametime"];

            view.ShowData(fps, [Series(fps, "Old workspace", [120, 118, 100, 98, 110, 112])]);
            view.ZoomToRange(2.0, 6.0);

            view.ShowData(frameTime, [Series(frameTime, "New workspace", [8.3, 8.5, 10.0, 10.2, 9.1, 8.9])]);

            var limits = LimitsOf(view);
            Assert.Equal(0.0, limits.Left, precision: 4);
            Assert.Equal(10.0, limits.Right, precision: 4);
        });
    }
}
