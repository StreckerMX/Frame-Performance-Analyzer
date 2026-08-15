using System.Globalization;
using System.Text;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Benchmarks;

/// <summary>
/// Deterministic synthetic captures that exercise the same hot paths as
/// real FrameView logs: ascending per-frame timestamps, jittered frametime
/// with occasional spikes, and GPU utilization with loading-screen dips so
/// the filter detector has real work to do.
/// </summary>
public static class BenchmarkData
{
    public const string CsvHeader = "TimeInSeconds,MsBetweenPresents,GPU0Util(%)";

    /// <summary>Writes a synthetic FrameView-style CSV file and returns its path.</summary>
    public static (string Path, string Directory) CreateCsvFile(int rows, int seed)
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "fva-bench-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, $"FrameView_{rows}_{seed}_Log.csv");
        var random = new Random(seed);
        var builder = new StringBuilder(CsvHeader.Length + rows * 32);
        builder.Append(CsvHeader).Append('\n');
        var time = 0.0;
        for (var row = 0; row < rows; row++)
        {
            var frametimeMs = FrametimeMs(random);
            time += frametimeMs / 1000.0;
            builder.Append(time.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                .Append(frametimeMs.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                .Append(GpuUtil(random, row, rows).ToString("0.00", CultureInfo.InvariantCulture))
                .Append('\n');
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return (path, directory);
    }

    /// <summary>Builds an in-memory capture with the same content as the CSV file.</summary>
    public static CaptureData CreateCapture(int rows, int seed)
    {
        var random = new Random(seed);
        var times = new string[rows];
        var frametimes = new string[rows];
        var gpus = new string[rows];
        var time = 0.0;
        for (var row = 0; row < rows; row++)
        {
            var frametimeMs = FrametimeMs(random);
            time += frametimeMs / 1000.0;
            times[row] = time.ToString("0.0000", CultureInfo.InvariantCulture);
            frametimes[row] = frametimeMs.ToString("0.0000", CultureInfo.InvariantCulture);
            gpus[row] = GpuUtil(random, row, rows).ToString("0.00", CultureInfo.InvariantCulture);
        }

        return new CaptureData
        {
            Path = $"C:/synthetic/FrameView_{rows}_{seed}_Log.csv",
            DisplayName = $"FrameView_{rows}_{seed}_Log",
            Kind = CsvKind.Log,
            Headers = ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            Columns = [times, frametimes, gpus],
        };
    }

    /// <summary>Ascending x values spaced like 60 FPS frame timestamps.</summary>
    public static double[] AscendingXs(int count, double step = 1.0 / 60.0)
    {
        var xs = new double[count];
        for (var i = 0; i < count; i++)
        {
            xs[i] = i * step;
        }

        return xs;
    }

    /// <summary>Noisy series with occasional upward spikes (chart-like data).</summary>
    public static double[] NoisySeries(
        Random random,
        int count,
        double baseline,
        double noise,
        double spikeChance)
    {
        var ys = new double[count];
        for (var i = 0; i < count; i++)
        {
            ys[i] = baseline + ((random.NextDouble() * 2.0) - 1.0) * noise;
            if (random.NextDouble() < spikeChance)
            {
                ys[i] += random.NextDouble() * baseline;
            }
        }

        return ys;
    }

    /// <summary>
    /// Frame time in milliseconds (~9–11 ms, like a real 60–120 FPS capture).
    /// Timestamps advance by frametime / 1000 so a capture spans roughly
    /// rows / 100 one-second bins, like real FrameView logs.
    /// </summary>
    private static double FrametimeMs(Random random) => 9.0 + (random.NextDouble() * 2.0);

    private static double GpuUtil(Random random, int row, int rows)
    {
        // Loading-screen dip near the start and a second dip near the middle
        // give FilterProfileDetector a realistic workload.
        var loadingStart = rows * 0.01;
        var loadingEnd = rows * 0.02;
        var dipStart = rows * 0.48;
        var dipEnd = rows * 0.50;
        if ((row >= loadingStart && row < loadingEnd)
            || (row >= dipStart && row < dipEnd))
        {
            return 20.0 + (random.NextDouble() * 30.0);
        }

        return 75.0 + (random.NextDouble() * 25.0);
    }
}
