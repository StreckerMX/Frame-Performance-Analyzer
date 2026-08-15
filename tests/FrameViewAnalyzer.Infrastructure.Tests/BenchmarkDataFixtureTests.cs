using System.Globalization;
using FrameViewAnalyzer.Benchmarks;

namespace FrameViewAnalyzer.Infrastructure.Tests;

/// <summary>
/// Guards the synthetic benchmark fixture against unit bugs. The original
/// Phase 12 fixture generated frametimes of ~9–11 SECONDS instead of
/// milliseconds, silently turning a realistic capture into a ~2,000,000 s
/// workload with one bin per row and contaminating every baseline. These
/// assertions lock the fixture to realistic FrameView-like data.
/// </summary>
public class BenchmarkDataFixtureTests
{
    [Fact]
    public void Synthetic_frametimes_are_in_a_realistic_millisecond_range()
    {
        var (path, directory) = BenchmarkData.CreateCsvFile(rows: 1_000, seed: 42);
        try
        {
            var lines = File.ReadAllLines(path).Skip(1).ToArray();
            Assert.Equal(1_000, lines.Length);
            foreach (var line in lines)
            {
                var frametime = double.Parse(
                    line.Split(',')[1],
                    CultureInfo.InvariantCulture);
                Assert.InRange(frametime, 5.0, 50.0);
            }
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Synthetic_timestamps_are_monotonically_increasing()
    {
        var capture = BenchmarkData.CreateCapture(rows: 10_000, seed: 42);
        var times = capture.Columns[capture.IndexOfHeader("TimeInSeconds")];
        var previous = double.Parse(times[0], CultureInfo.InvariantCulture);
        for (var i = 1; i < times.Length; i++)
        {
            var current = double.Parse(times[i], CultureInfo.InvariantCulture);
            Assert.True(current > previous, $"Timestamp {i} is not increasing.");
            previous = current;
        }
    }

    [Fact]
    public void Large_capture_spans_a_realistic_duration_not_millions_of_seconds()
    {
        var capture = BenchmarkData.CreateCapture(rows: 200_000, seed: 42);
        var times = capture.Columns[capture.IndexOfHeader("TimeInSeconds")];
        var first = double.Parse(times[0], CultureInfo.InvariantCulture);
        var last = double.Parse(times[^1], CultureInfo.InvariantCulture);
        var duration = last - first;

        // 200k frames at ~9–11 ms span roughly 1,800–2,200 s. A seconds-vs-
        // milliseconds unit bug would produce ~2,000,000 s.
        Assert.InRange(duration, 1_500.0, 2_500.0);
    }

    [Fact]
    public void Synthetic_capture_and_csv_share_the_same_shape()
    {
        var (path, directory) = BenchmarkData.CreateCsvFile(rows: 500, seed: 7);
        try
        {
            var capture = BenchmarkData.CreateCapture(rows: 500, seed: 7);

            Assert.Equal(BenchmarkData.CsvHeader.Split(','), capture.Headers);
            Assert.Equal(500, capture.RowCount);
            Assert.Equal(500, File.ReadAllLines(path).Length - 1);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private static void Cleanup(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
