using FrameViewAnalyzer.Core.Math;

namespace FrameViewAnalyzer.Core.Tests;

public class StatisticsTests
{
    [Fact]
    public void Percentile_interpolates_like_the_python_reference()
    {
        Assert.Equal(2.0, Statistics.Percentile([1.0, 2.0, 3.0], 0.5));
        Assert.Equal(2.5, Statistics.Percentile([0.0, 10.0], 0.25));
    }

    [Fact]
    public void Percentile_handles_edge_inputs()
    {
        Assert.Null(Statistics.Percentile([], 0.5));
        Assert.Equal(7.0, Statistics.Percentile([7.0], 0.9));
        Assert.Equal(1.0, Statistics.Percentile([1.0, 2.0, 3.0], 0.0));
        Assert.Equal(3.0, Statistics.Percentile([1.0, 2.0, 3.0], 1.0));
    }

    [Fact]
    public void Mean_is_null_for_empty_values()
    {
        Assert.Null(Statistics.Mean([]));
        Assert.Equal(2.5, Statistics.Mean([1.0, 4.0]));
    }
}
