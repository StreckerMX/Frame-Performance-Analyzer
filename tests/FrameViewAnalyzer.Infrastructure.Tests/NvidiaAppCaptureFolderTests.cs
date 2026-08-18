using FrameViewAnalyzer.Infrastructure;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class NvidiaAppCaptureFolderTests
{
    [Fact]
    public void DiscoverLogFiles_includes_nvidia_app_performance_logs()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fva-nvidia-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var nvidia = Path.Combine(directory, "NVIDIA_App_Performance_Log_2026-08-18T00-03-32.csv");
            var unrelated = Path.Combine(directory, "other.csv");
            File.WriteAllText(nvidia, "header");
            File.WriteAllText(unrelated, "header");

            var files = CaptureFolderScanner.DiscoverLogFiles(directory);

            Assert.Contains(nvidia, files);
            Assert.DoesNotContain(unrelated, files);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
