namespace FrameViewAnalyzer.Analytics.Samples;

/// <summary>
/// Parsed per-frame samples in struct-of-arrays form, sorted by time.
/// Missing values are NaN. RowIndex maps back to the source capture row so
/// per-metric values can be resolved lazily during series building.
/// </summary>
public sealed class ParsedSamples
{
    public required double[] TimeSeconds { get; init; }
    public required double[] FrametimeMs { get; init; }
    public required double[] Fps { get; init; }
    public required double[] GpuUtilPercent { get; init; }
    public required int[] RowIndex { get; init; }

    public int Count => TimeSeconds.Length;
}
