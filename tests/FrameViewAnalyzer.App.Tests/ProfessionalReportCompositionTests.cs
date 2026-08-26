using FrameViewAnalyzer.App.Charting;

namespace FrameViewAnalyzer.App.Tests;

public class ProfessionalReportCompositionTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(6, 3)]
    [InlineData(7, 4)]
    [InlineData(8, 4)]
    public void Benchmark_cards_use_readable_adaptive_columns(int runs, int expectedColumns)
    {
        Assert.Equal(expectedColumns, ReportPlotBuilder.ReportRunColumns(runs));
    }

    [Theory]
    [InlineData("Base — NV APP OPTIMIZED", "NV APP OPTIMIZED")]
    [InlineData("Comparison — NV APP OPT NO BKG", "NV APP OPT NO BKG")]
    [InlineData("Base - Run A", "Run A")]
    [InlineData("FG, SR, RR & LLM ULTRA", "FG, SR, RR & LLM ULTRA")]
    public void Pair_role_prefixes_are_not_repeated_inside_run_cards(string raw, string expected)
    {
        Assert.Equal(expected, ReportPlotBuilder.ReportRunDisplayLabel(raw));
    }
}
