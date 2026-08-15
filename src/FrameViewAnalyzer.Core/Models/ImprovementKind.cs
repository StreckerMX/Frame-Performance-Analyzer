namespace FrameViewAnalyzer.Core.Models;

/// <summary>
/// Direction-aware classification of a comparison delta. The sign of the
/// delta alone never decides; metric semantics do.
/// </summary>
public enum ImprovementKind
{
    None,
    Improvement,
    Regression,
}
