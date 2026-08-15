using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Core.Metrics;

/// <summary>
/// Resolves the value of one metric for one capture row, mirroring the
/// Python reference: FPS is derived from frame time, frame time falls back
/// to the FPS column, and every other metric parses its resolved column.
/// </summary>
public static class MetricValueResolver
{
    public static double? GetMetricValue(
        CaptureData capture,
        MetricDefinition metric,
        int rowIndex)
    {
        switch (metric.Id)
        {
            case "fps":
            {
                var frameTime = GetMetricValue(capture, CoreMetricCatalog.CoreById["frametime"], rowIndex);
                return frameTime is > 0 ? 1000.0 / frameTime : null;
            }

            case "frametime":
            {
                var frameTime = ParseFirstPositive(capture, metric.ColumnKeys, rowIndex);
                if (frameTime is not null)
                {
                    return frameTime;
                }

                var fpsColumn = capture.IndexOfHeader("FPS");
                if (fpsColumn < 0)
                {
                    return null;
                }

                var fps = ParseCell(capture, fpsColumn, rowIndex);
                return fps is > 0 ? 1000.0 / fps : null;
            }

            default:
            {
                var column = metric.ResolveColumn(capture.Headers);
                return column is null
                    ? null
                    : ParseCell(capture, capture.IndexOfHeader(column), rowIndex);
            }
        }
    }

    private static double? ParseFirstPositive(
        CaptureData capture,
        IReadOnlyList<string> columnKeys,
        int rowIndex)
    {
        foreach (var key in columnKeys)
        {
            var index = capture.IndexOfHeader(key);
            if (index < 0)
            {
                continue;
            }

            var value = ParseCell(capture, index, rowIndex);
            if (value is > 0)
            {
                return value;
            }
        }

        return null;
    }

    private static double? ParseCell(CaptureData capture, int columnIndex, int rowIndex) =>
        CsvValues.TryParseNumber(capture.Cell(columnIndex, rowIndex), out var value)
            ? value
            : null;
}
