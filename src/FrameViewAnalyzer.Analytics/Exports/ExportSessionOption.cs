using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Exports;

/// <summary>
/// Legacy pair-mode scope retained for report-header compatibility. The new
/// export dialog uses ExportReportSelection so it can grow to N sessions.
/// </summary>
public enum ExportScope
{
    All,
    Single,
}

/// <summary>
/// One selectable export session. Pair exports keep explicit Base/Comparison
/// roles; Multi exports carry the stable workspace index used by the shared
/// chart/report palette and intentionally have no Base or Reference semantics.
/// </summary>
public sealed record ExportSessionOption(
    SessionRole Role,
    string DisplayName,
    SessionAnalysis Session,
    int WorkspaceIndex = 0,
    bool IsMultiPeer = false)
{
    /// <summary>
    /// Picker label. Pair keeps the role prefix; Multi shows the benchmark name
    /// directly because every selected capture is an equal peer.
    /// </summary>
    public string Label => IsMultiPeer
        ? DisplayName
        : Role == SessionRole.Base
            ? $"Base — {DisplayName}"
            : $"Comparison — {DisplayName}";

    /// <summary>Human-readable report-header line for this selected benchmark.</summary>
    public string HeaderLine => IsMultiPeer
        ? $"Benchmark: {DisplayName}"
        : ExportReport.SessionRoleLine(Role, DisplayName);
}

/// <summary>
/// Future-proof PNG report request. Sessions and metrics are explicit
/// collections so the same contract supports Pair and Multi workspaces.
/// </summary>
public sealed record ExportReportSelection(
    IReadOnlyList<ExportSessionOption> Sessions,
    IReadOnlyList<string> MetricIds);
