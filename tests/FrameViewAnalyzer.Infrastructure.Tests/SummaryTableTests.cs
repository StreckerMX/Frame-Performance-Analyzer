using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class SummaryTableTests
{
    [Fact]
    public void Preferred_columns_are_ordered_first()
    {
        var capture = CaptureWith(
            ["Extra One", "Resolution", "Log Name", "Avg FPS", "Extra Two"],
            [["x", "4K", "A", "144.33300", "y"]]);

        var document = SummaryTable.Build(capture);

        Assert.Equal(["Log Name", "Resolution", "Avg FPS", "Extra One", "Extra Two"], document.Columns.Select(c => c.Header));
    }

    [Fact]
    public void Empty_columns_are_dropped()
    {
        var capture = CaptureWith(
            ["Log Name", "Resolution", "Empty"],
            [["A", "4K", ""], ["B", "1080p", "NA"]]);

        var document = SummaryTable.Build(capture);

        Assert.Equal(["Log Name", "Resolution"], document.Columns.Select(c => c.Header));
    }

    [Fact]
    public void Numeric_cells_are_formatted_like_the_reference()
    {
        var capture = CaptureWith(
            ["Log Name", "Avg FPS", "0.1% Low FPS"],
            [["A", "144.33300", "61.000"], ["B", "120.0", "0.5"]]);

        var document = SummaryTable.Build(capture);

        Assert.Equal("144.333", document.Cell(0, 1));
        Assert.Equal("61", document.Cell(0, 2));
        Assert.Equal("120", document.Cell(1, 1));
        Assert.Equal("0.5", document.Cell(1, 2));
        Assert.True(document.Columns[1].Numeric);
    }

    [Fact]
    public void Numeric_sort_orders_values_numerically()
    {
        var capture = CaptureWith(
            ["Log Name", "Avg FPS"],
            [["A", "9"], ["B", "90"], ["C", "100"]]);
        var document = SummaryTable.Build(capture);

        var ascending = SummaryTable.Sort(document, 1, ascending: true);

        Assert.Equal(["A", "B", "C"], ascending.Rows.Select(row => row[0]));
    }

    [Fact]
    public void Sort_keeps_empty_cells_last_and_is_stable()
    {
        var capture = CaptureWith(
            ["Log Name", "Avg FPS"],
            [["A", ""], ["B", "50"], ["C", ""], ["D", "20"]]);
        var document = SummaryTable.Build(capture);

        var ascending = SummaryTable.Sort(document, 1, ascending: true);

        Assert.Equal(["D", "B", "A", "C"], ascending.Rows.Select(row => row[0]));
    }

    [Fact]
    public void Unicode_text_is_preserved()
    {
        var capture = CaptureWith(
            ["Log Name"],
            [["Ünïcode · 游戏 · Café"]]);

        var document = SummaryTable.Build(capture);

        Assert.Equal("Ünïcode · 游戏 · Café", document.Cell(0, 0));
    }

    private static CaptureData CaptureWith(string[] headers, string[][] rows)
    {
        var columns = new string[headers.Length][];
        for (var i = 0; i < headers.Length; i++)
        {
            columns[i] = new string[rows.Length];
            for (var r = 0; r < rows.Length; r++)
            {
                columns[i][r] = rows[r][i];
            }
        }

        return new CaptureData
        {
            Path = "summary.csv",
            DisplayName = "summary",
            Kind = CsvKind.Summary,
            Headers = headers,
            Columns = columns,
        };
    }
}
