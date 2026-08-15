using System.Globalization;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Infrastructure.Csv;

/// <summary>One summary-table column: its header and whether its cells are numeric.</summary>
public sealed record SummaryColumn(string Header, bool Numeric);

/// <summary>
/// Read-only model of a FrameView_Summary.csv table: preferred column order,
/// remaining columns in original order, numeric formatting, and numeric-aware
/// sorting — mirroring the Python reference summary view. Never mutates the
/// source capture.
/// </summary>
public sealed class SummaryTableDocument
{
    public required IReadOnlyList<SummaryColumn> Columns { get; init; }

    public required string[][] Rows { get; init; }

    public int RowCount => Rows.Length;

    public int ColumnCount => Columns.Count;

    /// <summary>Formatted cell text.</summary>
    public string Cell(int row, int column) => Rows[row][column];
}

/// <summary>Builds and sorts summary-table documents.</summary>
public static class SummaryTable
{
    public static readonly string[] PreferredColumns =
    [
        "Log Name",
        "Application",
        "Resolution",
        "Avg FPS",
        "1% Low FPS",
        "0.1% Low FPS",
        "AvgPCLatency (ms)",
        "Average PC Latency(MSec)",
    ];

    /// <summary>
    /// Builds the read-only summary document from a parsed capture. Column
    /// order follows the reference priority list, then the remaining
    /// non-empty columns in original order.
    /// </summary>
    public static SummaryTableDocument Build(CaptureData capture)
    {
        var columnOrder = new List<int>();
        foreach (var preferred in PreferredColumns)
        {
            var index = capture.IndexOfHeader(preferred);
            if (index >= 0 && !IsEmptyColumn(capture, index))
            {
                columnOrder.Add(index);
            }
        }

        for (var i = 0; i < capture.Headers.Count; i++)
        {
            if (!columnOrder.Contains(i) && !IsEmptyColumn(capture, i))
            {
                columnOrder.Add(i);
            }
        }

        var columns = new List<SummaryColumn>(columnOrder.Count);
        var rows = new string[capture.RowCount][];
        for (var row = 0; row < capture.RowCount; row++)
        {
            rows[row] = new string[columnOrder.Count];
        }

        for (var slot = 0; slot < columnOrder.Count; slot++)
        {
            var index = columnOrder[slot];
            var numeric = IsNumericColumn(capture, index);
            columns.Add(new SummaryColumn(capture.Headers[index], numeric));
            for (var row = 0; row < capture.RowCount; row++)
            {
                rows[row][slot] = FormatCell(capture.Cell(index, row), numeric);
            }
        }

        return new SummaryTableDocument { Columns = columns, Rows = rows };
    }

    /// <summary>
    /// Numeric-aware, stable sort by one column. Empty cells always sort
    /// last; text compares case-insensitively; numbers compare numerically.
    /// </summary>
    public static SummaryTableDocument Sort(
        SummaryTableDocument document,
        int columnIndex,
        bool ascending)
    {
        if (columnIndex < 0 || columnIndex >= document.ColumnCount)
        {
            return document;
        }

        var numeric = document.Columns[columnIndex].Numeric;
        var indexed = document.Rows
            .Select((row, index) => (Row: row, Index: index))
            .ToList();

        indexed.Sort((a, b) =>
        {
            var aCell = a.Row[columnIndex];
            var bCell = b.Row[columnIndex];
            if (aCell.Length == 0 != (bCell.Length == 0))
            {
                return aCell.Length == 0 ? 1 : -1;
            }

            var compared = numeric
                ? CompareNumeric(aCell, bCell)
                : string.Compare(aCell, bCell, StringComparison.OrdinalIgnoreCase);
            if (compared != 0)
            {
                return ascending ? compared : -compared;
            }

            // Stability: fall back to the original row order.
            return a.Index.CompareTo(b.Index);
        });

        return new SummaryTableDocument
        {
            Columns = document.Columns,
            Rows = indexed.Select(pair => pair.Row).ToArray(),
        };
    }

    /// <summary>Formats a raw cell: numbers become ints or trimmed decimals; text is kept as-is.</summary>
    public static string FormatCell(string raw, bool numeric)
    {
        if (CsvValues.IsNa(raw))
        {
            return string.Empty;
        }

        if (numeric && double.TryParse(
                raw.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return number.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return raw;
    }

    private static bool IsEmptyColumn(CaptureData capture, int columnIndex) =>
        capture.Columns[columnIndex].All(CsvValues.IsNa);

    private static bool IsNumericColumn(CaptureData capture, int columnIndex)
    {
        var column = capture.Columns[columnIndex];
        var any = false;
        foreach (var raw in column)
        {
            if (CsvValues.IsNa(raw))
            {
                continue;
            }

            any = true;
            if (!double.TryParse(
                    raw.Replace(',', '.'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                || !double.IsFinite(value))
            {
                return false;
            }
        }

        return any;
    }

    private static int CompareNumeric(string a, string b)
    {
        var aNumber = double.TryParse(
            a,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var aValue)
            ? aValue
            : double.NaN;
        var bNumber = double.TryParse(
            b,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var bValue)
            ? bValue
            : double.NaN;
        return aNumber.CompareTo(bNumber);
    }
}
