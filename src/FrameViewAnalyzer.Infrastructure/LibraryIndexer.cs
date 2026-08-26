using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Infrastructure;

/// <summary>
/// File-system half of the Benchmark Library: stable identities, availability
/// resolution, and folder refresh. Pure record math lives in Analytics.
/// </summary>
public sealed class LibraryIndexer
{
    /// <summary>Stable identity for a capture path, or null when unreadable.</summary>
    public static string? IdentityOf(CaptureInfo capture) =>
        CaptureIdentityResolver.TryBuild(capture.Path);

    /// <summary>Insert or merge one capture; returns false when unidentifiable or ignored.</summary>
    public bool Upsert(LibraryModel library, CaptureInfo capture, string now)
    {
        var identity = IdentityOf(capture);
        if (identity is null || library.IgnoredIdentities.Contains(identity))
        {
            return false;
        }

        LibraryUpdater.Upsert(library, capture, identity, now);
        return true;
    }

    /// <summary>Find a capture by identity inside one directory (move tolerance).</summary>
    public static string? LocateIdentity(string directory, string identity)
    {
        foreach (var path in CaptureFolderScanner.DiscoverLogFiles(directory))
        {
            if (CaptureIdentityResolver.TryBuild(path) == identity)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Whether the capture source still exists (recorded path or directory).</summary>
    public static bool ResolveAvailability(LibraryRecord record, string? activeDirectory = null)
    {
        if (File.Exists(record.SourcePath)
            && CaptureIdentityResolver.TryBuild(record.SourcePath) == record.Identity)
        {
            return true;
        }

        return activeDirectory is not null
            && LocateIdentity(activeDirectory, record.Identity) is not null;
    }

    /// <summary>
    /// Scan a folder and update the library; records that no longer resolve
    /// anywhere stay visible but are marked missing. User-hidden identities
    /// are excluded from refresh even when their source CSV still exists.
    /// </summary>
    public async Task RefreshAsync(
        LibraryModel library,
        string directory,
        CaptureFolderScanner scanner,
        CancellationToken cancellationToken = default)
    {
        foreach (var identity in library.IgnoredIdentities)
        {
            library.Records.Remove(identity);
        }

        library.RecentComparisons.RemoveAll(pair =>
            library.IgnoredIdentities.Contains(pair.Base)
            || library.IgnoredIdentities.Contains(pair.Comparison));

        var now = LibraryUpdater.NowIso();
        var infos = await scanner.ScanCaptureFolderAsync(directory, cancellationToken).ConfigureAwait(false);
        var foundIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var info in infos)
        {
            var identity = IdentityOf(info);
            if (identity is null || library.IgnoredIdentities.Contains(identity))
            {
                continue;
            }

            if (Upsert(library, info, now))
            {
                foundIdentities.Add(identity);
            }
        }

        // The folder was already enumerated completely above. Re-running
        // LocateIdentity once per stored record turns refresh into O(N²) file
        // enumeration on large libraries. A moved file inside the active
        // directory is already represented by foundIdentities, while a source
        // outside that directory only needs its recorded path checked once.
        foreach (var identity in library.Records.Keys.ToList())
        {
            var record = library.Records[identity];
            var available = foundIdentities.Contains(identity)
                || ResolveAvailability(record);
            if (available != record.Available)
            {
                library.Records[identity] = record with { Available = available };
            }
        }
    }
}
