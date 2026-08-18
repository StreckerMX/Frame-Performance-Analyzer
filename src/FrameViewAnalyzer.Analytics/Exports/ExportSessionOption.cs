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
/// One selectable export session: an explicit role prefix and the resolved
/// session, so the UI label and the actual export target can never diverge.
/// </summary>
public sealed record ExportSessionOption(SessionRole Role, string DisplayName, SessionAnalysis Session)
{
    /// <summary>Role-aware label, e.g. "Base — GTA5 Enhanced".</summary>
    public string Label => Role == SessionRole.Base
        ? $"Base — {DisplayName}"
        : $"Comparison — {DisplayName}";
}

/// <summary>
/// Future-proof PNG report request. Sessions and metrics are explicit
/// collections so the same contract can support Pair and Multi workspaces.
/// </summary>
public sealed record ExportReportSelection(
    IReadOnlyList<ExportSessionOption> Sessions,
    IReadOnlyList<string> MetricIds);
