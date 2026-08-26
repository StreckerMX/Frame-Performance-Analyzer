using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;
using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.Infrastructure;

/// <summary>
/// Discovers and describes supported detailed performance logs stored in a
/// folder. FrameView *_Log.csv files and NVIDIA_App_Performance_Log_*.csv files
/// are considered; summaries and unrelated CSVs are ignored. Missing
/// directories and permission problems yield empty results.
/// </summary>
public sealed class CaptureFolderScanner
{
    private const string NvidiaAppLogPrefix = "NVIDIA_App_Performance_Log_";
    private const int MetadataScanConcurrency = 6;
    private readonly IFrameViewCsvReader _reader;

    public CaptureFolderScanner(IFrameViewCsvReader reader) => _reader = reader;

    /// <summary>Supported detailed performance CSVs, newest first by last-write time.</summary>
    public static IReadOnlyList<string> DiscoverLogFiles(string directory)
    {
        List<string> candidates = [];
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                if (!IsSupportedLogName(Path.GetFileName(path)) || !File.Exists(path))
                {
                    continue;
                }

                candidates.Add(path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Array.Empty<string>();
        }

        try
        {
            return candidates.OrderByDescending(GetLastWriteTime).ToList();
        }
        catch
        {
            return candidates;
        }
    }

    public Task<CaptureInfo?> ReadCaptureInfoAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        _reader.ReadCaptureInfoAsync(path, cancellationToken);

    /// <summary>
    /// Builds capture infos for a stable ordered set of files. Metadata reads
    /// are I/O-bound and independent, so a small bounded fan-out is much faster
    /// than walking a large benchmark folder serially without flooding Windows
    /// with hundreds of simultaneous file handles.
    /// </summary>
    public async Task<IReadOnlyList<CaptureInfo>> ReadCaptureInfosAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        var results = new CaptureInfo?[paths.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, paths.Count),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Min(MetadataScanConcurrency, paths.Count),
            },
            async (index, token) =>
            {
                results[index] = await _reader.ReadCaptureInfoAsync(paths[index], token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return results.Where(info => info is not null).Select(info => info!).ToList();
    }

    /// <summary>Builds capture infos for every supported log found in the folder.</summary>
    public Task<IReadOnlyList<CaptureInfo>> ScanCaptureFolderAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var paths = DiscoverLogFiles(directory);
        return ReadCaptureInfosAsync(paths, cancellationToken);
    }

    private static bool IsSupportedLogName(string fileName) =>
        fileName.EndsWith(CaptureFileNaming.LogSuffix, StringComparison.Ordinal)
        || (fileName.StartsWith(NvidiaAppLogPrefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));

    private static DateTime GetLastWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }
}
