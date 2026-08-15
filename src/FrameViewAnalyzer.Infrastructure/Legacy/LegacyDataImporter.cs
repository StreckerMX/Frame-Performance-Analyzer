using System.Text.Json;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.Infrastructure.Legacy;

public enum LegacySettingsOutcome
{
    NoLegacy,
    Imported,
    SkippedV2Exists,
    Malformed,
}

/// <summary>Result of a one-way legacy import, with a user-facing summary.</summary>
public sealed record LegacyImportResult(
    LegacySettingsOutcome Settings,
    int MetadataImported,
    int MetadataAlreadyPresent,
    int LibraryImported,
    int LibraryAlreadyPresent,
    int RecentComparisonsImported,
    int MalformedStores)
{
    public string Summary()
    {
        var settings = Settings switch
        {
            LegacySettingsOutcome.NoLegacy => "no legacy file",
            LegacySettingsOutcome.Imported => "imported",
            LegacySettingsOutcome.SkippedV2Exists => "skipped (V2 already configured)",
            _ => "skipped (malformed)",
        };
        return string.Join(
            Environment.NewLine,
            [
                "Legacy import complete",
                $"Settings: {settings}",
                $"Metadata: {MetadataImported} imported, {MetadataAlreadyPresent} already present",
                $"Library: {LibraryImported} imported, {LibraryAlreadyPresent} already present",
                $"Recent comparisons: {RecentComparisonsImported} imported",
                $"Malformed stores: {MalformedStores}",
            ]);
    }
}

/// <summary>One-way, read-only migration from the Python application's stores.</summary>
public interface ILegacyDataImporter
{
    LegacyImportResult Import();
}

/// <summary>
/// Imports the legacy Python stores (%APPDATA%\FrameViewAnalyzer\settings.json,
/// metadata.json, library.json) into the V2 stores. Legacy files are NEVER
/// written, deleted, renamed, or migrated in place; existing V2 data always
/// wins; repeated runs are idempotent; each store is imported independently
/// so a malformed store never aborts the others.
/// </summary>
public sealed class LegacyDataImporter : ILegacyDataImporter
{
    public const int LegacyFormatVersion = 1;

    private readonly ISettingsStore _settingsStore;
    private readonly IManualMetadataStore _metadataStore;
    private readonly ILibraryStore _libraryStore;
    private readonly string _legacyRoot;
    private readonly string _v2SettingsPath;

    public LegacyDataImporter(
        ISettingsStore settingsStore,
        IManualMetadataStore metadataStore,
        ILibraryStore libraryStore,
        string? legacyRoot = null,
        string? v2SettingsPath = null)
    {
        _settingsStore = settingsStore;
        _metadataStore = metadataStore;
        _libraryStore = libraryStore;
        _legacyRoot = legacyRoot ?? DefaultLegacyRoot();
        _v2SettingsPath = v2SettingsPath ?? JsonSettingsStore.DefaultSettingsPath();
    }

    public static string DefaultLegacyRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FrameViewAnalyzer");

    public static string LegacySettingsPath(string root) => Path.Combine(root, "settings.json");

    public static string LegacyMetadataPath(string root) => Path.Combine(root, "metadata.json");

    public static string LegacyLibraryPath(string root) => Path.Combine(root, "library.json");

    public LegacyImportResult Import()
    {
        var malformed = 0;

        var settings = ImportSettings();
        if (settings == LegacySettingsOutcome.Malformed)
        {
            malformed++;
        }

        var (metadataImported, metadataPresent, metadataMalformed) = ImportMetadata();
        if (metadataMalformed)
        {
            malformed++;
        }

        var (libraryImported, libraryPresent, recentImported, libraryMalformed) = ImportLibrary();
        if (libraryMalformed)
        {
            malformed++;
        }

        return new LegacyImportResult(
            settings,
            metadataImported,
            metadataPresent,
            libraryImported,
            libraryPresent,
            recentImported,
            malformed);
    }

    private LegacySettingsOutcome ImportSettings()
    {
        // Existing V2 settings always win; the legacy file is only read when
        // the V2 store does not exist yet.
        if (File.Exists(_v2SettingsPath))
        {
            return LegacySettingsOutcome.SkippedV2Exists;
        }

        var legacyPath = LegacySettingsPath(_legacyRoot);
        if (!File.Exists(legacyPath))
        {
            return LegacySettingsOutcome.NoLegacy;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(legacyPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return LegacySettingsOutcome.Malformed;
            }

            var captureDirectory = root.TryGetProperty("capture_directory", out var directory)
                && directory.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(directory.GetString())
                    ? directory.GetString()!.Trim()
                    : null;
            var appearance = root.TryGetProperty("appearance_mode", out var mode)
                && mode.ValueKind == JsonValueKind.String
                && mode.GetString() is "dark" or "light"
                    ? mode.GetString()!
                    : "dark";

            _settingsStore.Save(new SettingsDocument(
                FormatVersion: 1,
                CaptureDirectory: captureDirectory,
                AppearanceMode: appearance,
                Window: null));
            return LegacySettingsOutcome.Imported;
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            return LegacySettingsOutcome.Malformed;
        }
    }

