using System.Text.Json;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Infrastructure.Stores;

/// <summary>Manual benchmark metadata keyed by stable capture identity.</summary>
public interface IManualMetadataStore
{
    ManualMetadata? Get(string identity);

    /// <summary>Null or empty metadata removes the entry; persists immediately.</summary>
    void Set(string identity, ManualMetadata? metadata);

    IReadOnlyDictionary<string, ManualMetadata> Load();

    void Save(IReadOnlyDictionary<string, ManualMetadata> entries);
}

/// <summary>
/// Versioned JSON metadata store at
/// %APPDATA%\FrameViewAnalyzer\V2\metadata.json, mirroring the Python
/// reference: tolerant reads (missing/malformed/unknown version → empty),
/// atomic writes (temp file + replace), and empty entries dropped on save.
/// </summary>
public sealed class JsonManualMetadataStore : IManualMetadataStore
{
    public const int StoreFormatVersion = 1;

    private static readonly string[] StringFields =
    [
        "benchmark_name",
        "game",
        "resolution",
        "graphics_preset",
        "upscaler",
        "upscaler_quality",
        "frame_generation",
        "ray_tracing",
        "driver_version",
        "notes",
    ];

    private readonly string _path;
    private readonly Dictionary<string, ManualMetadata> _entries;

    public JsonManualMetadataStore(string? path = null)
    {
        _path = path ?? DefaultMetadataPath();
        _entries = new Dictionary<string, ManualMetadata>(Load(), StringComparer.Ordinal);
    }

    public static string DefaultMetadataPath() =>
        Path.Combine(JsonSettingsStore.DefaultAppDataRoot(), "metadata.json");

    public ManualMetadata? Get(string identity) =>
        _entries.TryGetValue(identity, out var metadata) ? metadata : null;

    public void Set(string identity, ManualMetadata? metadata)
    {
        if (metadata is null || metadata.IsEmpty)
        {
            _entries.Remove(identity);
        }
        else
        {
            _entries[identity] = metadata;
        }

        Save(_entries);
    }

    public IReadOnlyDictionary<string, ManualMetadata> Load()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.GetProperty("format_version").GetInt32() != StoreFormatVersion
                || !root.TryGetProperty("entries", out var entries)
                || entries.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, ManualMetadata>();
            }

            var result = new Dictionary<string, ManualMetadata>(StringComparer.Ordinal);
            foreach (var property in entries.EnumerateObject())
            {
                var identity = property.Name;
                var raw = property.Value;
                var metadata = raw.ValueKind == JsonValueKind.Object
                    && raw.TryGetProperty("metadata", out var payload)
                    ? ParseMetadata(payload)
                    : new ManualMetadata();
                if (!string.IsNullOrEmpty(identity) && !metadata.IsEmpty)
                {
                    result[identity] = metadata;
                }
            }

            return result;
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            return new Dictionary<string, ManualMetadata>();
        }
    }

    public void Save(IReadOnlyDictionary<string, ManualMetadata> entries)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format_version", StoreFormatVersion);
            writer.WriteStartObject("entries");
            foreach (var (identity, metadata) in entries
                         .Where(pair => !pair.Value.IsEmpty)
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(identity);
                writer.WriteStartObject();
                writer.WritePropertyName("metadata");
                WriteMetadata(writer, metadata);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        var temporary = _path + ".tmp";
        File.WriteAllBytes(temporary, stream.ToArray());
        File.Move(temporary, _path, overwrite: true);
    }

    private static ManualMetadata ParseMetadata(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
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

    private static void WriteMetadata(Utf8JsonWriter writer, ManualMetadata metadata)
    {
        var values = new Dictionary<string, string>
        {
            ["benchmark_name"] = metadata.BenchmarkName,
            ["game"] = metadata.Game,
            ["resolution"] = metadata.Resolution,
            ["graphics_preset"] = metadata.GraphicsPreset,
            ["upscaler"] = metadata.Upscaler,
            ["upscaler_quality"] = metadata.UpscalerQuality,
            ["frame_generation"] = metadata.FrameGeneration,
            ["ray_tracing"] = metadata.RayTracing,
            ["driver_version"] = metadata.DriverVersion,
            ["notes"] = metadata.Notes,
        };

        writer.WriteStartObject();
        foreach (var field in StringFields)
        {
            writer.WriteString(field, values[field]);
        }

        writer.WriteStartArray("tags");
        foreach (var tag in metadata.Tags)
        {
            writer.WriteStringValue(tag);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
