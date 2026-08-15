using System.Globalization;
using System.Text;

namespace FrameViewAnalyzer.Infrastructure.Tests;

/// <summary>Temp folder removed on dispose; shared capture fixture writer.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "fva-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string WriteUtf8(string fileName, string content, bool withBom = false)
    {
        var filePath = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(filePath, content, new UTF8Encoding(withBom));
        return filePath;
    }

    public string WriteBytes(string fileName, byte[] content)
    {
        var filePath = System.IO.Path.Combine(Path, fileName);
        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}

internal static class TestLogFactory
{
    /// <summary>
    /// A small FrameView log with the same shape as the Python test fixture:
    /// two frames per second, TimeInSeconds 0.0 … (seconds - 0.5).
    /// </summary>
    public static string WriteLog(
        TempDirectory directory,
        string fileName,
        string application = "GTA5_Enhanced.exe",
        string resolution = "2560x1440",
        string gpu = "NVIDIA GeForce RTX 5070 Ti",
        string cpu = "AMD Ryzen 7 5700X3D 8-Core Processor",
        int seconds = 5)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Application,Resolution,GPU,CPU,TimeInSeconds,MsBetweenPresents,GPU0Util(%)");
        for (var second = 0; second < seconds; second++)
        {
            foreach (var offset in new[] { 0.0, 0.5 })
            {
                var time = (second + offset).ToString("F1", CultureInfo.InvariantCulture);
                builder.AppendLine($"{application},{resolution},{gpu},{cpu},{time},10,90");
            }
        }

        return directory.WriteUtf8(fileName, builder.ToString());
    }
}
