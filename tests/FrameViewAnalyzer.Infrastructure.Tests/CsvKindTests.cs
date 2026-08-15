using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class CsvKindTests
{
    [Fact]
    public void Kind_values_are_defined()
    {
        Assert.Equal(0, (int)CsvKind.Unknown);
        Assert.Equal(1, (int)CsvKind.Log);
        Assert.Equal(2, (int)CsvKind.Summary);
    }
}
