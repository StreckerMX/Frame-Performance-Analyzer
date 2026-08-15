using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.Infrastructure.Exports;

public sealed record PackageValidationResult(
    IReadOnlyList<PackageCaptureImport> Valid,
    IReadOnlyList<string> Errors);

public sealed record PackageCaptureImport(
    string Identity,
    string SourcePath,
    string SourceName,
    string Game,
    string Resolution,
    string Gpu,
    string Cpu,
    double? DurationSeconds,
    ManualMetadata? ManualMetadata,
    IReadOnlyDictionary<string, double> StatsSummary,
    IReadOnlyDictionary<string, string> AnalysisOptions,
    bool SourceAvailable);

/// <summary>
/// Proposed merged state for a package import. Nothing is persisted by this
/// type and no live state is mutated; the caller publishes it through
/// IExportService.CommitBenchmarkImport, which serializes both documents
/// completely, version-checks both destinations, and commits the metadata
/// store and the library store together with rollback. A failure restores
/// the original files (or removes a newly created one), cleans temporary
/// files, and throws — the in-memory state is published only after both
/// writes succeed.
/// </summary>
public sealed record PackageImportProposal(
    LibraryModel Library,
    IReadOnlyDictionary<string, ManualMetadata> Metadata,
    int Imported,
    int Skipped);

/// <summary>Result of a user-facing package export after digest hydration.</summary>
public sealed record PackageExportResult(
    BenchmarkPackageDto Package,
    int Exported,
    int Analyzed,
    int Excluded);

/// <summary>File-writing half of the export system (CSV, JSON, packages).</summary>
public interface IExportService
{
    int WriteStatisticsCsv(string path, IReadOnlyList<ComparisonRow> rows);

    void WriteStatisticsJson(string path, ExportStatisticsDto document);

    void WriteBenchmarkPackage(string path, BenchmarkPackageDto package);

    PackageValidationResult ValidateBenchmarkPackage(string json);

    PackageImportProposal ImportBenchmarkPackage(
        LibraryModel currentLibrary,
        IReadOnlyDictionary<string, ManualMetadata> currentMetadata,
        string json);

    /// <summary>
    /// Commits a package import to both V2 stores as one coordinated
    /// operation: both documents are fully serialized first, both
    /// destinations are version-checked, and a failure of the second store
    /// write rolls the first one back to its original bytes (or removes it
    /// when it did not previously exist). Throws
    /// CoordinatedStoreCommitException on a controlled failure and
    /// CoordinatedStoreRollbackException when the automatic restoration
    /// itself fails. In-memory state is never published here.
    /// </summary>
    void CommitBenchmarkImport(
        PackageImportProposal proposal,
        ILibraryStore libraryStore,
        IManualMetadataStore metadataStore);

    Task<PackageExportResult> PreparePackageAsync(
        LibraryModel library,
        IReadOnlyDictionary<string, ManualMetadata> manualLookup,
        IFrameViewCsvReader reader,
        ICaptureAnalysisService analysis);
}

/// <summary>
/// Writes the export formats atomically (temp file + replace; partial files
/// are never left behind), implements the package hydration pipeline, and
/// commits package imports to both V2 stores as one coordinated operation
/// (serialize both documents, version-check both destinations, write with
/// rollback). The Statistics CSV is a pure table (UTF-8 BOM, invariant
/// numbers, proper quoting); Benchmark JSON and packages use snake_case keys
/// with JSON numbers. The portable package never embeds raw CSV contents.
/// </summary>
public sealed class ExportService : IExportService
{
    private static readonly string[] CsvFields =
    [
        "metric_id",
        "metric",
        "category",
        "unit",
        "statistic_key",
        "statistic",
        "base_session",
        "base_value",
        "comparison_session",
        "comparison_value",
        "delta",
        "delta_percent",
    ];

