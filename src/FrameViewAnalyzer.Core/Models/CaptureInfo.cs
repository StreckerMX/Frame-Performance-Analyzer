namespace FrameViewAnalyzer.Core.Models;

/// <summary>
/// Real metadata extracted from one FrameView log file without a full parse:
/// application, resolution, GPU/CPU, and the capture duration read from the
/// last recorded second.
/// </summary>
public sealed record CaptureInfo(
    string Path,
    string Name,
    string Application,
    string Resolution,
    string Gpu,
    string Cpu,
    double? DurationSeconds);
