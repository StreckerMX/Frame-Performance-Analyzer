namespace FrameViewAnalyzer.Core.Models;

/// <summary>A single data point on a metric series (time-relative x).</summary>
public readonly record struct ChartPoint(double X, double Y);
