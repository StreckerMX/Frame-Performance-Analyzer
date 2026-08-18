using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Library;

/// <summary>
/// In-memory Benchmark Library index: records keyed by stable capture
/// identity plus recent comparison pairs and identities explicitly removed
/// by the user. Persistence lives in Infrastructure; this model stays free
/// of file IO.
/// </summary>
public sealed class LibraryModel
{
    public int FormatVersion { get; init; } = LibraryConstants.FormatVersion;

    public Dictionary<string, LibraryRecord> Records { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Stable capture identities hidden by the user. Folder refresh must not
    /// re-add these records while the source CSV still exists.
    /// </summary>
    public HashSet<string> IgnoredIdentities { get; } = new(StringComparer.Ordinal);

    public List<(string Base, string Comparison)> RecentComparisons { get; } = [];
}
