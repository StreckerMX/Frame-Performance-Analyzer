using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Library;

/// <summary>
/// In-memory Benchmark Library index: records keyed by stable capture
/// identity plus the recent comparison pairs. Persistence lives in
/// Infrastructure; this model stays free of file IO.
/// </summary>
public sealed class LibraryModel
{
    public int FormatVersion { get; init; } = LibraryConstants.FormatVersion;

    public Dictionary<string, LibraryRecord> Records { get; } = new(StringComparer.Ordinal);

    public List<(string Base, string Comparison)> RecentComparisons { get; } = [];
}
