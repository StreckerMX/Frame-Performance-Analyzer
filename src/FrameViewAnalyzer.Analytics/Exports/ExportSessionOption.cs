using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Exports;

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
    /// <summary>Role-aware ComboBox label, e.g. "Base — GTA5 Enhanced".</summary>
    public string Label => Role == SessionRole.Base
        ? $"Base — {DisplayName}"
        : $"Comparison — {DisplayName}";
}
