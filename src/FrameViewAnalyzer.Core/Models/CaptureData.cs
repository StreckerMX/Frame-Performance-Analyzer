namespace FrameViewAnalyzer.Core.Models;

/// <summary>
/// A loaded FrameView CSV in column-major form. Raw cells are kept as
/// trimmed strings (empty for missing fields); numeric conversion happens
/// lazily per column in the analytics layer, never here.
/// </summary>
public sealed class CaptureData
{
    public required string Path { get; init; }
    public required string DisplayName { get; init; }
    public required CsvKind Kind { get; init; }
    public required IReadOnlyList<string> Headers { get; init; }
    public required string[][] Columns { get; init; }

    public int RowCount => Columns.Length == 0 ? 0 : Columns[0].Length;

    /// <summary>First header index matching <paramref name="header"/> (ordinal).</summary>
    public int IndexOfHeader(string header)
    {
        for (var i = 0; i < Headers.Count; i++)
        {
            if (string.Equals(Headers[i], header, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public bool ContainsHeader(string header) => IndexOfHeader(header) >= 0;

    public string Cell(int columnIndex, int rowIndex)
    {
        if ((uint)columnIndex >= (uint)Columns.Length)
        {
            return string.Empty;
        }

        var column = Columns[columnIndex];
        return (uint)rowIndex < (uint)column.Length ? column[rowIndex] : string.Empty;
    }
}
