using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Analytics.Library;

/// <summary>
/// Pure search / filter / sort / display helpers for the library browser,
/// ported from the Python reference (case-insensitive, Unicode-aware).
/// </summary>
public static class LibrarySearch
{
    /// <summary>All searchable text of a record, including manual metadata.</summary>
    public static string RecordSearchText(LibraryRecord record, ManualMetadata? manual = null)
    {
        var parts = new List<string>
        {
            record.Game,
            record.SourceName,
            record.Resolution,
            record.Gpu,
            record.Cpu,
        };
        if (manual is not null)
        {
            parts.AddRange(
            [
                manual.BenchmarkName,
                manual.Game,
                manual.GraphicsPreset,
                manual.Upscaler,
                manual.UpscalerQuality,
                manual.FrameGeneration,
                manual.RayTracing,
                manual.DriverVersion,
                .. manual.Tags,
            ]);
        }

        return string.Join("\n", parts.Where(part => part.Length > 0));
    }

    /// <summary>Case-insensitive substring search over the record text.</summary>
    public static IReadOnlyList<LibraryRecord> SearchRecords(
        IEnumerable<LibraryRecord> records,
        string query,
        IReadOnlyDictionary<string, ManualMetadata>? manualLookup = null)
    {
        var needle = query.Trim();
        var recordsList = records.ToList();
        if (needle.Length == 0)
        {
            return recordsList;
        }

        manualLookup ??= new Dictionary<string, ManualMetadata>();
        var matches = new List<LibraryRecord>();
        foreach (var record in recordsList)
        {
            var text = RecordSearchText(
                record,
                manualLookup.TryGetValue(record.Identity, out var manual) ? manual : null);
            if (text.Contains(needle, StringComparison.CurrentCultureIgnoreCase))
            {
                matches.Add(record);
            }
        }

        return matches;
    }

    /// <summary>AND-style filters; empty filters match everything.</summary>
    public static IReadOnlyList<LibraryRecord> FilterRecords(
        IEnumerable<LibraryRecord> records,
        IReadOnlyDictionary<string, ManualMetadata>? manualLookup = null,
        IReadOnlyCollection<string>? tags = null,
        string? resolution = null,
        string? gpu = null,
        string? game = null)
    {
        manualLookup ??= new Dictionary<string, ManualMetadata>();
        var wantedTags = (tags ?? [])
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Select(tag => tag.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var resolutionNeedle = (resolution ?? string.Empty).Trim().ToLowerInvariant();
        var gpuNeedle = (gpu ?? string.Empty).Trim().ToLowerInvariant();
        var gameNeedle = (game ?? string.Empty).Trim().ToLowerInvariant();

        var result = new List<LibraryRecord>();
        foreach (var record in records)
        {
            var manual = manualLookup.TryGetValue(record.Identity, out var value) ? value : null;
            if (wantedTags.Count > 0)
            {
                var manualTags = (manual?.Tags ?? [])
                    .Select(tag => tag.ToLowerInvariant())
                    .ToHashSet(StringComparer.Ordinal);
                if (!wantedTags.IsSubsetOf(manualTags))
                {
                    continue;
                }
            }

            if (resolutionNeedle.Length > 0
                && !record.Resolution.ToLowerInvariant().Contains(resolutionNeedle, StringComparison.Ordinal))
            {
                continue;
            }

            if (gpuNeedle.Length > 0
                && !record.Gpu.ToLowerInvariant().Contains(gpuNeedle, StringComparison.Ordinal))
            {
                continue;
            }

            if (gameNeedle.Length > 0
                && !LibraryGame(record, manual).ToLowerInvariant().Contains(gameNeedle, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(record);
        }

        return result;
    }

    /// <summary>Sort by last-seen date (newest first) or benchmark name (stable).</summary>
    public static IReadOnlyList<LibraryRecord> SortRecords(
        IEnumerable<LibraryRecord> records,
        string key = LibraryConstants.SortDate,
        bool? descending = null)
    {
        var ordered = key == LibraryConstants.SortName
            ? records.OrderBy(record => record.Game, StringComparer.CurrentCultureIgnoreCase).ToList()
            : records.OrderBy(record => record.LastSeenAt, StringComparer.Ordinal).ToList();

        if (key == LibraryConstants.SortName)
        {
            if (descending is true)
            {
                ordered.Reverse();
            }

            return ordered;
        }

        if (descending is not false)
        {
            ordered.Reverse();
        }

        return ordered;
    }

    /// <summary>Effective game/scene label: manual scene first, detected game after.</summary>
    public static string LibraryGame(LibraryRecord record, ManualMetadata? manual = null) =>
        (manual is { Game.Length: > 0 } ? manual.Game : string.Empty) is { Length: > 0 } manualGame
            ? manualGame
            : record.Game;

    /// <summary>Display title for a library row, preferring manual context.</summary>
    public static string LibraryRowTitle(LibraryRecord record, ManualMetadata? manual = null)
    {
        if (manual is not null)
        {
            return manual.BenchmarkName.Length > 0
                ? manual.BenchmarkName
                : (manual.Game.Length > 0 ? manual.Game : (record.Game.Length > 0 ? record.Game : record.SourceName));
        }

        return record.Game.Length > 0 ? record.Game : record.SourceName;
    }

    /// <summary>Compact subtitle: manual configuration plus detected context.</summary>
    public static string LibraryRowSubtitle(LibraryRecord record, ManualMetadata? manual = null)
    {
        var parts = new List<string>();
        if (manual is not null && manual.ConfigLine is { } config)
        {
            parts.Add(config);
        }

        foreach (var value in new[] { record.Resolution, record.Gpu, record.Cpu })
        {
            if (value.Length > 0 && value != "--")
            {
                parts.Add(value);
            }
        }

        if (record.DurationSeconds is { } seconds)
        {
            parts.Add(FormatCompactDuration(seconds));
        }

        return parts.Count > 0 ? string.Join("  ·  ", parts) : "No data";
    }

    /// <summary>Compact capture time from the FrameView file name, or empty.</summary>
    public static string LibraryStamp(LibraryRecord record) =>
        CaptureFileNaming.TryParseCaptureStamp(record.SourceName, out var stamp)
            ? CaptureFileNaming.FormatStamp(stamp)
            : string.Empty;

    /// <summary>
    /// Human-sized duration for compact Library rows. Once hours are present,
    /// seconds are intentionally omitted so long captures stay easy to scan.
    /// </summary>
    internal static string FormatCompactDuration(double seconds)
    {
        var totalSeconds = Math.Max(0L, (long)Math.Round(seconds));
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var remainingSeconds = totalSeconds % 60;

        if (hours > 0)
        {
            return minutes > 0 ? $"{hours}h {minutes}min" : $"{hours}h";
        }

        if (minutes > 0)
        {
            return remainingSeconds > 0
                ? $"{minutes}min {remainingSeconds}s"
                : $"{minutes}min";
        }

        return $"{remainingSeconds}s";
    }
}
