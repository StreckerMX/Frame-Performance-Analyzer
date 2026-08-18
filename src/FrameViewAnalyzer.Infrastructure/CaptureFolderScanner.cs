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

    /// <summary>Builds capture infos for every supported log found in the folder.</summary>
    public async Task<IReadOnlyList<CaptureInfo>> ScanCaptureFolderAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var infos = new List<CaptureInfo>();
        foreach (var path in DiscoverLogFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = await _reader.ReadCaptureInfoAsync(path, cancellationToken).ConfigureAwait(false);
            if (info is not null)
            {
                infos.Add(info);
            }
        }

        return infos;
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
