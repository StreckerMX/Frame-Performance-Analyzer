using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for the SessionChartView range-selection gesture
/// lifecycle (drag pan OFF). The previous MouseUp finalization depended on
/// _selectionOverlay existing, so a plain click left _selectStartX and the
/// mouse capture active — a later move could then create a selection with no
/// button pressed. The real view drives its real state machine through the
/// internal gesture seams (Begin/Update/End) on the shared STA host.
/// NOTE: headless WPF cannot hold real mouse capture (no presentation source
/// input), so capture is asserted in the only observable direction available:
/// after finalization no capture may be held. The release path itself is
/// unconditional in CancelSelection() whenever capture exists.
/// </summary>
public class SessionChartViewSelectionGestureTests
{
    private sealed record Host(SessionChartView View, WpfPlot Plot, int BaselineSpanCount);

    private static Host CreateHost()
    {
        var view = new SessionChartView();
        var metric = CoreMetricCatalog.CoreById["fps"];
        var series = new MetricSeries(
            metric,
            [0.0, 2.0, 4.0, 6.0, 8.0, 10.0],
            [120.0, 100.0, 90.0, 95.0, 88.0, 92.0],
            "Base",
            SessionRole.Base);
        view.ShowData(metric, [series]);
        view.ApplyInteractions(wheelZoomEnabled: true, panEnabled: false, markersVisible: false);

        var plot = (WpfPlot)view.FindName("ChartHost")!;
        // The interactive chart legitimately contains HorizontalSpans for
        // omitted-load gaps; only the selection overlay changes this count.
        var baseline = plot.Plot.GetPlottables<HorizontalSpan>().Count();
        return new Host(view, plot, baseline);
    }

    private static AxisLimitsView LimitsOf(Host host) => AxisLimitsView.Of(host.Plot.Plot.Axes.GetLimits());

    private readonly record struct AxisLimitsView(double Left, double Right, double Bottom, double Top)
    {
        public static AxisLimitsView Of(AxisLimits limits) =>
            new(limits.Left, limits.Right, limits.Bottom, limits.Top);
    }

    private static int SpanCountOf(Host host) =>
        host.Plot.Plot.GetPlottables<HorizontalSpan>().Count();

    private static void AssertNoSelectionState(Host host)
    {
        Assert.False(host.View.IsRangeSelectionActive, "Selection gesture state must be cleared.");
        Assert.False(host.Plot.IsMouseCaptured, "No mouse capture may remain after finalization.");
        Assert.Equal(host.BaselineSpanCount, SpanCountOf(host));
    }

    private static void AssertLimitsUnchanged(AxisLimitsView before, AxisLimitsView after)
    {
        Assert.Equal(before.Left, after.Left, precision: 4);
        Assert.Equal(before.Right, after.Right, precision: 4);
        Assert.Equal(before.Bottom, after.Bottom, precision: 4);
        Assert.Equal(before.Top, after.Top, precision: 4);
    }

    [Fact]
    public void Click_without_drag_finalizes_the_gesture_and_releases_everything() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var host = CreateHost();
            var before = LimitsOf(host);

            // MouseDown: the gesture begins.
            host.View.BeginRangeSelection(2.0);
            Assert.True(host.View.IsRangeSelectionActive);

            // MouseUp at effectively the same X, with no MouseMove ever
            // having created an overlay: the gesture must still finalize.
            host.View.EndRangeSelection(2.0);

            AssertNoSelectionState(host);
            AssertLimitsUnchanged(before, LimitsOf(host));
        });

    [Fact]
    public void Move_after_a_completed_click_cannot_resurrect_a_selection() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var host = CreateHost();
            var before = LimitsOf(host);

            host.View.BeginRangeSelection(2.0);
            host.View.EndRangeSelection(2.0);

            // A later pointer move with no button held must not begin a
            // selection, draw an overlay, or re-capture the mouse.
            host.View.UpdateRangeSelection(8.0);

            AssertNoSelectionState(host);
            AssertLimitsUnchanged(before, LimitsOf(host));
        });

    [Fact]
    public void Short_drag_below_one_second_is_ignored_and_fully_cancelled() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var host = CreateHost();
            var before = LimitsOf(host);

            host.View.BeginRangeSelection(2.0);
            host.View.UpdateRangeSelection(2.4);
            Assert.Equal(host.BaselineSpanCount + 1, SpanCountOf(host));

            host.View.EndRangeSelection(2.4);

            AssertNoSelectionState(host);
            AssertLimitsUnchanged(before, LimitsOf(host));
        });

    [Fact]
    public void Valid_drag_applies_the_selected_range_and_cleans_up() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var host = CreateHost();

            host.View.BeginRangeSelection(2.0);
            host.View.UpdateRangeSelection(4.0);
            host.View.EndRangeSelection(6.0);

            AssertNoSelectionState(host);

            var limits = LimitsOf(host);
            Assert.Equal(2.0, limits.Left, precision: 4);
            Assert.Equal(6.0, limits.Right, precision: 4);
        });

    [Fact]
    public void Reverse_drag_applies_the_same_ordered_range() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var host = CreateHost();

            host.View.BeginRangeSelection(6.0);
            host.View.UpdateRangeSelection(4.0);
            host.View.EndRangeSelection(2.0);

            AssertNoSelectionState(host);

            var limits = LimitsOf(host);
            Assert.Equal(2.0, limits.Left, precision: 4);
            Assert.Equal(6.0, limits.Right, precision: 4);
        });
}
