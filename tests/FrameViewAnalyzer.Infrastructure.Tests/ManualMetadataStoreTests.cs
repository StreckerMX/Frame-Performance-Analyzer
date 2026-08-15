using System.IO;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class ManualMetadataStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "fva-metadata-" + Guid.NewGuid().ToString("N"), "metadata.json");

    [Fact]
    public void Set_and_get_round_trip_metadata()
    {
        var path = TempPath();
        try
        {
            var store = new JsonManualMetadataStore(path);
            var metadata = new ManualMetadata(
                BenchmarkName: "RTX 5090 Run",
                Resolution: "4K",
                GraphicsPreset: "Ultra",
                Upscaler: "DLSS",
                UpscalerQuality: "Performance",
                Tags: ["gpu", "night"]);

            store.Set("identity-1", metadata);

            var reloaded = new JsonManualMetadataStore(path);
            var loaded = reloaded.Get("identity-1");

            Assert.NotNull(loaded);
            Assert.Equal(metadata, loaded);
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Missing_store_loads_as_empty()
    {
        var store = new JsonManualMetadataStore(TempPath());

        Assert.Empty(store.Load());
        Assert.Null(store.Get("anything"));
    }

    [Fact]
    public void Malformed_store_loads_as_empty()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ this is not json");

            Assert.Empty(new JsonManualMetadataStore(path).Load());
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Unknown_format_version_loads_as_empty()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                """{"format_version": 99, "entries": {"x": {"metadata": {"game": "GTA"}}}}""");

            Assert.Empty(new JsonManualMetadataStore(path).Load());
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Empty_metadata_entries_are_dropped()
    {
        var path = TempPath();
        try
        {
            var store = new JsonManualMetadataStore(path);
            store.Set("keep", new ManualMetadata(Game: "GTA V"));
            store.Set("empty", new ManualMetadata());

            var reloaded = new JsonManualMetadataStore(path).Load();

            Assert.Single(reloaded);
            Assert.True(reloaded.ContainsKey("keep"));
            Assert.False(reloaded.ContainsKey("empty"));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Setting_empty_metadata_removes_the_entry()
    {
        var path = TempPath();
        try
        {
            var store = new JsonManualMetadataStore(path);
            store.Set("identity-1", new ManualMetadata(Game: "GTA V"));

            store.Set("identity-1", new ManualMetadata());

            Assert.Null(store.Get("identity-1"));
            Assert.Empty(new JsonManualMetadataStore(path).Load());
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Capture_identity_resolver_uses_name_size_and_mtime()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var filePath = Path.Combine(directory, "FrameView_Test_Log.csv");
            File.WriteAllText(filePath, "header\n1,2\n");
            var info = new FileInfo(filePath);

            var identity = CaptureIdentityResolver.TryBuild(filePath);

            Assert.NotNull(identity);
            Assert.Equal(
                CaptureIdentity.Build(
                    info.Name,
                    info.Length,
                    (long)((info.LastWriteTimeUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks * 100)),
                identity);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Capture_identity_resolver_returns_null_for_missing_files()
    {
        Assert.Null(CaptureIdentityResolver.TryBuild("Z:/definitely/missing/file.csv"));
    }

    [Fact]
    public void Unknown_store_version_loads_safely_as_empty()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                """{"format_version": 99, "entries": {"x": {"metadata": {"game": "GTA"}}}}""" + Environment.NewLine);

            var store = new JsonManualMetadataStore(path);

            Assert.Empty(store.Load());
            Assert.Null(store.Get("x"));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Saving_an_unknown_version_store_fails_without_overwriting()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var original =
                """{"format_version": 99, "entries": {"x": {"metadata": {"game": "GTA"}}}}""" + Environment.NewLine;
            File.WriteAllText(path, original);
            var store = new JsonManualMetadataStore(path);

            var error = Assert.Throws<InvalidOperationException>(
                () => store.Set("y", new ManualMetadata(Game: "New Game")));

            Assert.Contains("format version 99", error.Message);
            Assert.Equal(original, File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Unknown_version_bytes_remain_byte_for_byte_untouched()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var originalBytes = System.Text.Encoding.UTF8.GetBytes(
                """{"format_version": 77, "entries": {"a": {"metadata": {"tags": ["x"]}}}}""" + Environment.NewLine);
            File.WriteAllBytes(path, originalBytes);
            var store = new JsonManualMetadataStore(path);

            try
            {
                store.Set("a", new ManualMetadata(Notes: "n"));
            }
            catch (InvalidOperationException)
            {
                // Expected: the unknown store must not be touched.
            }

            Assert.Equal(originalBytes, File.ReadAllBytes(path));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Current_version_save_still_succeeds_atomically()
    {
        var path = TempPath();
        try
        {
            var store = new JsonManualMetadataStore(path);
            store.Set("first", new ManualMetadata(Game: "GTA V"));

            store.Set("first", new ManualMetadata(Game: "Updated"));

            var reloaded = new JsonManualMetadataStore(path).Get("first");
            Assert.NotNull(reloaded);
            Assert.Equal("Updated", reloaded!.Game);
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void First_save_with_no_file_creates_the_store()
    {
        var path = TempPath();
        try
        {
            var store = new JsonManualMetadataStore(path);

            store.Set("new", new ManualMetadata(Resolution: "4K"));

            Assert.True(File.Exists(path));
            Assert.Equal("4K", new JsonManualMetadataStore(path).Get("new")!.Resolution);
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
