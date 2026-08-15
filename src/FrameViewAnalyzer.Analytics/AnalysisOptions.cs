namespace FrameViewAnalyzer.Analytics;

/// <summary>
/// Filters applied to a capture analysis. Defaults mirror the Python
/// reference: 10% manual GPU floor, 1 s edge trim, automatic threshold,
/// and transition (loading-screen / abnormal-FPS) exclusion enabled.
/// </summary>
public sealed record AnalysisOptions(
    double GpuThreshold = 10.0,
    double TrimBufferSeconds = 1.0,
    bool AutoGpuThreshold = true,
    bool ExcludeTransitions = true);
