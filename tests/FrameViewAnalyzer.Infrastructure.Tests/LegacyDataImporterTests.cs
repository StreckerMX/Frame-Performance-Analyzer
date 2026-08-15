using System.IO;
using FrameViewAnalyzer.Infrastructure.Legacy;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.Infrastructure.Tests;

/// <summary>
/// One-way legacy import tests: precedence, idempotence, partial failure,
/// capping, and byte-for-byte preservation of the legacy files.
/// </summary>
public class LegacyDataImporterTests
{
    private static string NewRoot(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));

    private static LegacyDataImporter Create(string legacyRoot, string v2Root) =>
        new(
            new JsonSettingsStore(Path.Combine(v2Root, "settings.json")),
            new JsonManualMetadataStore(Path.Combine(v2Root, "metadata.json")),
            new JsonLibraryStore(Path.Combine(v2Root, "library.json")),
            legacyRoot,
            Path.Combine(v2Root, "settings.json"));

    private static void Write(string root, string fileName, string content)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, fileName), content);
    }

    private static string LegacySettingsJson => """
        {"capture_directory": "C:/legacy-captures", "appearance_mode": "light"}
        """;

    private static string LegacyMetadataJson => """
        {
          "format_version": 1,
          "entries": {
            "id1": {"metadata": {"game": "GTA V", "resolution": "4K", "tags": ["gpu", "night"]}},
            "id2": {"metadata": {"benchmark_name": "Cyber Run"}}
          }
        }
        """;

    private static string LegacyLibraryJson => """
        {
          "format_version": 1,
          "records": {
            "id1": {
              "identity": "id1", "source_path": "C:/old/a.csv", "source_name": "a.csv",
              "game": "GTA V", "resolution": "4K", "gpu": "RTX 5090", "cpu": "Ryzen 7",
              "duration_seconds": 143.2, "added_at": "2026-01-01T00:00:00Z",
              "last_seen_at": "2026-01-02T00:00:00Z", "available": true,
              "analysis_options": {"gpu_threshold": "10"}, "stats_summary": {"avg_fps": 100.0}
            }
          },
          "recent_comparisons": [["id1", "id2"]]
        }
        """;

    private static void Delete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void No_legacy_files_is_a_safe_no_op()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            var result = Create(legacy, v2).Import();

            Assert.Equal(LegacySettingsOutcome.NoLegacy, result.Settings);
            Assert.Equal(0, result.MetadataImported);
            Assert.Equal(0, result.LibraryImported);
            Assert.False(File.Exists(Path.Combine(v2, "settings.json")));
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Valid_settings_import_when_v2_is_absent()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "settings.json", LegacySettingsJson);

            var result = Create(legacy, v2).Import();

            Assert.Equal(LegacySettingsOutcome.Imported, result.Settings);
            var settings = new JsonSettingsStore(Path.Combine(v2, "settings.json")).Load();
            Assert.Equal("C:/legacy-captures", settings.CaptureDirectory);
            Assert.Equal("light", settings.AppearanceMode);
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Existing_v2_settings_are_never_overwritten()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "settings.json", LegacySettingsJson);
            var v2SettingsPath = Path.Combine(v2, "settings.json");
            new JsonSettingsStore(v2SettingsPath).Save(new FrameViewAnalyzer.Infrastructure.Stores.SettingsDocument(
                FormatVersion: 1,
                CaptureDirectory: "C:/v2-captures",
                AppearanceMode: "dark",
                Window: null));

            var result = Create(legacy, v2).Import();

            Assert.Equal(LegacySettingsOutcome.SkippedV2Exists, result.Settings);
            var settings = new JsonSettingsStore(v2SettingsPath).Load();
            Assert.Equal("C:/v2-captures", settings.CaptureDirectory);
            Assert.Equal("dark", settings.AppearanceMode);
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Valid_metadata_imports_into_v2()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "metadata.json", LegacyMetadataJson);

            var result = Create(legacy, v2).Import();

            Assert.Equal(2, result.MetadataImported);
            var v2Metadata = new JsonManualMetadataStore(Path.Combine(v2, "metadata.json"));
            Assert.Equal("GTA V", v2Metadata.Get("id1")!.Game);
            Assert.Equal(["gpu", "night"], v2Metadata.Get("id1")!.Tags);
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Existing_v2_metadata_wins()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "metadata.json", LegacyMetadataJson);
            var metadataStore = new JsonManualMetadataStore(Path.Combine(v2, "metadata.json"));
            metadataStore.Set("id1", new FrameViewAnalyzer.Core.Models.ManualMetadata(Game: "V2 Game"));

            var result = Create(legacy, v2).Import();

            Assert.Equal(1, result.MetadataImported);
            Assert.Equal(1, result.MetadataAlreadyPresent);
            Assert.Equal("V2 Game", metadataStore.Get("id1")!.Game);
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Valid_library_records_import()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "library.json", LegacyLibraryJson);

            var result = Create(legacy, v2).Import();

            Assert.Equal(1, result.LibraryImported);
            var library = new JsonLibraryStore(Path.Combine(v2, "library.json")).Load();
            var record = library.Records["id1"];
            Assert.Equal("GTA V", record.Game);
            Assert.Equal(143.2, record.DurationSeconds);
            Assert.Equal("10", record.AnalysisOptions["gpu_threshold"]);
            Assert.Equal(1, result.RecentComparisonsImported);
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Existing_v2_library_records_win()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "library.json", LegacyLibraryJson);
            var libraryStore = new JsonLibraryStore(Path.Combine(v2, "library.json"));
            var library = new FrameViewAnalyzer.Analytics.Library.LibraryModel();
            library.Records["id1"] = new FrameViewAnalyzer.Core.Models.LibraryRecord(
                "id1", "C:/v2/a.csv", "a.csv", "V2 Game", "1080p", "RTX 4070", "Ryzen 5",
                50.0, "now", "now");
            libraryStore.Save(library);

            var result = Create(legacy, v2).Import();

            Assert.Equal(0, result.LibraryImported);
            Assert.Equal(1, result.LibraryAlreadyPresent);
            var record = libraryStore.Load().Records["id1"];
            Assert.Equal("V2 Game", record.Game);
            Assert.Equal("1080p", record.Resolution);
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Recent_comparisons_are_deduplicated()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "library.json", LegacyLibraryJson);
            var libraryStore = new JsonLibraryStore(Path.Combine(v2, "library.json"));
            var library = new FrameViewAnalyzer.Analytics.Library.LibraryModel();
            library.RecentComparisons.Add(("id1", "id2"));
            libraryStore.Save(library);

            var result = Create(legacy, v2).Import();

            Assert.Equal(0, result.RecentComparisonsImported);
            Assert.Single(libraryStore.Load().RecentComparisons);
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Recent_comparisons_remain_capped_at_five()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            var pairs = string.Join(
                ",",
                Enumerable.Range(0, 8).Select(index => $"[\"a{index}\",\"b{index}\"]"));
            Write(
                legacy,
                "library.json",
                $$"""{"format_version": 1, "records": {}, "recent_comparisons": [{{pairs}}]}""");

            var result = Create(legacy, v2).Import();

            Assert.Equal(5, result.RecentComparisonsImported);
            var recent = new JsonLibraryStore(Path.Combine(v2, "library.json")).Load().RecentComparisons;
            Assert.Equal(5, recent.Count);
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Repeated_imports_are_idempotent()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "settings.json", LegacySettingsJson);
            Write(legacy, "metadata.json", LegacyMetadataJson);
            Write(legacy, "library.json", LegacyLibraryJson);
            var importer = Create(legacy, v2);

            importer.Import();
            var second = importer.Import();

            Assert.Equal(LegacySettingsOutcome.SkippedV2Exists, second.Settings);
            Assert.Equal(0, second.MetadataImported);
            Assert.Equal(2, second.MetadataAlreadyPresent);
            Assert.Equal(0, second.LibraryImported);
            Assert.Equal(0, second.RecentComparisonsImported);
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Malformed_settings_are_skipped_safely()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "settings.json", "{ broken");

            var result = Create(legacy, v2).Import();

            Assert.Equal(LegacySettingsOutcome.Malformed, result.Settings);
            Assert.Equal(1, result.MalformedStores);
            Assert.False(File.Exists(Path.Combine(v2, "settings.json")));
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Malformed_metadata_is_skipped_safely()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "metadata.json", "{ broken");

            var result = Create(legacy, v2).Import();

            Assert.Equal(0, result.MetadataImported);
            Assert.Equal(1, result.MalformedStores);
            Assert.False(File.Exists(Path.Combine(v2, "metadata.json")));
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Malformed_library_is_skipped_safely()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "library.json", "{ broken");

            var result = Create(legacy, v2).Import();

            Assert.Equal(0, result.LibraryImported);
            Assert.Equal(1, result.MalformedStores);
            Assert.False(File.Exists(Path.Combine(v2, "library.json")));
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Partial_failure_does_not_block_the_other_stores()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "settings.json", LegacySettingsJson);
            Write(legacy, "metadata.json", "{ broken");
            Write(legacy, "library.json", LegacyLibraryJson);

            var result = Create(legacy, v2).Import();

            Assert.Equal(LegacySettingsOutcome.Imported, result.Settings);
            Assert.Equal(0, result.MetadataImported);
            Assert.Equal(1, result.LibraryImported);
            Assert.Equal(1, result.MalformedStores);
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }

    [Fact]
    public void Legacy_files_remain_byte_for_byte_unchanged()
    {
        var legacy = NewRoot("legacy-");
        var v2 = NewRoot("v2-");
        try
        {
            Write(legacy, "settings.json", LegacySettingsJson);
            Write(legacy, "metadata.json", LegacyMetadataJson);
            Write(legacy, "library.json", LegacyLibraryJson);
            var before = Directory.GetFiles(legacy)
                .ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);

            Create(legacy, v2).Import();

            foreach (var (fileName, bytes) in before)
            {
                Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(legacy, fileName)));
            }
        }
        finally
        {
            Delete(legacy);
            Delete(v2);
        }
    }
}
