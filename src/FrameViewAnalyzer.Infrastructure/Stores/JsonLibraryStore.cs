using System.Text.Json;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Infrastructure.Stores;

/// <summary>Persistence for the Benchmark Library index.</summary>
public interface ILibraryStore
{
    LibraryModel Load();

    void Save(LibraryModel library);
}

/// <summary>
/// Versioned v1 JSON library store at
/// %APPDATA%\FrameViewAnalyzer\V2\library.json. Missing/malformed files load
/// as empty; a store with an unknown format_version loads as empty and is
/// never overwritten (saving throws instead of downgrading it). Writes are
/// atomic (temp file + replace).
/// </summary>
public sealed class JsonLibraryStore : ILibraryStore
{
    private readonly string _path;

    public JsonLibraryStore(string? path = null) => _path = path ?? DefaultLibraryPath();

    public static string DefaultLibraryPath() =>
        Path.Combine(JsonSettingsStore.DefaultAppDataRoot(), "library.json");

    public LibraryModel Load()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format_version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != LibraryConstants.FormatVersion)
            {
                return new LibraryModel();
            }

            var library = new LibraryModel();
            if (root.TryGetProperty("records", out var records)
                && records.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in records.EnumerateObject())
                {
                    var record = ParseRecord(property.Value);
                    if (record is not null && record.Identity == property.Name)
                    {
                        library.Records[record.Identity] = record;
                    }
                }
            }

            if (root.TryGetProperty("recent_comparisons", out var recent)
                && recent.ValueKind == JsonValueKind.Array)
            {
                foreach (var pair in recent.EnumerateArray())
                {
                    if (pair.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var values = pair.EnumerateArray().Select(item => item.GetString()).ToList();
                    if (values.Count == 2 && values.All(value => !string.IsNullOrEmpty(value)))
                    {
                        library.RecentComparisons.Add((values[0]!, values[1]!));
                    }
                }
            }

            return library;
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            return new LibraryModel();
        }
    }

    public void Save(LibraryModel library)
    {
        var existingVersion = TryReadStoreVersion();
        if (existingVersion is { } version && version != LibraryConstants.FormatVersion)
        {
            throw new InvalidOperationException(
                $"The library store at '{_path}' uses format version {version}, "
                + "which this version of the application does not support. "
                + "The file was left unchanged.");
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format_version", library.FormatVersion);
            writer.WriteStartObject("records");
            foreach (var (identity, record) in library.Records
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(identity);
                WriteRecord(writer, record);
            }

            writer.WriteEndObject();
            writer.WriteStartArray("recent_comparisons");
            foreach (var (first, second) in library.RecentComparisons)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(first);
                writer.WriteStringValue(second);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        var temporary = _path + ".tmp";
        File.WriteAllBytes(temporary, stream.ToArray());
        File.Move(temporary, _path, overwrite: true);
    }

    private int? TryReadStoreVersion()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("format_version", out var version)
                && version.ValueKind == JsonValueKind.Number
                    ? version.GetInt32()
                    : null;
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            return null;
        }
    }

    private static LibraryRecord? ParseRecord(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("identity", out var identityElement)
            || identityElement.ValueKind != JsonValueKind.String
            || identityElement.GetString() is not { Length: > 0 } identity)
        {
            return null;
        }

        try
        {
            string Field(string name) => payload.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
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
                StatsSummary: stats);
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static void WriteRecord(Utf8JsonWriter writer, LibraryRecord record)
    {
        writer.WriteStartObject();
        writer.WriteString("identity", record.Identity);
        writer.WriteString("source_path", record.SourcePath);
        writer.WriteString("source_name", record.SourceName);
        writer.WriteString("game", record.Game);
        writer.WriteString("resolution", record.Resolution);
        writer.WriteString("gpu", record.Gpu);
        writer.WriteString("cpu", record.Cpu);
        if (record.DurationSeconds is { } seconds)
        {
            writer.WriteNumber("duration_seconds", seconds);
        }
        else
        {
            writer.WriteNull("duration_seconds");
        }

        writer.WriteString("added_at", record.AddedAt);
        writer.WriteString("last_seen_at", record.LastSeenAt);
        writer.WriteBoolean("available", record.Available);
        writer.WriteStartObject("stats_summary");
        foreach (var (key, value) in record.StatsSummary.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteNumber(key, value);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
