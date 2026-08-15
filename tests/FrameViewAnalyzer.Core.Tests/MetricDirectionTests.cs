using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Core.Tests;

public class MetricDirectionTests
{
    [Fact]
    public void Direction_values_are_defined()
    {
        Assert.Equal(0, (int)MetricDirection.Undefined);
        Assert.Equal(1, (int)MetricDirection.HigherIsBetter);
        Assert.Equal(2, (int)MetricDirection.LowerIsBetter);
    }

    [Fact]
    public void ChartPoint_is_a_value_type()
    {
        var point = new ChartPoint(1.5, 60.0);
        var copy = point with { Y = 61.0 };
        Assert.Equal(1.5, copy.X);
        Assert.Equal(61.0, copy.Y);
        Assert.Equal(60.0, point.Y);
    }
}
