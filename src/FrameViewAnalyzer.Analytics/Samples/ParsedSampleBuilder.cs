using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Analytics.Samples;

/// <summary>
/// Builds sorted per-frame samples from a capture. Rows without a valid
/// time are skipped; frametime falls back to the FPS column and GPU
/// utilization resolves through the reference column aliases.
/// </summary>
public static class ParsedSampleBuilder
{
    private static readonly string[] GpuUtilKeys =
    [
        "GPU0Util(%)",
        "GPU1Util(%)",
        "GPU1 Utilization(%)",
        "GPU Utilization(%)",
        "GPU0 Util%",
    ];

    public static ParsedSamples Build(CaptureData capture)
    {
        var timeIndex = -1;
        foreach (var key in CoreMetricCatalog.TimeColumnKeys)
        {
            timeIndex = capture.IndexOfHeader(key);
            if (timeIndex >= 0)
            {
                break;
            }
        }

        if (timeIndex < 0)
        {
            return Empty();
        }

        var frametimeIndices = ResolveIndices(capture, "MsBetweenPresents", "MsBetweenDisplayChange");
        var fpsColumnIndex = capture.IndexOfHeader("FPS");
        var gpuIndices = ResolveIndices(capture, GpuUtilKeys);

        var capacity = capture.RowCount;
        var times = new List<double>(capacity);
        var frametimes = new List<double>(capacity);
        var fpsValues = new List<double>(capacity);
        var utils = new List<double>(capacity);
        var rowIndices = new List<int>(capacity);

        for (var row = 0; row < capture.RowCount; row++)
        {
            if (!CsvValues.TryParseNumber(capture.Cell(timeIndex, row), out var time))
            {
                continue;
            }

            var frameTime = ParseFrameTime(capture, frametimeIndices, fpsColumnIndex, row);
            var fps = frameTime is > 0 ? 1000.0 / frameTime.Value : double.NaN;

            times.Add(time);
            frametimes.Add(frameTime ?? double.NaN);
            fpsValues.Add(fps);
            utils.Add(ParseFirst(capture, gpuIndices, row) ?? double.NaN);
            rowIndices.Add(row);
        }

        // FrameView logs are written with ascending timestamps; when the
        // input is already ordered, the stable sort is the identity (the
        // Python reference's sorted() produces the exact same result) and
        // can be skipped entirely. Unordered input keeps the LINQ stable
        // sort for exact parity.
        if (IsAscending(times))
        {
            return new ParsedSamples
            {
                TimeSeconds = times.ToArray(),
                FrametimeMs = frametimes.ToArray(),
                Fps = fpsValues.ToArray(),
                GpuUtilPercent = utils.ToArray(),
                RowIndex = rowIndices.ToArray(),
            };
        }

        var order = Enumerable.Range(0, times.Count)
            .OrderBy(i => times[i])
            .ToArray();

        return new ParsedSamples
        {
            TimeSeconds = Select(times, order),
            FrametimeMs = Select(frametimes, order),
            Fps = Select(fpsValues, order),
            GpuUtilPercent = Select(utils, order),
            RowIndex = Select(rowIndices, order),
        };
    }

    private static bool IsAscending(List<double> values)
    {
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] < values[i - 1])
            {
                return false;
            }
        }

        return true;
    }

    private static int[] ResolveIndices(CaptureData capture, params string[] headers)
    {
        var indices = new List<int>();
        foreach (var header in headers)
        {
            var index = capture.IndexOfHeader(header);
            if (index >= 0)
            {
                indices.Add(index);
            }
        }

        return indices.ToArray();
    }

    private static double? ParseFrameTime(
        CaptureData capture,
        int[] frametimeIndices,
        int fpsColumnIndex,
        int row)
    {
        var frameTime = ParseFirstPositive(capture, frametimeIndices, row);
        if (frameTime is not null)
        {
            return frameTime;
        }

        if (fpsColumnIndex < 0)
        {
            return null;
        }

        var fps = ParseCell(capture, fpsColumnIndex, row);
        return fps is > 0 ? 1000.0 / fps : null;
    }

    private static double? ParseFirstPositive(CaptureData capture, int[] indices, int row)
    {
        foreach (var index in indices)
        {
            var value = ParseCell(capture, index, row);
            if (value is > 0)
            {
                return value;
            }
        }

        return null;
    }

    private static double? ParseFirst(CaptureData capture, int[] indices, int row)
    {
        foreach (var index in indices)
        {
            var value = ParseCell(capture, index, row);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static double? ParseCell(CaptureData capture, int index, int row) =>
        CsvValues.TryParseNumber(capture.Cell(index, row), out var value)
            ? value
            : null;

    private static double[] Select(List<double> values, int[] order)
    {
        var result = new double[values.Count];
        for (var i = 0; i < order.Length; i++)
        {
            result[i] = values[order[i]];
        }

        return result;
    }

    private static int[] Select(List<int> values, int[] order)
    {
        var result = new int[values.Count];
        for (var i = 0; i < order.Length; i++)
        {
            result[i] = values[order[i]];
        }

        return result;
    }

    private static ParsedSamples Empty() => new()
    {
        TimeSeconds = [],
        FrametimeMs = [],
        Fps = [],
        GpuUtilPercent = [],
        RowIndex = [],
    };
}
