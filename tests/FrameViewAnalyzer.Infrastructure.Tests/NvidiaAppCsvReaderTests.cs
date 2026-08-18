using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class NvidiaAppCsvReaderTests
{
    [Fact]
    public async Task Reader_detects_and_loads_nvidia_app_performance_log()
    {
        var path = CreateTempCsv(
            "Timestamp (Elapsed time in seconds),PID,FPS,FPS 1(%) Low,Render Latency(MSec),GPU1 Utilization(%)\n" +
            ",24728,95.002,64.667,8.952,\n" +
            "1.174,24728,86.644,13.403,9.049,36\n" +
            "1.691,24728,86.447,13.403,8.868,95\n" +
            "2.191,24728,100.353,72.624,8.643,92\n");

        try
        {
            var reader = new FrameViewCsvReader();
            var capture = await reader.LoadCaptureAsync(path);

            Assert.Equal(CsvKind.Log, capture.Kind);
            Assert.True(CaptureSourceDetector.IsNvidiaAppPerformanceLog(capture));
            Assert.Equal(4, capture.RowCount);
            Assert.Equal("FPS", capture.Headers[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Capture_info_reads_nvidia_elapsed_duration()
    {
        var path = CreateTempCsv(
            "Timestamp (Elapsed time in seconds),PID,FPS,GPU1 Utilization(%)\n" +
            "0.500,10,90,95\n" +
            "5.750,10,92,96\n");

        try
        {
            var info = await new FrameViewCsvReader().ReadCaptureInfoAsync(path);

            Assert.NotNull(info);
            Assert.Equal(5.75, info!.DurationSeconds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempCsv(string content)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"NVIDIA_App_Performance_Log_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }
}
