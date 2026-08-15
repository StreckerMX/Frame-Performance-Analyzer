using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class DiscoverLogFilesTests
{
    [Fact]
    public void Only_frameview_logs_are_listed_newest_first()
    {
        using var temp = new TempDirectory();
        var logA = TestLogFactory.WriteLog(temp, "FrameView_A.exe_2026_08_13T033633_Log.csv");
        var logB = TestLogFactory.WriteLog(temp, "FrameView_B.exe_2026_08_13T071536_Log.csv");
        temp.WriteUtf8("FrameView_Summary.csv", "Avg FPS,Log Name\n");
        temp.WriteUtf8("notes.csv", "hello\n");
        File.SetLastWriteTimeUtc(logA, new DateTime(2026, 8, 13, 3, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(logB, new DateTime(2026, 8, 13, 7, 0, 0, DateTimeKind.Utc));

        var files = CaptureFolderScanner.DiscoverLogFiles(temp.Path);

        Assert.Equal(2, files.Count);
        Assert.Equal(logB, files[0]);
        Assert.Equal(logA, files[1]);
    }

    [Fact]
    public void Missing_directory_returns_empty()
    {
        Assert.Empty(CaptureFolderScanner.DiscoverLogFiles("Z:/does/not/exist"));
    }
}

public class ReadCaptureInfoTests
{
    private readonly FrameViewCsvReader _reader = new();

    [Fact]
    public async Task Real_metadata_is_extracted()
    {
        using var temp = new TempDirectory();
        var path = TestLogFactory.WriteLog(temp, "FrameView_A.exe_2026_08_13T033633_Log.csv");

        var info = await _reader.ReadCaptureInfoAsync(path);

        Assert.NotNull(info);
        Assert.Equal("GTA5 Enhanced", info.Application);
        Assert.Equal("2560x1440", info.Resolution);
        Assert.Equal("NVIDIA GeForce RTX 5070 Ti", info.Gpu);
        Assert.Contains("Ryzen 7 5700X3D", info.Cpu);
        Assert.Equal(4.5, info.DurationSeconds);
        Assert.Equal("A.exe_2026_08_13T033633", info.Name);
    }

    [Fact]
    public async Task Summary_files_are_rejected()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteUtf8("FrameView_Summary.csv", "Avg FPS,Log Name\n60,Game\n");

        Assert.Null(await _reader.ReadCaptureInfoAsync(path));
    }

    [Fact]
    public async Task Missing_files_return_null()
    {
        using var temp = new TempDirectory();
        Assert.Null(
            await _reader.ReadCaptureInfoAsync(System.IO.Path.Combine(temp.Path, "missing.csv")));
    }
}

public class ScanCaptureFolderTests
{
    [Fact]
    public async Task Folder_scan_returns_usable_infos()
    {
        using var temp = new TempDirectory();
        TestLogFactory.WriteLog(temp, "FrameView_A.exe_2026_08_13T033633_Log.csv");
        TestLogFactory.WriteLog(temp, "FrameView_B.exe_2026_08_13T071536_Log.csv", seconds: 3);
        temp.WriteUtf8("FrameView_Summary.csv", "Avg FPS,Log Name\n");
        var scanner = new CaptureFolderScanner(new FrameViewCsvReader());

        var infos = await scanner.ScanCaptureFolderAsync(temp.Path);

        Assert.Equal(2, infos.Count);
        Assert.All(infos, info => Assert.Equal("GTA5 Enhanced", info.Application));
    }
}
