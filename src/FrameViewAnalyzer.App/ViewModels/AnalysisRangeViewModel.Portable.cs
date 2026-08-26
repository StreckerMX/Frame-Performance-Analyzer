using FrameViewAnalyzer.Analytics;

namespace FrameViewAnalyzer.App.ViewModels;

public partial class AnalysisRangeViewModel
{
    /// <summary>
    /// Displays the analysis settings embedded in an imported portable
    /// snapshot without allowing re-analysis. The raw FrameView capture is not
    /// embedded in portable exports, so these controls are intentionally
    /// read-only until a real capture is loaded again.
    /// </summary>
    public void AttachPortable(IReadOnlyList<SessionAnalysis> sessions, bool isMultiWorkspace)
    {
        _debounce.Stop();
        _isMultiSessionMode = isMultiWorkspace;
        _suppressEvents = true;
        try
        {
            IsEnabled = false;
            OnPropertyChanged(nameof(ManualGpuThresholdEnabled));

            if (sessions.Count == 0)
            {
                AnalysisSummaryText = "Imported snapshot contains no sessions.";
                return;
            }

            var options = sessions[0].EffectiveOptions;
            AutoGpuThresholdEnabled = options.AutoGpuThreshold;
            GpuThreshold = Math.Clamp(options.GpuThreshold, MinGpuThreshold, MaxGpuThreshold);
            TrimBufferSeconds = Math.Clamp(options.TrimBufferSeconds, MinTrimSeconds, MaxTrimSeconds);
            ExcludeTransitionsEnabled = options.ExcludeTransitions;
            FilterHelpText = "imported analyzed data is read-only; load the original capture to change filtering";
            AnalysisSummaryText = sessions.Count == 1
                ? "Imported analyzed-data snapshot  ·  Analysis controls are locked to the exported data."
                : $"Imported analyzed-data snapshot  ·  {sessions.Count} benchmarks  ·  Analysis controls are locked to the exported data.";
        }
        finally
        {
            _suppressEvents = false;
        }
    }
}
