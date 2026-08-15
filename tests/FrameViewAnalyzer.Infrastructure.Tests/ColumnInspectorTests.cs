using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class ColumnInspectorTests
{
    private static CaptureData CaptureWith(params string[] column) => new()
    {
        Path = "capture.csv",
        DisplayName = "capture",
        Kind = CsvKind.Log,
        Headers = ["value"],
        Columns = [column],
    };

    [Fact]
    public void Non_finite_samples_are_ignored()
    {
        Assert.True(ColumnInspector.IsNumericColumn(CaptureWith("1.0", "inf", "2.0"), 0));
    }

    [Fact]
    public void Only_non_finite_samples_disqualify_the_column()
    {
        Assert.False(ColumnInspector.IsNumericColumn(CaptureWith("inf"), 0));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("NA")]
    [InlineData("")]
    public void Non_numeric_or_na_only_samples_disqualify_the_column(string value)
    {
        Assert.False(ColumnInspector.IsNumericColumn(CaptureWith(value), 0));
    }

    [Fact]
    public void Decimal_commas_count_as_numeric()
    {
        Assert.True(ColumnInspector.IsNumericColumn(CaptureWith("12,5", "3.0"), 0));
    }

    [Fact]
    public void Out_of_range_column_is_not_numeric()
    {
        Assert.False(ColumnInspector.IsNumericColumn(CaptureWith("1.0"), 1));
    }
}
