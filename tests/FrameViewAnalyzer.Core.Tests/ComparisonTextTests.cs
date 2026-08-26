using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Core.Tests;

public class ComparisonTextTests
{
    [Fact]
    public void Improvement_with_positive_value_change_uses_up_arrow()
    {
        var text = ComparisonText.FormatDelta(10.0, 12.5, ImprovementKind.Improvement);

        Assert.Equal("↑ 12.5%", text);
    }

    [Fact]
    public void Improvement_with_negative_value_change_uses_down_arrow()
    {
        var text = ComparisonText.FormatDelta(-2.0, -14.0, ImprovementKind.Improvement);

        Assert.Equal("↓ 14.0%", text);
    }

    [Fact]
    public void Regression_with_positive_value_change_uses_up_arrow()
    {
        var text = ComparisonText.FormatDelta(2.0, 14.0, ImprovementKind.Regression);

        Assert.Equal("↑ 14.0%", text);
    }

    [Fact]
    public void Regression_with_negative_value_change_uses_down_arrow()
    {
        var text = ComparisonText.FormatDelta(-10.0, -12.5, ImprovementKind.Regression);

        Assert.Equal("↓ 12.5%", text);
    }

    [Fact]
    public void Neutral_delta_keeps_signed_text_without_direction_arrow()
    {
        var text = ComparisonText.FormatDelta(-2.0, -14.0, ImprovementKind.None);

        Assert.Equal("-14.0%", text);
    }
}
