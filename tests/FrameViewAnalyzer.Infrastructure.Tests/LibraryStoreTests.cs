using System.IO;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class LibraryStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "fva-library-" + Guid.NewGuid().ToString("N"), "library.json");

    private static LibraryModel SampleLibrary()
    {
        var library = new LibraryModel();
        library.Records["id-1"] = new LibraryRecord(
            "id-1",
            "C:/captures/a.csv",
            "FrameView_2026_01_02T033633_Log.csv",
            "GTA5 Enhanced",
            "1920x1080",
            "RTX 4090",
            "Ryzen 7",
            42.5,
            "2026-01-01T00:00:00Z",
            "2026-01-02T00:00:00Z",
            Available: true,
            StatsSummary: new Dictionary<string, double> { ["avg_fps"] = 100.0 });
        library.RecentComparisons.Add(("id-1", "id-2"));
        return library;
    }

    [Fact]
    public void Save_and_load_round_trip_the_library()
    {
        var path = TempPath();
        try
        {
            new JsonLibraryStore(path).Save(SampleLibrary());

            var loaded = new JsonLibraryStore(path).Load();

            Assert.Equal(1, loaded.FormatVersion);
            var record = loaded.Records["id-1"];
            Assert.Equal("GTA5 Enhanced", record.Game);
            Assert.Equal(100.0, record.StatsSummary["avg_fps"]);
            Assert.Equal(42.5, record.DurationSeconds);
            Assert.Equal(("id-1", "id-2"), Assert.Single(loaded.RecentComparisons));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Missing_store_loads_as_empty()
    {
        Assert.Empty(new JsonLibraryStore(TempPath()).Load().Records);
    }

    [Fact]
    public void Malformed_store_loads_as_empty()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "not json at all");

            Assert.Empty(new JsonLibraryStore(path).Load().Records);
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Unknown_version_loads_as_empty_and_save_refuses_to_overwrite()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var original =
                """{"format_version": 99, "records": {"x": {}}}""" + Environment.NewLine;
            File.WriteAllText(path, original);
            var store = new JsonLibraryStore(path);

            Assert.Empty(store.Load().Records);

            var error = Assert.Throws<InvalidOperationException>(() => store.Save(SampleLibrary()));

            Assert.Contains("format version 99", error.Message);
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void First_save_creates_the_store()
    {
        var path = TempPath();
        try
        {
            new JsonLibraryStore(path).Save(SampleLibrary());

            Assert.True(File.Exists(path));
            Assert.Single(new JsonLibraryStore(path).Load().Records);
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Records_without_identity_are_dropped_on_load()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                """{"format_version": 1, "records": {"key": {"source_name": "no-identity"}}, "recent_comparisons": []}""");

            Assert.Empty(new JsonLibraryStore(path).Load().Records);
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; transient handles may briefly linger.
        }
    }
}
