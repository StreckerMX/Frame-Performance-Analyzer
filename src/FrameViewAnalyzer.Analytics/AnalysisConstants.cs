namespace FrameViewAnalyzer.Analytics;

/// <summary>Tuning constants shared by the analysis engine (Python parity).</summary>
public static class AnalysisConstants
{
    public const double FpsBinSeconds = 1.0;
    public const double DefaultGpuThreshold = 10.0;
    public const double DefaultTrimBufferSeconds = 1.0;
    public const double FpsChartCap = 5000.0;
    public const int MinFramesPerBin = 3;
    public const double AutoGpuRatio = 0.55;
    public const double AutoGpuMin = 5.0;
    public const double AutoGpuMax = 80.0;
    public const int MinActiveRunSeconds = 3;
}
