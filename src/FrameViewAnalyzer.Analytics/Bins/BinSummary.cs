namespace FrameViewAnalyzer.Analytics.Bins;

/// <summary>
/// One-second bin summary. FPS is harmonic (1000 × frames / Σ frame time),
/// never the mean of per-frame FPS values.
/// </summary>
public readonly record struct BinSummary(
    int Index,
    double Start,
    double? GpuUtil,
    double? Fps,
    int FrameCount);