    private static readonly JsonSerializerOptions JsonExportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public int WriteStatisticsCsv(string path, IReadOnlyList<ComparisonRow> rows)
    {
        WriteAtomically(path, target =>
        {
            using var writer = new StreamWriter(
                target,
                append: false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            foreach (var field in CsvFields)
            {
                csv.WriteField(field);
            }

            csv.NextRecord();
            foreach (var row in rows)
            {
                csv.WriteField(row.MetricId);
                csv.WriteField(row.MetricLabel);
                csv.WriteField(row.Category);
                csv.WriteField(row.Unit);
                csv.WriteField(row.StatisticKey);
                csv.WriteField(row.StatisticLabel);
                csv.WriteField(row.BaseSession);
                csv.WriteField(row.BaseValue);
                csv.WriteField(row.ComparisonSession);
                csv.WriteField(row.ComparisonValue);
                csv.WriteField(row.Delta);
                csv.WriteField(row.DeltaPercent);
                csv.NextRecord();
            }
        });

        return rows.Count;
    }

    public void WriteStatisticsJson(string path, ExportStatisticsDto document) =>
        WriteAtomically(
            path,
            target => File.WriteAllText(
                target,
                JsonSerializer.Serialize(document, JsonExportOptions) + Environment.NewLine,
                new UTF8Encoding(false)));

    public void WriteBenchmarkPackage(string path, BenchmarkPackageDto package) =>
        WriteAtomically(
            path,
            target => File.WriteAllText(
                target,
                JsonSerializer.Serialize(package, JsonExportOptions) + Environment.NewLine,
                new UTF8Encoding(false)));

    public PackageValidationResult ValidateBenchmarkPackage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("package_version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || version.GetInt32() != ExportReport.PackageVersion)
        {
            return new PackageValidationResult([], ["Unsupported or missing package_version."]);
        }

        if (!root.TryGetProperty("captures", out var captures)
            || captures.ValueKind != JsonValueKind.Array)
        {
            return new PackageValidationResult([], ["Missing 'captures' list."]);
        }

        var valid = new List<PackageCaptureImport>();
        var errors = new List<string>();
        var index = 0;
        foreach (var raw in captures.EnumerateArray())
        {
            if (raw.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"Capture {index}: not an object.");
                index++;
                continue;
            }

            var sourceName = Field(raw, "source_name");
            var detected = Object(raw, "detected");
            var game = Field(detected, "game");
            var resolution = Field(detected, "resolution");
            var stats = Object(raw, "stats_summary");
            var hasAverageFps = stats is { } statsElement
                && statsElement.TryGetProperty("avg_fps", out var average)
                && average.ValueKind == JsonValueKind.Number;
            if (sourceName.Length == 0)
            {
                errors.Add($"Capture {index}: missing source_name.");
                index++;
                continue;
            }

            if (game.Length == 0)
            {
                errors.Add($"Capture {index} ({sourceName}): missing game.");
                index++;
                continue;
            }

            if (resolution.Length == 0)
            {
                errors.Add($"Capture {index} ({sourceName}): missing resolution.");
                index++;
                continue;
            }

            if (!hasAverageFps)
            {
                errors.Add($"Capture {index} ({sourceName}): missing average FPS.");
                index++;
                continue;
            }

            var identity = Field(raw, "identity");
            valid.Add(new PackageCaptureImport(
                Identity: identity,
                SourcePath: Field(raw, "source_path"),
                SourceName: sourceName,
                Game: game,
                Resolution: resolution,
                Gpu: Field(detected, "gpu"),
                Cpu: Field(detected, "cpu"),
                DurationSeconds: Number(detected, "duration_seconds"),
                ManualMetadata: ParseManualMetadata(raw),
                StatsSummary: ParseStats(stats),
                AnalysisOptions: ParseOptions(Object(raw, "analysis_options")),
                SourceAvailable: raw.TryGetProperty("source_available", out var available)
                    && available.ValueKind == JsonValueKind.True));
            index++;
        }

        return new PackageValidationResult(valid, errors);
    }

    public PackageImportProposal ImportBenchmarkPackage(
        LibraryModel currentLibrary,
        IReadOnlyDictionary<string, ManualMetadata> currentMetadata,
        string json)
    {
        var validation = ValidateBenchmarkPackage(json);

        // Build the proposed merged state first; publish only after both
        // stores save successfully (see PackageImportProposal docs).
        var library = new LibraryModel();
        foreach (var (identity, record) in currentLibrary.Records)
        {
            library.Records[identity] = record;
        }

        foreach (var (first, second) in currentLibrary.RecentComparisons)
        {
            library.RecentComparisons.Add((first, second));
        }

        var metadata = new Dictionary<string, ManualMetadata>(currentMetadata, StringComparer.Ordinal);
        var imported = 0;
        var now = LibraryUpdater.NowIso();
        foreach (var capture in validation.Valid)
        {
            var identity = capture.Identity.Length > 0
                ? capture.Identity
                : $"imported:{capture.SourceName}";
            var existing = library.Records.TryGetValue(identity, out var record) ? record : null;
            var sourceExists = capture.SourcePath.Length > 0 && File.Exists(capture.SourcePath);
            library.Records[identity] = existing is null
                ? new LibraryRecord(
                    identity,
                    capture.SourcePath,
                    capture.SourceName,
                    capture.Game,
                    capture.Resolution,
                    capture.Gpu,
                    capture.Cpu,
                    capture.DurationSeconds,
                    now,
                    now,
                    sourceExists,
                    capture.StatsSummary,
                    capture.AnalysisOptions)
                : existing with
                {
                    SourcePath = capture.SourcePath.Length > 0 ? capture.SourcePath : existing.SourcePath,
                    SourceName = capture.SourceName,
                    Game = capture.Game,
                    Resolution = capture.Resolution,
                    Gpu = capture.Gpu,
                    Cpu = capture.Cpu,
                    DurationSeconds = capture.DurationSeconds ?? existing.DurationSeconds,
                    StatsSummary = capture.StatsSummary,
                    AnalysisOptions = capture.AnalysisOptions.Count > 0
                        ? capture.AnalysisOptions
                        : existing.AnalysisOptions,
                };
            imported++;

            if (capture.ManualMetadata is { IsEmpty: false } manual)
            {
                metadata[identity] = manual;
            }
        }

        return new PackageImportProposal(library, metadata, imported, validation.Errors.Count);
    }

