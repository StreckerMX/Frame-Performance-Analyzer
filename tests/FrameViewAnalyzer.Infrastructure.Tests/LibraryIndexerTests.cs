using System.IO;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class LibraryIndexerTests
{
    private static CaptureInfo Info(string path, string name, string resolution = "1920x1080") =>
        new(path, name, "Game.exe", resolution, "RTX 4080", "Ryzen 7", 60.0);

    [Fact]
    public void Upsert_uses_the_stable_capture_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-idx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "FrameView_2026_01_02T033633_Log.csv");
            File.WriteAllText(path, "TimeInSeconds,MsBetweenPresents\n0,10\n");
            var library = new LibraryModel();

            var result = new LibraryIndexer().Upsert(library, Info(path, "FrameView_2026_01_02T033633_Log.csv"), "now");

            Assert.True(result);
            Assert.Single(library.Records);
            var identity = Assert.Single(library.Records.Keys);
            Assert.StartsWith("FrameView_2026_01_02T033633_Log.csv|", identity);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Upsert_skips_an_explicitly_ignored_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-idx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "FrameView_2026_01_02T033633_Log.csv");
            File.WriteAllText(path, "TimeInSeconds,MsBetweenPresents\n0,10\n");
            var info = Info(path, "FrameView_2026_01_02T033633_Log.csv");
            var identity = CaptureIdentityResolver.TryBuild(path)!;
            var library = new LibraryModel();
            library.IgnoredIdentities.Add(identity);

            var result = new LibraryIndexer().Upsert(library, info, "now");

            Assert.False(result);
            Assert.Empty(library.Records);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Refresh_does_not_readd_an_ignored_capture_that_still_exists()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-idx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "FrameView_2026_01_02T033633_Log.csv");
            File.WriteAllText(file, "TimeInSeconds,MsBetweenPresents\n0,10\n1,10\n");
            var identity = CaptureIdentityResolver.TryBuild(file)!;
            var library = new LibraryModel();
            library.IgnoredIdentities.Add(identity);

            await new LibraryIndexer().RefreshAsync(
                library,
                directory,
                new CaptureFolderScanner(new FrameViewCsvReader()));

            Assert.True(File.Exists(file));
            Assert.Empty(library.Records);
            Assert.Contains(identity, library.IgnoredIdentities);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Refresh_marks_disappeared_captures_as_unavailable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-idx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "FrameView_2026_01_02T033633_Log.csv");
            File.WriteAllText(file, "TimeInSeconds,MsBetweenPresents\n0,10\n");
            var info = Info(file, "FrameView_2026_01_02T033633_Log.csv");
            var identity = CaptureIdentityResolver.TryBuild(file)!;
            var library = new LibraryModel();
            new LibraryIndexer().Upsert(library, info, "now");

            File.Delete(file);

            await new LibraryIndexer().RefreshAsync(
                library,
                directory,
                new CaptureFolderScanner(new FrameViewCsvReader()));

            Assert.False(library.Records[identity].Available);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Availability_resolves_through_the_active_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-idx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var original = Path.Combine(directory, "FrameView_2026_01_02T033633_Log.csv");
            File.WriteAllText(original, "TimeInSeconds,MsBetweenPresents\n0,10\n");
            var identity = CaptureIdentityResolver.TryBuild(original)!;

            var record = new LibraryRecord(
                identity,
                "C:/elsewhere/FrameView_2026_01_02T033633_Log.csv",
                "FrameView_2026_01_02T033633_Log.csv",
                "Game.exe",
                "1920x1080",
                "RTX 4080",
                "Ryzen 7",
                60.0,
                "now",
                "now");

            Assert.True(LibraryIndexer.ResolveAvailability(record, directory));
            Assert.Equal(original, LibraryIndexer.LocateIdentity(directory, identity));
            Assert.False(LibraryIndexer.ResolveAvailability(record));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
