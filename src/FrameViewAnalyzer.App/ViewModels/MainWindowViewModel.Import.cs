using FrameViewAnalyzer.Analytics.Exports;

namespace FrameViewAnalyzer.App.ViewModels;

public partial class MainWindowViewModel
{
    /// <summary>
    /// Replaces the active workspace with sessions restored from a portable
    /// Frame Performance Analyzer CSV/JSON export. Imported snapshots are intentionally
    /// read-only with respect to the analysis-range filters because no raw
    /// FrameView rows are embedded in the portable file.
    /// </summary>
    public void LoadPortableAnalysis(PortableAnalysisDocument document, string sourcePath)
    {
        var imported = PortableAnalysisExport.RestoreSessions(document, sourcePath);
        var isMulti = string.Equals(document.WorkspaceMode, "multi", StringComparison.OrdinalIgnoreCase);

        if (isMulti)
        {
            if (imported.Count < 2 || imported.Count > 8)
            {
                throw new InvalidDataException("A Multi analyzed-data export must contain 2–8 sessions.");
            }

            MultiSessions.Clear();
            foreach (var item in imported.OrderBy(item => item.SessionIndex))
            {
                MultiSessions.Add(new MultiBenchmarkSession(item.Session, item.Label));
            }

            SetWorkspaceMode(BenchmarkWorkspaceMode.Multi);
            ActivateMultiWorkspace();
            AnalysisRange.AttachPortable(
                MultiSessions.Select(item => item.Session).ToList(),
                isMultiWorkspace: true);
            NotifyMultiStateChanged();
            StatusText = $"IMPORTED ANALYZED DATA  ·  {MultiSessions.Count} benchmarks";
            return;
        }

        if (imported.Count is < 1 or > 2)
        {
            throw new InvalidDataException("A Pair analyzed-data export must contain one or two sessions.");
        }

        SetWorkspaceMode(BenchmarkWorkspaceMode.Pair);
        var ordered = imported.OrderBy(item => item.SessionIndex).ToList();
        var baseItem = ordered.FirstOrDefault(item =>
                string.Equals(item.Role, "base", StringComparison.OrdinalIgnoreCase))
            ?? ordered[0];
        var comparisonItem = ordered.FirstOrDefault(item =>
            !ReferenceEquals(item, baseItem)
            && string.Equals(item.Role, "comparison", StringComparison.OrdinalIgnoreCase));
        comparisonItem ??= ordered.FirstOrDefault(item => !ReferenceEquals(item, baseItem));

        BaseSession = baseItem.Session;
        ComparisonSession = comparisonItem?.Session;
        RefreshSessionCards();
        Chart.SetSessions(BaseSession, ComparisonSession);
        AnalysisRange.AttachPortable(
            comparisonItem is null
                ? [baseItem.Session]
                : [baseItem.Session, comparisonItem.Session],
            isMultiWorkspace: false);
        StatusText = comparisonItem is null
            ? $"IMPORTED ANALYZED DATA  ·  {baseItem.Label}"
            : $"IMPORTED ANALYZED DATA  ·  {baseItem.Label} vs {comparisonItem.Label}";
    }
}
