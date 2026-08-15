namespace FrameViewAnalyzer.Analytics.Tests;

public class AnalysisOptionsTests
{
    [Fact]
    public void Defaults_match_the_python_reference()
    {
        var options = new AnalysisOptions();
        Assert.Equal(10.0, options.GpuThreshold);
        Assert.Equal(1.0, options.TrimBufferSeconds);
        Assert.True(options.AutoGpuThreshold);
        Assert.True(options.ExcludeTransitions);
    }

    [Fact]
    public void With_expressions_preserve_untouched_members()
    {
        var options = new AnalysisOptions { GpuThreshold = 25.0 };
        var updated = options with { GpuThreshold = 30.0 };
        Assert.Equal(30.0, updated.GpuThreshold);
        Assert.Equal(options.TrimBufferSeconds, updated.TrimBufferSeconds);
    }
}
