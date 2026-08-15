using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;

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

public sealed record PackageImportResult(
    int Imported,
    int Skipped,
    IReadOnlyDictionary<string, ManualMetadata> ManualMetadataByIdentity);

/// <summary>File-writing half of the export system (CSV, JSON, packages).</summary>
public interface IExportService
{
    int WriteStatisticsCsv(string path, IReadOnlyList<ComparisonRow> rows);

    void WriteStatisticsJson(string path, ExportStatisticsDto document);

    void WriteBenchmarkPackage(string path, BenchmarkPackageDto package);

    PackageValidationResult ValidateBenchmarkPackage(string json);

    PackageImportResult ImportBenchmarkPackage(LibraryModel library, string json);
}

/// <summary>
/// Writes the export formats. Statistics CSV is a pure table (UTF-8 BOM);
/// Benchmark JSON embeds the structured sessions and manual metadata; the
/// portable benchmark package never embeds raw CSV contents.
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

    public int WriteStatisticsCsv(string path, IReadOnlyList<ComparisonRow> rows)
    {
        CreateDirectory(path);
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
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

        return rows.Count;
    }

    public void WriteStatisticsJson(string path, ExportStatisticsDto document)
    {
        CreateDirectory(path);
        var json = JsonSerializer.Serialize(document, JsonExportOptions);
        File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
    }

    public void WriteBenchmarkPackage(string path, BenchmarkPackageDto package)
    {
        CreateDirectory(path);
        var json = JsonSerializer.Serialize(package, JsonExportOptions);
        File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
    }

    private static readonly JsonSerializerOptions JsonExportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static void CreateDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

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

    public PackageImportResult ImportBenchmarkPackage(LibraryModel library, string json)
    {
        var validation = ValidateBenchmarkPackage(json);
        var imported = new Dictionary<string, ManualMetadata>(StringComparer.Ordinal);
        var importedCount = 0;
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
            importedCount++;

            if (capture.ManualMetadata is { IsEmpty: false } manual)
            {
                imported[identity] = manual;
            }
        }

        return new PackageImportResult(importedCount, validation.Errors.Count, imported);
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
