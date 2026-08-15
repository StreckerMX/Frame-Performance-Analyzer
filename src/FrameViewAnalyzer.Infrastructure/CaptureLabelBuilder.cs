using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Infrastructure;

/// <summary>
/// One-line capture labels for the dashboard dropdown and library rows.
/// Includes application, resolution, compacted CPU/GPU, capture time, and
/// duration; the time and duration are always kept and the head is
/// ellipsized only when the label would exceed the maximum length.
/// </summary>
public static class CaptureLabelBuilder
{
    public static string BuildLabel(CaptureInfo info, int maximumLength = 72)
    {
        var stamp = CaptureStamp(info);
        var duration = DisplayText.FormatDuration(info.DurationSeconds);

        var headParts = new List<string> { info.Application };
        foreach (var value in new[] { info.Resolution, info.Cpu, info.Gpu })
        {
            if (!string.IsNullOrEmpty(value) && value != "--")
            {
                headParts.Add(DisplayText.CompactHardware(value));
            }
        }

        var head = string.Join(" · ", headParts);
        var tail = $"{stamp} · {duration}";
        if (head.Length + tail.Length + 3 > maximumLength)
        {
            var budget = maximumLength - tail.Length - 4;
            if (budget <= 0)
            {
                head = "…";
            }
            else
            {
                head = head[..Math.Min(budget, head.Length)].TrimEnd(' ', '·') + "…";
            }
        }

        return $"{head} · {tail}";
    }

    /// <summary>
    /// Compact capture timestamp: the FrameView file-name stamp when
    /// present, otherwise the file's last-write time.
    /// </summary>
    public static string CaptureStamp(CaptureInfo info)
    {
        if (CaptureFileNaming.TryParseCaptureStamp(info.Path, out var stamp))
        {
            return CaptureFileNaming.FormatStamp(stamp);
        }

        try
        {
            return CaptureFileNaming.FormatStamp(File.GetLastWriteTime(info.Path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "unknown time";
        }
    }
}
