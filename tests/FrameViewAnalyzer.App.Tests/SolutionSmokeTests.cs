namespace FrameViewAnalyzer.App.Tests;

public class SolutionSmokeTests
{
    [Fact]
    public void App_assembly_is_referenced()
    {
        var name = typeof(FrameViewAnalyzer.App.App).Assembly.GetName().Name;
        Assert.Equal("FrameViewAnalyzer.App", name);
    }
}
