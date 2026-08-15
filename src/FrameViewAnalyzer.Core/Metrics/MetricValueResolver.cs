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

    /// <summary>
    /// Column indices resolved once per capture/metric pair. Reusing this
    /// in hot loops avoids per-row column resolution and header scans.
    /// </summary>
    public readonly record struct MetricColumns(
        int[] FrametimeKeyIndices,
        int FpsColumnIndex,
        int ColumnIndex)
    {
        public static MetricColumns Resolve(CaptureData capture, MetricDefinition metric)
        {
            if (metric.Id is "fps" or "frametime")
            {
                // The fps metric derives from frame time exactly like the
                // frametime metric does in GetMetricValue.
                var keys = metric.Id == "fps"
                    ? CoreMetricCatalog.CoreById["frametime"].ColumnKeys
                    : metric.ColumnKeys;
                var indices = new List<int>(keys.Count);
                foreach (var key in keys)
                {
                    var index = capture.IndexOfHeader(key);
                    if (index >= 0)
                    {
                        indices.Add(index);
                    }
                }

                return new MetricColumns(indices.ToArray(), capture.IndexOfHeader("FPS"), -1);
            }

            var column = metric.ResolveColumn(capture.Headers);
            return new MetricColumns([], -1, column is null ? -1 : capture.IndexOfHeader(column));
        }
    }

    /// <summary>
    /// Resolves one metric value for one row using precomputed column
    /// indices; identical results to the per-row overload.
    /// </summary>
    public static double? GetMetricValue(
        CaptureData capture,
        MetricDefinition metric,
        int rowIndex,
        in MetricColumns columns)
    {
        if (metric.Id is "fps" or "frametime")
        {
            // fps derives from frame time exactly like the per-row overload:
            // first positive frame-time column, then the FPS fallback, and
            // finally the fps metric converts back with 1000 / frame_time.
            double? frameTime = null;
            foreach (var index in columns.FrametimeKeyIndices)
            {
                var value = ParseCell(capture, index, rowIndex);
                if (value is > 0)
                {
                    frameTime = value;
                    break;
                }
            }

            if (frameTime is null && columns.FpsColumnIndex >= 0)
            {
                var fps = ParseCell(capture, columns.FpsColumnIndex, rowIndex);
                frameTime = fps is > 0 ? 1000.0 / fps : null;
            }

            return frameTime is null
                ? null
                : metric.Id == "fps" ? 1000.0 / frameTime : frameTime;
        }

        return columns.ColumnIndex >= 0
            ? ParseCell(capture, columns.ColumnIndex, rowIndex)
            : null;
    }
}