    private (int Imported, int AlreadyPresent, bool Malformed) ImportMetadata()
    {
        var legacyPath = LegacyMetadataPath(_legacyRoot);
        if (!File.Exists(legacyPath))
        {
            return (0, 0, false);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(legacyPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format_version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != LegacyFormatVersion
                || !root.TryGetProperty("entries", out var entries)
                || entries.ValueKind != JsonValueKind.Object)
            {
                return (0, 0, true);
            }

            var existing = _metadataStore.Load();
            var merged = new Dictionary<string, ManualMetadata>(existing, StringComparer.Ordinal);
            var imported = 0;
            var alreadyPresent = 0;
            foreach (var property in entries.EnumerateObject())
            {
                var identity = property.Name;
                if (identity.Length == 0 || existing.ContainsKey(identity))
                {
                    if (existing.ContainsKey(identity))
                    {
                        alreadyPresent++;
                    }

                    continue;
                }

                var metadata = ParseManualMetadata(property.Value);
                if (metadata.IsEmpty)
                {
                    continue;
                }

                merged[identity] = metadata;
                imported++;
            }

            if (imported > 0)
            {
                _metadataStore.Save(merged);
            }

            return (imported, alreadyPresent, false);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            return (0, 0, true);
        }
    }

    private (int Imported, int AlreadyPresent, int RecentImported, bool Malformed) ImportLibrary()
    {
        var legacyPath = LegacyLibraryPath(_legacyRoot);
        if (!File.Exists(legacyPath))
        {
            return (0, 0, 0, false);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(legacyPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format_version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != LegacyFormatVersion
                || !root.TryGetProperty("records", out var records)
                || records.ValueKind != JsonValueKind.Object)
            {
                return (0, 0, 0, true);
            }

            var library = _libraryStore.Load();
            var imported = 0;
            var alreadyPresent = 0;
            if (records.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in records.EnumerateObject())
                {
                    if (property.Name.Length == 0)
                    {
                        continue;
                    }

                    if (library.Records.ContainsKey(property.Name))
                    {
                        alreadyPresent++;
                        continue;
                    }

                    var record = ParseLibraryRecord(property.Name, property.Value);
                    if (record is null)
                    {
                        continue;
                    }

                    library.Records[property.Name] = record;
                    imported++;
                }
            }

            var recentImported = 0;
            if (root.TryGetProperty("recent_comparisons", out var recent)
                && recent.ValueKind == JsonValueKind.Array)
            {
                foreach (var pair in recent.EnumerateArray())
                {
                    if (pair.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var values = pair.EnumerateArray()
                        .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty)
                        .ToList();
                    if (values.Count != 2 || values.Any(string.IsNullOrEmpty))
                    {
                        continue;
                    }

                    var candidate = (values[0]!, values[1]!);
                    if (library.RecentComparisons.Contains(candidate))
                    {
                        continue;
                    }

                    library.RecentComparisons.Add(candidate);
                    recentImported++;
                }

                while (library.RecentComparisons.Count > LibraryConstants.RecentComparisonLimit)
                {
                    library.RecentComparisons.RemoveAt(library.RecentComparisons.Count - 1);
                }

                recentImported = System.Math.Min(recentImported, library.RecentComparisons.Count);
            }

            if (imported > 0 || recentImported > 0)
            {
                _libraryStore.Save(library);
            }

            return (imported, alreadyPresent, recentImported, false);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            return (0, 0, 0, true);
        }
    }

    /// <summary>Legacy metadata entries share the V2 shape ({"metadata": {...}}).</summary>
    private static ManualMetadata ParseManualMetadata(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty("metadata", out var payload)
            || payload.ValueKind != JsonValueKind.Object)
        {
            return new ManualMetadata();
        }

        string Field(string name) => payload.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                ? value.ToString().Trim()
                : string.Empty;

        var tags = new List<string>();
        if (payload.TryGetProperty("tags", out var tagsElement)
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
            BenchmarkName: Field("benchmark_name"),
            Game: Field("game"),
            Resolution: Field("resolution"),
            GraphicsPreset: Field("graphics_preset"),
            Upscaler: Field("upscaler"),
            UpscalerQuality: Field("upscaler_quality"),
            FrameGeneration: Field("frame_generation"),
            RayTracing: Field("ray_tracing"),
            DriverVersion: Field("driver_version"),
            Notes: Field("notes"),
            Tags: tags);
    }

    private static LibraryRecord? ParseLibraryRecord(string identity, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            string Field(string name) => payload.TryGetProperty(name, out var value)
                && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                    ? value.ToString().Trim()
                    : string.Empty;

            var stats = new Dictionary<string, double>(StringComparer.Ordinal);
            if (payload.TryGetProperty("stats_summary", out var rawStats)
                && rawStats.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in rawStats.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetDouble(out var number))
                    {
                        stats[property.Name] = number;
                    }
                }
            }

            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            if (payload.TryGetProperty("analysis_options", out var rawOptions)
                && rawOptions.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in rawOptions.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                        or JsonValueKind.True or JsonValueKind.False)
                    {
                        options[property.Name] = property.Value.ToString();
                    }
                }
            }

            return new LibraryRecord(
                Identity: identity,
                SourcePath: Field("source_path"),
                SourceName: Field("source_name"),
                Game: Field("game"),
                Resolution: Field("resolution"),
                Gpu: Field("gpu"),
                Cpu: Field("cpu"),
                DurationSeconds: payload.TryGetProperty("duration_seconds", out var duration)
                    && duration.ValueKind == JsonValueKind.Number
                    && duration.TryGetDouble(out var seconds)
                        ? seconds
                        : null,
                AddedAt: Field("added_at"),
                LastSeenAt: Field("last_seen_at"),
                Available: !payload.TryGetProperty("available", out var available)
                    || available.ValueKind != JsonValueKind.False,
                StatsSummary: stats,
                AnalysisOptions: options);
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
