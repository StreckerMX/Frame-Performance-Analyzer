namespace FrameViewAnalyzer.Core;

/// <summary>
/// Session identity carried by a plotted series so styling follows the
/// session role (Base = SeriesA, Comparison = SeriesB) rather than the
/// collection index.
/// </summary>
public enum SessionRole
{
    Base,
    Comparison,
}
