namespace FrameViewAnalyzer.Core.Models;

/// <summary>
/// Performance direction of a metric. Drives percentile tail selection
/// (1% Low vs 1% High), improvement/regression semantics, and range analysis.
/// </summary>
public enum MetricDirection
{
    /// <summary>No defined direction (contextual metrics).</summary>
    Undefined,

    /// <summary>Higher values are better (FPS, efficiency).</summary>
    HigherIsBetter,

    /// <summary>Lower values are better (frame time, latency, temperatures).</summary>
    LowerIsBetter,
}
