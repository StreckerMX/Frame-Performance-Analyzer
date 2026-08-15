using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.Analytics.Tests;

public class VisibleRangeCalculatorTests
{
    private static readonly MetricDefinition Fps = CoreMetricCatalog.CoreById["fps"];

    private static readonly double[] Xs = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
    private static readonly double[] Ys = [60, 61, 62, 63, 64, 65, 66, 67, 68, 69];

    [Fact]
    public void Filter_values_uses_inclusive_bounds()
    {
        var values = VisibleRangeCalculator.FilterValues(Xs, Ys, 2.0, 4.0);

        Assert.Equal([62, 63, 64], values);
    }

    [Fact]
    public void Out_of_range_slices_are_empty()
    {
        Assert.Empty(VisibleRangeCalculator.FilterValues(Xs, Ys, 20.0, 30.0));
    }

    [Fact]
    public void Compute_returns_statistics_for_the_visible_slice()
    {
        var (stats, count) = VisibleRangeCalculator.Compute(Fps, Xs, Ys, 3.0, 7.0);

        Assert.Equal(5, count);
        Assert.Equal(65.0, stats.Avg);
        Assert.Equal(63.0, stats.Min);
        Assert.Equal(67.0, stats.Max);
    }

    [Fact]
    public void Empty_slices_yield_null_statistics()
    {
        var (stats, count) = VisibleRangeCalculator.Compute(Fps, Xs, Ys, 100.0, 200.0);

        Assert.Equal(0, count);
        Assert.Null(stats.Avg);
        Assert.Null(stats.P1);
    }
}
