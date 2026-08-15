using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Tooltip point-selection regression tests: every series is probed against
/// the cursor independently, nearest real samples only, deterministic anchor.
/// </summary>
public class SeriesProbeTests
{
    private static MetricSeries Series(double[] xs, double[] ys, string? label = null) =>
        new(CoreMetricCatalog.CoreById["fps"], xs, ys, label);

    private static readonly MetricSeries Base = Series(
        [0.0, 1.0, 2.0],
        [100.0, 120.0, 90.0]);

    private static readonly MetricSeries Comparison = Series(
        [10.0, 11.0, 12.0],
        [50.0, 60.0, 55.0],
        "Comparison");

    private const double Tolerance = 0.5;

    [Fact]
    public void Both_series_qualify_when_both_are_near_the_cursor()
    {
        var overlapping = Series([0.0, 1.0, 2.0], [40.0, 42.0, 44.0], "Comparison");

        var hits = SeriesProbe.Select([Base, overlapping], cursorX: 1.0, Tolerance);
        var anchor = SeriesProbe.Anchor(hits);

        Assert.Equal(2, hits.Count);
        Assert.Equal(Base, hits[0].Series);
        Assert.Equal("Comparison", hits[1].Series.Label);
        Assert.NotNull(anchor);
        Assert.Equal(1.0, anchor.Value.X, precision: 9);
        Assert.Equal(120.0, anchor.Value.Y, precision: 9);
    }

    [Fact]
    public void Base_only_region_returns_only_base()
    {
        var hits = SeriesProbe.Select([Base, Comparison], cursorX: 1.0, Tolerance);

        Assert.Single(hits);
        Assert.Equal(Base, hits[0].Series);
        Assert.Equal(1, hits[0].Index);
    }

    [Fact]
    public void Comparison_only_region_returns_only_comparison()
    {
        var hits = SeriesProbe.Select([Base, Comparison], cursorX: 11.4, Tolerance);

        Assert.Single(hits);
        Assert.Equal("Comparison", hits[0].Series.Label);
        Assert.Equal(11.0, hits[0].X, precision: 9);
        Assert.Equal(60.0, hits[0].Y, precision: 9);
    }

    [Fact]
    public void Neither_series_near_the_cursor_returns_nothing()
    {
        var hits = SeriesProbe.Select([Base, Comparison], cursorX: 6.0, Tolerance);

        Assert.Empty(hits);
        Assert.Null(SeriesProbe.Anchor(hits));
    }

    [Fact]
    public void Nearest_real_samples_are_used_without_interpolation()
    {
        var series = Series([0.0, 2.0], [100.0, 200.0]);

        var hits = SeriesProbe.Select([series], cursorX: 0.6, tolerance: 0.7);

        Assert.Single(hits);
        Assert.Equal(0.0, hits[0].X, precision: 9);
        Assert.Equal(100.0, hits[0].Y, precision: 9);
    }

    [Fact]
    public void Samples_outside_the_tolerance_do_not_qualify()
    {
        var series = Series([0.0, 2.0], [100.0, 200.0]);

        Assert.Empty(SeriesProbe.Select([series], cursorX: 0.9, tolerance: 0.5));
    }

    [Fact]
    public void Empty_series_are_skipped()
    {
        var empty = new MetricSeries(CoreMetricCatalog.CoreById["fps"], [], []);

        var hits = SeriesProbe.Select([empty, Comparison], cursorX: 11.5, Tolerance);

        Assert.Single(hits);
        Assert.Equal("Comparison", hits[0].Series.Label);
    }

    [Fact]
    public void Anchor_is_the_first_qualifying_series_in_plot_order()
    {
        var hits = SeriesProbe.Select([Base, Comparison], cursorX: 11.0, Tolerance);

        // Base does not qualify here, so the comparison hit anchors.
        Assert.Single(hits);
        Assert.NotNull(SeriesProbe.Anchor(hits));
        Assert.Equal("Comparison", SeriesProbe.Anchor(hits)!.Value.Series.Label);
    }
}
