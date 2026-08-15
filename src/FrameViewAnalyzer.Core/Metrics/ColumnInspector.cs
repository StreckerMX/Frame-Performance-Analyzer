using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Core.Metrics;

/// <summary>
/// Column-level numeric detection over a CaptureData. Unparseable samples
/// disqualify a column; NA and non-finite samples are skipped, mirroring the
/// Python reference.
/// </summary>
public static class ColumnInspector
{
    public static bool IsNumericColumn(CaptureData capture, int columnIndex, int sampleSize = 200)
    {
        if ((uint)columnIndex >= (uint)capture.Columns.Length)
        {
            return false;
        }

        var column = capture.Columns[columnIndex];
        var limit = System.Math.Min(sampleSize, column.Length);
        var checkedRows = 0;
        var numericHits = 0;

        for (var i = 0; i < limit; i++)
        {
            var raw = column[i];
            if (CsvValues.IsNa(raw))
            {
                continue;
            }

            if (!CsvValues.TryParseAnyNumber(raw, out var value))
            {
                return false;
            }

            if (!double.IsFinite(value))
            {
                continue;
            }

            checkedRows++;
            numericHits++;
        }

        return checkedRows > 0 && numericHits == checkedRows;
    }
}
