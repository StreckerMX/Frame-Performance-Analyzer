using System.Globalization;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Library;

/// <summary>
/// Pure library mutations: record creation/merge, recent comparison history,
/// and the small FPS stats digest cached from an analyzed session.
/// </summary>
public static class LibraryUpdater
{
    public static string NowIso() =>
        DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    public static LibraryRecord NewRecord(CaptureInfo capture, string identity, string now) =>
        new(
            Identity: identity,
            SourcePath: capture.Path,
            SourceName: capture.Name,
            Game: capture.Application,
            Resolution: capture.Resolution,
            Gpu: capture.Gpu,
            Cpu: capture.Cpu,
            DurationSeconds: capture.DurationSeconds,
            AddedAt: now,
            LastSeenAt: now,
            Available: true);

    /// <summary>
    /// Merge freshly scanned context into an existing record without erasing
    /// richer information: null, empty, whitespace, and "--" values never
    /// overwrite useful existing fields, while valid newly discovered values
    /// replace missing or placeholder ones. AddedAt and the statistics
    /// digest are preserved unless explicitly recalculated.
    /// </summary>
    public static LibraryRecord Merge(
        LibraryRecord existing,
        CaptureInfo capture,
        string now) =>
        existing with
        {
            SourcePath = Keep(existing.SourcePath, capture.Path),
            SourceName = Keep(existing.SourceName, capture.Name),
            Game = Keep(existing.Game, capture.Application),
            Resolution = Keep(existing.Resolution, capture.Resolution),
            Gpu = Keep(existing.Gpu, capture.Gpu),
            Cpu = Keep(existing.Cpu, capture.Cpu),
            DurationSeconds = capture.DurationSeconds ?? existing.DurationSeconds,
            LastSeenAt = now,
            Available = true,
        };

    private static string Keep(string existing, string incoming) =>
        string.IsNullOrWhiteSpace(incoming) || incoming == "--" ? existing : incoming;

    /// <summary>Insert or merge one capture; returns the current record.</summary>
    public static LibraryRecord Upsert(
        LibraryModel library,
        CaptureInfo capture,
        string identity,
        string now)
    {
        var record = library.Records.TryGetValue(identity, out var existing)
            ? Merge(existing, capture, now)
            : NewRecord(capture, identity, now);
        library.Records[identity] = record;
        return record;
    }

    /// <summary>
    /// Remember a comparison pair, newest first, deduplicated and capped.
    /// Returns a new list (the caller owns the library's list).
    /// </summary>
    public static List<(string Base, string Comparison)> WithComparison(
        IReadOnlyList<(string Base, string Comparison)> recent,
        string identityA,
        string identityB)
    {
        var pair = (identityA, identityB);
        var updated = recent.Where(existing => existing != pair).ToList();
        updated.Insert(0, pair);
        if (updated.Count > LibraryConstants.RecentComparisonLimit)
        {
            updated.RemoveRange(
                LibraryConstants.RecentComparisonLimit,
                updated.Count - LibraryConstants.RecentComparisonLimit);
        }

        return updated;
    }

    /// <summary>
    /// Cache the average / 1% low / 0.1% low FPS digest for a record from an
    /// analyzed session. The library never re-analyzes files during scans.
    /// </summary>
    public static void UpdateStats(
        LibraryModel library,
        SessionAnalysis session,
        string identity)
    {
        if (!library.Records.TryGetValue(identity, out var record))
        {
            return;
        }

        var fps = SeriesBuilder.Build(session, "fps").Y;
        var stats = StatisticsCalculator.Compute(CoreMetricCatalog.CoreById["fps"], fps);
        var summary = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (key, value) in new[] { ("avg_fps", stats.Avg), ("p1_fps", stats.P1), ("p01_fps", stats.P01) })
        {
            if (value is not null)
            {
                summary[key] = value.Value;
            }
        }

        library.Records[identity] = record with { StatsSummary = summary };
    }
}
