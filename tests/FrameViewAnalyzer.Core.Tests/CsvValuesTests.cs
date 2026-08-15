using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Core.Tests;

public class CsvValuesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("NA")]
    [InlineData("N/A")]
    [InlineData("n/a")]
    [InlineData("null")]
    [InlineData("NULL")]
    public void IsNa_matches_the_python_set(string value) => Assert.True(CsvValues.IsNa(value));

    [Theory]
    [InlineData("60")]
    [InlineData("na")]
    [InlineData("Null")]
    [InlineData("--")]
    public void IsNa_rejects_non_members(string value) => Assert.False(CsvValues.IsNa(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NA")]
    [InlineData("na")]
    [InlineData("N/A")]
    [InlineData("NULL")]
    [InlineData("null")]
    public void IsMissing_accepts_the_lenient_superset(string? value) => Assert.True(CsvValues.IsMissing(value));

    [Theory]
    [InlineData("60")]
    [InlineData("Off")]
    public void IsMissing_rejects_real_values(string value) => Assert.False(CsvValues.IsMissing(value));

    [Fact]
    public void TryParseNumber_accepts_decimal_comma()
    {
        Assert.True(CsvValues.TryParseNumber("12,5", out var value));
        Assert.Equal(12.5, value);
    }

    [Theory]
    [InlineData("nan")]
    [InlineData("inf")]
    [InlineData("-inf")]
    [InlineData("NA")]
    [InlineData("")]
    [InlineData("abc")]
    public void TryParseNumber_rejects_non_numeric_values(string raw)
    {
        Assert.False(CsvValues.TryParseNumber(raw, out _));
    }

    [Fact]
    public void TryParseNumber_accepts_invariant_decimals()
    {
        Assert.True(CsvValues.TryParseNumber("1.5", out var value));
        Assert.Equal(1.5, value);
    }

    [Theory]
    [InlineData(" 60 ")]
    [InlineData("60 ")]
    [InlineData(" 60")]
    [InlineData("1e3")]
    [InlineData("-2.5E-2")]
    [InlineData("+4.25")]
    public void TryParseNumber_accepts_padded_signed_and_exponent_forms(string raw)
    {
        Assert.True(CsvValues.TryParseNumber(raw, out _));
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("+INF")]
    [InlineData("NaN")]
    [InlineData("-infinity")]
    public void TryParseNumber_rejects_inf_and_nan_tokens_under_any_casing(string raw)
    {
        Assert.False(CsvValues.TryParseNumber(raw, out _));
    }

    [Fact]
    public void TryParseAnyNumber_accepts_inf_and_nan_tokens_like_python_float()
    {
        Assert.True(CsvValues.TryParseAnyNumber("inf", out var inf));
        Assert.True(double.IsPositiveInfinity(inf));
        Assert.True(CsvValues.TryParseAnyNumber("+Infinity", out var plusInf));
        Assert.True(double.IsPositiveInfinity(plusInf));
        Assert.True(CsvValues.TryParseAnyNumber("-inf", out var negInf));
        Assert.True(double.IsNegativeInfinity(negInf));
        Assert.True(CsvValues.TryParseAnyNumber("nan", out var nan));
        Assert.True(double.IsNaN(nan));
    }
}
