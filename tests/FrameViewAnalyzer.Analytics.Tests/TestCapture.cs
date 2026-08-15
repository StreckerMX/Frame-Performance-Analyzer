using System.Globalization;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

/// <summary>Column-major capture fixtures mirroring the Python test captures.</summary>
internal static class TestCapture
{
    public static CaptureData MakeSession(int seconds = 10, bool withMetadata = false)
    {
        var headers = new List<string> { "TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)" };
        if (withMetadata)
        {
            headers.AddRange(["Application", "GPU", "CPU", "Resolution", "Runtime"]);
        }

        var rowCount = seconds * 3;
        var columns = new string[headers.Count][];
        for (var i = 0; i < headers.Count; i++)
        {
            columns[i] = new string[rowCount];
        }

        var offsets = new[] { 0.0, 0.25, 0.5 };
        for (var row = 0; row < rowCount; row++)
        {
            var second = row / 3;
            var time = (second + offsets[row % 3]).ToString(CultureInfo.InvariantCulture);
            columns[0][row] = time;
            columns[1][row] = "10.0";
            columns[2][row] = "80.0";
            if (withMetadata)
            {
                columns[3][row] = "Game.exe";
                columns[4][row] = "Example GPU";
                columns[5][row] = "Example CPU";
                columns[6][row] = "3840x2160";
                columns[7][row] = "DXGI";
            }
        }

        return new CaptureData
        {
            Path = "synthetic.csv",
            DisplayName = "synthetic",
            Kind = CsvKind.Log,
            Headers = headers,
            Columns = columns,
        };
    }

    /// <summary>
    /// Multi-scene capture with four high-FPS transition seconds (4-7) in
    /// the middle, mirroring the Python fixture exactly.
    /// </summary>
    public static CaptureData MakeMultiScene()
    {
        var headers = new[] { "TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)" };
        var rows = new List<string[]>();
        for (var second = 0; second < 13; second++)
        {
            var isTransition = second is 4 or 5 or 6 or 7;
            var fps = isTransition ? 1000.0 : 100.0 + second;
            var gpu = second == 4 || !isTransition ? 90.0 : 20.0;
            var frameTime = 1000.0 / fps;
            var frameCount = Math.Max(3, (int)Math.Round(fps));
            for (var frame = 0; frame < frameCount; frame++)
            {
                var time = (second + (double)frame / frameCount).ToString(CultureInfo.InvariantCulture);
                rows.Add(
                [
                    time,
                    frameTime.ToString(CultureInfo.InvariantCulture),
                    gpu.ToString(CultureInfo.InvariantCulture),
                ]);
            }
        }

        return CaptureWith(headers, rows);
    }

    /// <summary>Capture with every bin below the GPU threshold.</summary>
    public static CaptureData MakeLowGpu(int seconds = 6, double util = 5.0)
    {
        var headers = new[] { "TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)" };
        var rows = new List<string[]>();
        foreach (var offset in new[] { 0.0, 0.25, 0.5 })
        {
            for (var second = 0; second < seconds; second++)
            {
                rows.Add(
                [
                    (second + offset).ToString(CultureInfo.InvariantCulture),
                    "10.0",
                    util.ToString(CultureInfo.InvariantCulture),
                ]);
            }
        }

        return CaptureWith(headers, rows);
    }

    public static CaptureData CaptureWith(
        IReadOnlyList<string> headers,
        IReadOnlyList<string[]> rows,
        CsvKind kind = CsvKind.Log)
    {
        var columns = new string[headers.Count][];
        for (var i = 0; i < headers.Count; i++)
        {
            columns[i] = new string[rows.Count];
            for (var r = 0; r < rows.Count; r++)
            {
                columns[i][r] = rows[r][i];
            }
        }

        return new CaptureData
        {
            Path = "capture.csv",
            DisplayName = "capture",
            Kind = kind,
            Headers = headers,
            Columns = columns,
        };
    }
}
