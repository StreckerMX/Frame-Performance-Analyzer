namespace FrameViewAnalyzer.Analytics.Series;

/// <summary>
/// Immutable analyzed metric samples restored from a Frame Performance Analyzer
/// portable CSV/JSON export. X values keep their original capture-relative
/// timestamps so imported snapshots preserve the exact exported time window.
/// </summary>
public sealed record ImportedSeriesData(
    double[] X,
    double[] Y);
