namespace FrameViewAnalyzer.Analytics.RangeAnalysis;

/// <summary>An inclusive time window in seconds, relative to capture start.</summary>
public readonly record struct TimeRange(double Start, double End);

/// <summary>Tunables shared by the range-analysis algorithms (Python parity).</summary>
public static class RangeAnalysisDefaults
{
    public const double DefaultWindowSeconds = 10.0;
    public const int MinSamplesPerWindow = 5;
    public const int DropMinGapSamples = 5;
    public const double DropMinAbsolute = 0.5;
    public const double DropMinFractionOfRange = 0.03;
}
