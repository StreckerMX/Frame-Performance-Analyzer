namespace FrameViewAnalyzer.Core.Models;

/// <summary>
/// Identifies supported capture producers from their exported CSV headers.
/// Source detection stays separate from <see cref="CsvKind"/> because both
/// FrameView logs and NVIDIA App performance logs are detailed analyzable CSVs.
/// </summary>
public static class CaptureSourceDetector
{
    public const string NvidiaAppTimeHeader = "Timestamp (Elapsed time in seconds)";

    public static bool IsNvidiaAppPerformanceLog(CaptureData capture) =>
        IsNvidiaAppPerformanceLog(capture.Headers);

    public static bool IsNvidiaAppPerformanceLog(IReadOnlyList<string> headers)
    {
        var normalized = new HashSet<string>(headers.Select(header => header.Trim()), StringComparer.Ordinal);
        return normalized.Contains(NvidiaAppTimeHeader)
            && normalized.Contains("FPS")
            && (normalized.Contains("FPS 1(%) Low")
                || normalized.Contains("Render Latency(MSec)")
                || normalized.Contains("GPU1 Utilization(%)"));
    }
}