    public void CommitBenchmarkImport(
        PackageImportProposal proposal,
        ILibraryStore libraryStore,
        IManualMetadataStore metadataStore)
    {
        if (metadataStore is not IStoreDestination metadataDestination)
        {
            throw new InvalidOperationException(
                $"The metadata store '{metadataStore.GetType().Name}' does not support coordinated commits.");
        }

        if (libraryStore is not IStoreDestination libraryDestination)
        {
            throw new InvalidOperationException(
                $"The library store '{libraryStore.GetType().Name}' does not support coordinated commits.");
        }

        // Stage: serialize BOTH proposed documents completely before either
        // live file is touched. A serialization failure aborts without any
        // change; a failed second-store write rolls the first store back.
        var metadataBytes = JsonManualMetadataStore.SerializeDocument(proposal.Metadata);
        var libraryBytes = JsonLibraryStore.SerializeDocument(proposal.Library);

        StoreCommitTransaction.Commit(
            metadataDestination,
            metadataBytes,
            libraryDestination,
            libraryBytes);
    }

    public async Task<PackageExportResult> PreparePackageAsync(
        LibraryModel library,
        IReadOnlyDictionary<string, ManualMetadata> manualLookup,
        IFrameViewCsvReader reader,
        ICaptureAnalysisService analysis)
    {
        var exported = new List<LibraryRecord>();
        var analyzed = 0;
        var excluded = 0;
        foreach (var identity in library.Records.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            var record = library.Records[identity];
            if (!record.StatsSummary.ContainsKey("avg_fps")
                && record.Available
                && File.Exists(record.SourcePath))
            {
                // Hydrate the digest so the record can satisfy the package
                // validation requirements.
                try
                {
                    var capture = await reader.LoadCaptureAsync(record.SourcePath).ConfigureAwait(false);
                    var session = analysis.Analyze(capture);
                    var currentIdentity = CaptureIdentityResolver.TryBuild(record.SourcePath);
                    if (currentIdentity == record.Identity)
                    {
                        LibraryUpdater.UpdateStats(library, session, identity);
                        record = library.Records[identity];
                        analyzed++;
                    }
                }
                catch (Exception error) when (error is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException
                    or FormatException)
                {
                    // Hydration failure: the record cannot be made exportable.
                }
            }

            var required = record.SourceName.Length > 0
                && record.Game.Length > 0
                && record.Resolution.Length > 0
                && record.StatsSummary.ContainsKey("avg_fps");
            if (!required)
            {
                excluded++;
                continue;
            }

            exported.Add(record);
        }

        var package = ExportReport.BuildBenchmarkPackage(library, manualLookup, exported);
        return new PackageExportResult(package, exported.Count, analyzed, excluded);
    }

    private static void WriteAtomically(string path, Action<string> write)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        try
        {
            write(temporary);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private static string Field(JsonElement? element, string name) =>
        element is { } value
        && value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var field)
        && field.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? field.ToString().Trim()
            : string.Empty;

    private static JsonElement? Object(JsonElement? parent, string name) =>
        parent is { } value
        && value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var child)
        && child.ValueKind == JsonValueKind.Object
            ? child
            : null;

    private static double? Number(JsonElement? element, string name) =>
        element is { } value
        && value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.Number
        && field.TryGetDouble(out var number)
            ? number
            : null;

    private static IReadOnlyDictionary<string, double> ParseStats(JsonElement? element)
    {
        var stats = new Dictionary<string, double>(StringComparer.Ordinal);
        if (element is { } value)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetDouble(out var number))
                {
                    stats[property.Name] = number;
                }
            }
        }

        return stats;
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(JsonElement? element)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element is { } value)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                    or JsonValueKind.True or JsonValueKind.False)
                {
                    options[property.Name] = property.Value.ToString();
                }
            }
        }

        return options;
    }

    private static ManualMetadata? ParseManualMetadata(JsonElement capture)
    {
        if (!capture.TryGetProperty("manual_metadata", out var raw)
            || raw.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var tags = new List<string>();
        if (raw.TryGetProperty("tags", out var tagsElement)
            && tagsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagsElement.EnumerateArray())
            {
                if (tag.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var trimmed = tag.GetString()!.Trim();
                if (trimmed.Length > 0)
                {
                    tags.Add(trimmed);
                }
            }
        }

        return new ManualMetadata(
            BenchmarkName: Field(raw, "benchmark_name"),
            Game: Field(raw, "game"),
            Resolution: Field(raw, "resolution"),
            GraphicsPreset: Field(raw, "graphics_preset"),
            Upscaler: Field(raw, "upscaler"),
            UpscalerQuality: Field(raw, "upscaler_quality"),
            FrameGeneration: Field(raw, "frame_generation"),
            RayTracing: Field(raw, "ray_tracing"),
            DriverVersion: Field(raw, "driver_version"),
            Notes: Field(raw, "notes"),
            Tags: tags);
    }
}
