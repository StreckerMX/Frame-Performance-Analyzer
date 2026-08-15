using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Exports;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class ExportServiceTests
{
    private readonly ExportService _service = new();
    private readonly FrameViewCsvReader _reader = new();
    private readonly CaptureAnalysisService _analysis = new();

    private static string TempPath(string name) =>
        Path.Combine(Path.GetTempPath(), "fva-exp-" + Guid.NewGuid().ToString("N"), name);

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "fva-exp-" + Guid.NewGuid().ToString("N"));

    private static IReadOnlyList<ComparisonRow> SampleRows(string name = "base") =>
    [
        new ComparisonRow(
            "fps", "FPS (Calculated)", "Performance", "FPS", "avg", "Average",
            name, 100.0, "comparison", 90.0, -10.0, -10.0, ImprovementKind.Regression),
    ];

    private static SessionAnalysis SessionOf()
    {
        var capture = new CaptureData
        {
            Path = "C:/captures/FrameView_2026_01_02T033633_Log.csv",
            DisplayName = "FrameView_2026_01_02T033633_Log",
            Kind = CsvKind.Log,
            Headers = ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            Columns =
            [
                ["0.0", "0.25", "0.5", "0.75", "1.0", "1.25"],
                ["10.0", "10.0", "10.0", "10.0", "10.0", "10.0"],
                ["80.0", "80.0", "80.0", "80.0", "80.0", "80.0"],
            ],
        };
        return new CaptureAnalysisService().Analyze(capture);
    }

    private static LibraryRecord Record(
        string identity,
        string game = "GTA V",
        string resolution = "4K",
        string? sourcePath = "C:/captures/a.csv",
        bool available = true,
        double? avgFps = 100.0,
        string sourceName = "a.csv") =>
        new(
            identity,
            sourcePath ?? string.Empty,
            sourceName,
            game,
            resolution,
            "RTX 5090",
            "Ryzen 7",
            60.0,
            "2026-01-01T00:00:00Z",
            "2026-01-02T00:00:00Z",
            available,
            avgFps is { } fps
                ? new Dictionary<string, double> { ["avg_fps"] = fps, ["p1_fps"] = 80.0 }
                : new Dictionary<string, double>());

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

    [Fact]
    public void Statistics_csv_has_a_utf8_bom_header_and_rows()
    {
        var path = TempPath("stats.csv");
        try
        {
            var count = _service.WriteStatisticsCsv(path, SampleRows());

            Assert.Equal(1, count);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);
            var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            Assert.StartsWith("metric_id,metric,category", text);
            Assert.Contains("fps,FPS (Calculated)", text);
            Assert.Contains("-10", text);
        }
        finally
        {
            Cleanup(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Statistics_csv_is_byte_identical_across_locales()
    {
        var original = CultureInfo.CurrentCulture;
        var directory = TempDirectory();
        try
        {
            byte[]? reference = null;
            foreach (var culture in new[] { "en-US", "es-MX", "de-DE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                var path = Path.Combine(directory, $"stats-{culture}.csv");
                _service.WriteStatisticsCsv(path, SampleRows("Ünïcode"));
                var bytes = File.ReadAllBytes(path);
                Assert.Contains((byte)'1', bytes); // numeric content present
                reference ??= bytes;
                Assert.Equal(reference, bytes);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            Cleanup(directory);
        }
    }

    [Fact]
    public void Statistics_csv_quotes_names_with_commas_quotes_and_unicode()
    {
        var path = TempPath("stats.csv");
        try
        {
            _service.WriteStatisticsCsv(path, SampleRows("Ünïcode \"Base\", the first"));

            var text = File.ReadAllText(path);

            Assert.Contains("\"Ünïcode \"\"Base\"\", the first\"", text);
        }
        finally
        {
            Cleanup(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Statistics_json_embeds_sessions_statistics_and_manual_metadata()
    {
        var path = TempPath("stats.json");
        try
        {
            var document = ExportReport.BuildStatisticsPayload(
                SessionOf(),
                null,
                new ManualMetadata(BenchmarkName: "RTX Run"));

            _service.WriteStatisticsJson(path, document);

            using var parsed = JsonDocument.Parse(File.ReadAllText(path));
            var root = parsed.RootElement;
            Assert.Equal(1, root.GetProperty("format_version").GetInt32());
            Assert.Equal(1, root.GetProperty("sessions").GetArrayLength());
            Assert.Equal(
                "RTX Run",
                root.GetProperty("sessions")[0]
                    .GetProperty("manual_metadata")
                    .GetProperty("benchmark_name")
                    .GetString());
            Assert.True(root.GetProperty("statistics").GetArrayLength() > 0);
        }
        finally
        {
            Cleanup(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Failed_write_never_leaves_a_partial_or_temporary_file()
    {
        var directory = TempDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            // A directory where the target should be makes the final replace
            // fail after the temporary file was fully written.
            var target = Path.Combine(directory, "stats.csv");
            Directory.CreateDirectory(target);

            var error = Assert.ThrowsAny<Exception>(() => _service.WriteStatisticsCsv(target, SampleRows()));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.False(File.Exists(target + ".tmp"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private static string PackageJson(string identity = "id1", string game = "GTA V") =>
        $$"""
        {
          "package_version": 1,
          "exported_at": "2026-01-02T00:00:00Z",
          "captures": [
            {
              "identity": "{{identity}}",
              "source_path": "C:/captures/a.csv",
              "source_name": "a.csv",
              "source_available": true,
              "detected": {"game": "{{game}}", "resolution": "4K", "gpu": "RTX 5090", "cpu": "Ryzen 7", "duration_seconds": 60.0},
              "manual_metadata": {"benchmark_name": "Night Run", "tags": ["gpu"]},
              "stats_summary": {"avg_fps": 100.0, "p1_fps": 80.0}
            }
          ]
        }
        """;

    [Fact]
    public void Package_export_validate_import_round_trip_succeeds()
    {
        var directory = TempDirectory();
        try
        {
            var library = new LibraryModel();
            library.Records["id1"] = Record("id1");
            var manual = new Dictionary<string, ManualMetadata>(StringComparer.Ordinal)
            {
                ["id1"] = new ManualMetadata(BenchmarkName: "Night Run", Tags: ["gpu"]),
            };
            var package = ExportReport.BuildBenchmarkPackage(library, manual);
            var path = Path.Combine(directory, "package.json");
            _service.WriteBenchmarkPackage(path, package);

            var json = File.ReadAllText(path);
            var validation = _service.ValidateBenchmarkPackage(json);
            Assert.Empty(validation.Errors);
            Assert.Single(validation.Valid);

            var target = new LibraryModel();
            var proposal = _service.ImportBenchmarkPackage(
                target,
                new Dictionary<string, ManualMetadata>(),
                json);
            Assert.Equal(1, proposal.Imported);
            Assert.Equal("GTA V", proposal.Library.Records["id1"].Game);
            Assert.Equal("Night Run", proposal.Metadata["id1"].BenchmarkName);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Prepare_package_hydrates_available_records_without_stats()
    {
        var directory = TempDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            var capturePath = Path.Combine(directory, "FrameView_2026_01_02T033633_Log.csv");
            File.WriteAllText(
                capturePath,
                "TimeInSeconds,MsBetweenPresents,GPU0Util(%)\n0.0,10,80\n0.25,10,80\n0.5,10,80\n0.75,10,80\n1.0,10,80\n1.25,10,80\n");
            var identity = CaptureIdentityResolver.TryBuild(capturePath)!;
            var library = new LibraryModel();
            library.Records[identity] = Record(identity, sourcePath: capturePath, avgFps: null);

            var result = await _service.PreparePackageAsync(
                library,
                new Dictionary<string, ManualMetadata>(),
                _reader,
                _analysis);

            Assert.Equal(1, result.Analyzed);
            Assert.Equal(0, result.Excluded);
            Assert.Equal(1, result.Exported);
            Assert.True(library.Records[identity].StatsSummary.ContainsKey("avg_fps"));

            // Round-trip through the real writer so the validator sees the
            // same snake_case document the user receives.
            var packagePath = Path.Combine(directory, "package.json");
            _service.WriteBenchmarkPackage(packagePath, result.Package);
            var validation = _service.ValidateBenchmarkPackage(File.ReadAllText(packagePath));
            Assert.Empty(validation.Errors);
            Assert.Single(validation.Valid);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Prepare_package_excludes_records_without_producible_stats()
    {
        var library = new LibraryModel();
        library.Records["gone"] = Record(
            "gone",
            sourcePath: "C:/missing/a.csv",
            available: true,
            avgFps: null);

        var result = await _service.PreparePackageAsync(
            library,
            new Dictionary<string, ManualMetadata>(),
            _reader,
            _analysis);

        Assert.Equal(0, result.Analyzed);
        Assert.Equal(1, result.Excluded);
        Assert.Empty(result.Package.Captures);
    }

    [Fact]
    public async Task Unavailable_records_with_a_digest_remain_portable()
    {
        var library = new LibraryModel();
        library.Records["offline"] = Record(
            "offline",
            sourcePath: "C:/missing/a.csv",
            available: false);

        var result = await _service.PreparePackageAsync(
            library,
            new Dictionary<string, ManualMetadata>(),
            _reader,
            _analysis);

        Assert.Equal(1, result.Exported);
        Assert.Equal(0, result.Excluded);
        Assert.Single(result.Package.Captures);
        Assert.False(result.Package.Captures[0].SourceAvailable);
    }

    [Fact]
    public void Package_round_trip_preserves_every_supported_field()
    {
        var library = new LibraryModel();
        library.Records["a"] = Record("a", game: "Game A", resolution: "4K");
        library.Records["b"] = Record("b", game: "Game B", resolution: "1080p");
        var manual = new Dictionary<string, ManualMetadata>(StringComparer.Ordinal)
        {
            ["a"] = new ManualMetadata(Upscaler: "DLSS", UpscalerQuality: "Quality", Tags: ["night"]),
        };
        var package = ExportReport.BuildBenchmarkPackage(library, manual);

        var proposal = _service.ImportBenchmarkPackage(
            new LibraryModel(),
            new Dictionary<string, ManualMetadata>(),
            JsonSerializer.Serialize(package, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            }));

        Assert.Equal(2, proposal.Imported);
        var a = proposal.Library.Records["a"];
        Assert.Equal("Game A", a.Game);
        Assert.Equal("4K", a.Resolution);
        Assert.Equal("RTX 5090", a.Gpu);
        Assert.Equal("Ryzen 7", a.Cpu);
        Assert.Equal(60.0, a.DurationSeconds);
        Assert.Equal(100.0, a.StatsSummary["avg_fps"]);
        Assert.Equal("DLSS Quality", proposal.Metadata["a"].ConfigLine);
        Assert.Equal(["night"], proposal.Metadata["a"].Tags);
    }

    [Fact]
    public void Package_round_trip_retains_analysis_options()
    {
        var library = new LibraryModel();
        library.Records["a"] = Record("a") with
        {
            AnalysisOptions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["gpu_threshold"] = "25",
                ["trim_buffer_seconds"] = "2",
                ["auto_gpu_threshold"] = "false",
                ["exclude_transitions"] = "true",
            },
        };
        var package = ExportReport.BuildBenchmarkPackage(
            library,
            new Dictionary<string, ManualMetadata>());

        var proposal = _service.ImportBenchmarkPackage(
            new LibraryModel(),
            new Dictionary<string, ManualMetadata>(),
            JsonSerializer.Serialize(package, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            }));

        Assert.Equal(
            "25",
            proposal.Library.Records["a"].AnalysisOptions["gpu_threshold"]);
        Assert.Equal(
            "2",
            proposal.Library.Records["a"].AnalysisOptions["trim_buffer_seconds"]);
        Assert.Equal(
            "false",
            proposal.Library.Records["a"].AnalysisOptions["auto_gpu_threshold"]);
    }

    [Fact]
    public void Version_mismatch_is_rejected_without_modifying_destination_state()
    {
        var library = new LibraryModel();
        library.Records["keep"] = Record("keep");
        var metadata = new Dictionary<string, ManualMetadata>(StringComparer.Ordinal)
        {
            ["keep"] = new ManualMetadata(Notes: "original"),
        };
        var json = """{"package_version": 99, "captures": []}""";

        var proposal = _service.ImportBenchmarkPackage(library, metadata, json);

        Assert.Equal(0, proposal.Imported);
        Assert.Equal(1, proposal.Skipped);
        Assert.True(proposal.Library.Records.ContainsKey("keep"));
        Assert.Equal("original", proposal.Metadata["keep"].Notes);
    }

    [Fact]
    public void Unsupported_package_version_is_reported()
    {
        var result = _service.ValidateBenchmarkPackage("""{"package_version": 99, "captures": []}""");

        Assert.Equal(["Unsupported or missing package_version."], result.Errors);
    }

    [Fact]
    public void Missing_identity_imports_under_an_imported_key()
    {
        var proposal = _service.ImportBenchmarkPackage(
            new LibraryModel(),
            new Dictionary<string, ManualMetadata>(),
            PackageJson(identity: ""));

        Assert.Equal(1, proposal.Imported);
        Assert.True(proposal.Library.Records.ContainsKey("imported:a.csv"));
    }

    [Fact]
    public void Coordinated_import_commits_library_and_metadata_together()
    {
        var directory = TempDirectory();
        try
        {
            var libraryPath = Path.Combine(directory, "library.json");
            var metadataPath = Path.Combine(directory, "metadata.json");
            var libraryStore = new JsonLibraryStore(libraryPath);
            var metadataStore = new JsonManualMetadataStore(metadataPath);
            var proposal = _service.ImportBenchmarkPackage(
                new LibraryModel(),
                new Dictionary<string, ManualMetadata>(),
                PackageJson());

            _service.CommitBenchmarkImport(proposal, libraryStore, metadataStore);

            var library = new JsonLibraryStore(libraryPath).Load();
            Assert.Single(library.Records);
            Assert.Equal("GTA V", library.Records["id1"].Game);
            var metadata = new JsonManualMetadataStore(metadataPath).Load();
            Assert.Equal("Night Run", metadata["id1"].BenchmarkName);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Library_commit_failure_restores_metadata_and_live_state()
    {
        var directory = TempDirectory();
        try
        {
            var libraryPath = Path.Combine(directory, "library.json");
            var metadataPath = Path.Combine(directory, "metadata.json");
            var libraryStore = new FailingLibraryStore(new JsonLibraryStore(libraryPath));
            var metadataStore = new JsonManualMetadataStore(metadataPath);

            var originalLibrary = new LibraryModel();
            originalLibrary.Records["keep"] = Record("keep", sourceName: "keep.csv");
            libraryStore.Save(originalLibrary);
            metadataStore.Set("keep", new ManualMetadata(Notes: "original notes"));

            var libraryBefore = File.ReadAllBytes(libraryPath);
            var metadataBefore = File.ReadAllBytes(metadataPath);
            var proposal = _service.ImportBenchmarkPackage(
                new LibraryModel(),
                new Dictionary<string, ManualMetadata>(),
                PackageJson());

            libraryStore.FailAtWriteCall = 1;
            var error = Assert.Throws<CoordinatedStoreCommitException>(
                () => _service.CommitBenchmarkImport(proposal, libraryStore, metadataStore));

            Assert.IsType<CoordinatedStoreCommitException>(error);
            Assert.Equal(metadataBefore, File.ReadAllBytes(metadataPath));
            Assert.Equal(libraryBefore, File.ReadAllBytes(libraryPath));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.Equal("original notes", metadataStore.Get("keep")?.Notes);
            Assert.Null(metadataStore.Get("id1"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Failure_before_the_first_live_replacement_leaves_both_stores_untouched()
    {
        var directory = TempDirectory();
        try
        {
            var libraryPath = Path.Combine(directory, "library.json");
            var metadataPath = Path.Combine(directory, "metadata.json");
            var libraryStore = new JsonLibraryStore(libraryPath);
            var metadataStore = new FailingMetadataStore(new JsonManualMetadataStore(metadataPath));

            var originalLibrary = new LibraryModel();
            originalLibrary.Records["keep"] = Record("keep", sourceName: "keep.csv");
            libraryStore.Save(originalLibrary);
            metadataStore.Set("keep", new ManualMetadata(Notes: "original notes"));

            var libraryBefore = File.ReadAllBytes(libraryPath);
            var metadataBefore = File.ReadAllBytes(metadataPath);
            var proposal = _service.ImportBenchmarkPackage(
                new LibraryModel(),
                new Dictionary<string, ManualMetadata>(),
                PackageJson());

            metadataStore.FailAtWriteCall = 1;
            var error = Assert.Throws<CoordinatedStoreCommitException>(
                () => _service.CommitBenchmarkImport(proposal, libraryStore, metadataStore));

            Assert.Contains("Neither store was modified", error.Message);
            Assert.Equal(libraryBefore, File.ReadAllBytes(libraryPath));
            Assert.Equal(metadataBefore, File.ReadAllBytes(metadataPath));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Rollback_removes_a_metadata_file_created_by_the_import()
    {
        var directory = TempDirectory();
        try
        {
            var libraryPath = Path.Combine(directory, "library.json");
            var metadataPath = Path.Combine(directory, "metadata.json");
            var libraryStore = new FailingLibraryStore(new JsonLibraryStore(libraryPath));
            var metadataStore = new JsonManualMetadataStore(metadataPath);

            var originalLibrary = new LibraryModel();
            originalLibrary.Records["keep"] = Record("keep", sourceName: "keep.csv");
            libraryStore.Save(originalLibrary);
            var libraryBefore = File.ReadAllBytes(libraryPath);
            Assert.False(File.Exists(metadataPath));

            var proposal = _service.ImportBenchmarkPackage(
                new LibraryModel(),
                new Dictionary<string, ManualMetadata>(),
                PackageJson());

            libraryStore.FailAtWriteCall = 1;
            Assert.Throws<CoordinatedStoreCommitException>(
                () => _service.CommitBenchmarkImport(proposal, libraryStore, metadataStore));

            Assert.False(
                File.Exists(metadataPath),
                "A metadata file created during the import must be removed by the rollback.");
            Assert.Equal(libraryBefore, File.ReadAllBytes(libraryPath));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Failed_coordinated_import_preserves_both_files_byte_for_byte()
    {
        var directory = TempDirectory();
        try
        {
            var libraryPath = Path.Combine(directory, "library.json");
            var metadataPath = Path.Combine(directory, "metadata.json");
            var libraryStore = new FailingLibraryStore(new JsonLibraryStore(libraryPath));
            var metadataStore = new JsonManualMetadataStore(metadataPath);

            var originalLibrary = new LibraryModel();
            originalLibrary.Records["a"] = Record("a", game: "Game A", sourceName: "a.csv");
            originalLibrary.Records["b"] = Record("b", game: "Game B", sourceName: "b.csv");
            libraryStore.Save(originalLibrary);
            metadataStore.Set("a", new ManualMetadata(Notes: "Ünïcode notes for A"));
            metadataStore.Set("b", new ManualMetadata(Notes: "notes for B"));

            var libraryBefore = File.ReadAllBytes(libraryPath);
            var metadataBefore = File.ReadAllBytes(metadataPath);
            var proposal = _service.ImportBenchmarkPackage(
                new LibraryModel(),
                new Dictionary<string, ManualMetadata>(),
                PackageJson());

            libraryStore.FailAtWriteCall = 1;
            Assert.Throws<CoordinatedStoreCommitException>(
                () => _service.CommitBenchmarkImport(proposal, libraryStore, metadataStore));

            Assert.True(libraryBefore.SequenceEqual(File.ReadAllBytes(libraryPath)));
            Assert.True(metadataBefore.SequenceEqual(File.ReadAllBytes(metadataPath)));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Unknown_version_metadata_store_aborts_before_modifying_the_library()
    {
        var directory = TempDirectory();
        try
        {
            var libraryPath = Path.Combine(directory, "library.json");
            var metadataPath = Path.Combine(directory, "metadata.json");
            var libraryStore = new JsonLibraryStore(libraryPath);
            var metadataStore = new JsonManualMetadataStore(metadataPath);

            Directory.CreateDirectory(directory);
            var originalLibrary = new LibraryModel();
            originalLibrary.Records["keep"] = Record("keep", sourceName: "keep.csv");
            libraryStore.Save(originalLibrary);
            File.WriteAllText(metadataPath, """{"format_version": 999, "entries": {}}""");
            var libraryBefore = File.ReadAllBytes(libraryPath);
            var metadataBefore = File.ReadAllBytes(metadataPath);
            var proposal = _service.ImportBenchmarkPackage(
                new LibraryModel(),
                new Dictionary<string, ManualMetadata>(),
                PackageJson());

            var error = Assert.Throws<CoordinatedStoreCommitException>(
                () => _service.CommitBenchmarkImport(proposal, libraryStore, metadataStore));

            Assert.Contains("unsupported format version", error.Message);
            Assert.Equal(libraryBefore, File.ReadAllBytes(libraryPath));
            Assert.Equal(metadataBefore, File.ReadAllBytes(metadataPath));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Unknown_version_library_store_aborts_before_modifying_the_metadata()
    {
        var directory = TempDirectory();
        try
        {
            var libraryPath = Path.Combine(directory, "library.json");
            var metadataPath = Path.Combine(directory, "metadata.json");
            var libraryStore = new JsonLibraryStore(libraryPath);
            var metadataStore = new JsonManualMetadataStore(metadataPath);

            Directory.CreateDirectory(directory);
            File.WriteAllText(libraryPath, """{"format_version": 999, "records": {}}""");
            metadataStore.Set("keep", new ManualMetadata(Notes: "original notes"));
            var libraryBefore = File.ReadAllBytes(libraryPath);
            var metadataBefore = File.ReadAllBytes(metadataPath);
            var proposal = _service.ImportBenchmarkPackage(
                new LibraryModel(),
                new Dictionary<string, ManualMetadata>(),
                PackageJson());

            var error = Assert.Throws<CoordinatedStoreCommitException>(
                () => _service.CommitBenchmarkImport(proposal, libraryStore, metadataStore));

            Assert.Contains("unsupported format version", error.Message);
            Assert.Equal(libraryBefore, File.ReadAllBytes(libraryPath));
            Assert.Equal(metadataBefore, File.ReadAllBytes(metadataPath));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Successful_import_publishes_in_memory_state_only_after_both_stores_persist()
    {
        var directory = TempDirectory();
        try
        {
            var libraryPath = Path.Combine(directory, "library.json");
            var metadataPath = Path.Combine(directory, "metadata.json");
            var libraryStore = new JsonLibraryStore(libraryPath);
            var metadataStore = new JsonManualMetadataStore(metadataPath);
            var proposal = _service.ImportBenchmarkPackage(
                new LibraryModel(),
                new Dictionary<string, ManualMetadata>(),
                PackageJson());

            _service.CommitBenchmarkImport(proposal, libraryStore, metadataStore);

            // The transaction itself never publishes in-memory state.
            Assert.Null(metadataStore.Get("id1"));

            // Publishing happens explicitly, only after both writes succeeded.
            metadataStore.Reload();
            Assert.Equal("Night Run", metadataStore.Get("id1")?.BenchmarkName);
            Assert.Equal("GTA V", new JsonLibraryStore(libraryPath).Load().Records["id1"].Game);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Repeated_coordinated_imports_are_deterministic_and_idempotent()
    {
        var directory = TempDirectory();
        try
        {
            var libraryPath = Path.Combine(directory, "library.json");
            var metadataPath = Path.Combine(directory, "metadata.json");
            var libraryStore = new JsonLibraryStore(libraryPath);
            var metadataStore = new JsonManualMetadataStore(metadataPath);
            var json = PackageJson();

            var first = _service.ImportBenchmarkPackage(
                new LibraryModel(),
                new Dictionary<string, ManualMetadata>(),
                json);
            _service.CommitBenchmarkImport(first, libraryStore, metadataStore);
            var libraryAfterFirst = File.ReadAllBytes(libraryPath);
            var metadataAfterFirst = File.ReadAllBytes(metadataPath);

            var second = _service.ImportBenchmarkPackage(
                libraryStore.Load(),
                metadataStore.Load(),
                json);
            _service.CommitBenchmarkImport(second, libraryStore, metadataStore);

            Assert.Equal(libraryAfterFirst, File.ReadAllBytes(libraryPath));
            Assert.Equal(metadataAfterFirst, File.ReadAllBytes(metadataPath));
            Assert.Single(new JsonLibraryStore(libraryPath).Load().Records);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private sealed class FailingLibraryStore(JsonLibraryStore inner) : ILibraryStore, IStoreDestination
    {
        public string FilePath => inner.FilePath;

        public int ExpectedVersion => inner.ExpectedVersion;

        public int? ReadVersion() => inner.ReadVersion();

        public byte[]? ReadCurrentBytes() => inner.ReadCurrentBytes();

        public void Delete() => inner.Delete();

        public int FailAtWriteCall { get; set; } = int.MaxValue;

        public int WriteCalls { get; private set; }

        public void Write(byte[] bytes)
        {
            WriteCalls++;
            if (WriteCalls == FailAtWriteCall)
            {
                throw new IOException("Simulated library commit failure.");
            }

            inner.Write(bytes);
        }

        public LibraryModel Load() => inner.Load();

        public void Save(LibraryModel library) => inner.Save(library);
    }

    private sealed class FailingMetadataStore(JsonManualMetadataStore inner)
        : IManualMetadataStore, IStoreDestination
    {
        public string FilePath => inner.FilePath;

        public int ExpectedVersion => inner.ExpectedVersion;

        public int? ReadVersion() => inner.ReadVersion();

        public byte[]? ReadCurrentBytes() => inner.ReadCurrentBytes();

        public void Delete() => inner.Delete();

        public int FailAtWriteCall { get; set; } = int.MaxValue;

        public int WriteCalls { get; private set; }

        public void Write(byte[] bytes)
        {
            WriteCalls++;
            if (WriteCalls == FailAtWriteCall)
            {
                throw new IOException("Simulated metadata commit failure.");
            }

            inner.Write(bytes);
        }

        public ManualMetadata? Get(string identity) => inner.Get(identity);

        public void Set(string identity, ManualMetadata? metadata) => inner.Set(identity, metadata);

        public IReadOnlyDictionary<string, ManualMetadata> Load() => inner.Load();

        public void Save(IReadOnlyDictionary<string, ManualMetadata> entries) => inner.Save(entries);

        public void Reload() => inner.Reload();
    }
}
