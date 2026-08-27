namespace FrameViewAnalyzer.Analytics.Filtering;

/// <summary>The analyzable time window detected from the capture.</summary>
public sealed record ActiveWindow(double Start, double End);

/// <summary>Counts explaining how the filter profile was produced.</summary>
public sealed record FilterDiagnostics(
    int TotalBins = 0,
    int VisibleBins = 0,
    int BelowGpuBins = 0,
    int FpsOutlierBins = 0,
    int TransitionEdgeBins = 0,
    int EdgeTrimmedBins = 0,
    double? FpsUpperBound = null);

/// <summary>Result of the filter-profile detection over one capture.</summary>
public sealed record FilterProfile(
    ActiveWindow? Window,
    IReadOnlySet<int> ValidBins,
    FilterDiagnostics Diagnostics);
