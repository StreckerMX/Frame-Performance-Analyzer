using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Filtering;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// Binary timeline-data mode: raw capture data or the complete automatic
/// Precision filtering pipeline. Legacy option properties remain available for
/// imported snapshots, but live re-analysis always uses one canonical option
/// set per mode.
/// </summary>
public partial class AnalysisRangeViewModel : ObservableObject
{
    public const double MinGpuThreshold = 0.0;
    public const double MaxGpuThreshold = 80.0;
    public const double MinTrimSeconds = 0.0;
    public const double MaxTrimSeconds = 10.0;

    private const string PrecisionHelpText =
        "Automatically detects loading screens and FPS outliers, validates doubtful transition edges with available FrameView telemetry, and trims only the outer 1.0 s capture edges";

    private const string RawHelpText =
        "No GPU gate, FPS-outlier removal, transition validation, or edge trim is applied";

    private bool _suppressEvents;
    private bool _isMultiSessionMode;
    private bool _supportsMultimetricValidation;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _autoGpuThresholdEnabled = true;

    [ObservableProperty]
    private double _gpuThreshold = 10.0;

    [ObservableProperty]
    private double _trimBufferSeconds = 1.0;

    [ObservableProperty]
    private bool _excludeTransitionsEnabled = true;

    [ObservableProperty]
    private string _filterHelpText = PrecisionHelpText;

    [ObservableProperty]
    private string _filterMethodText = "No active filter";

    [ObservableProperty]
    private string _analysisSummaryText = "Load a capture to configure the analysis range.";

    /// <summary>Raised immediately when the Pair data mode changes.</summary>
    public event EventHandler<AnalysisOptions>? OptionsChanged;

    /// <summary>Raised immediately when the Multi data mode changes.</summary>
    public event EventHandler<AnalysisOptions>? MultiOptionsChanged;

    /// <summary>Effective GPU gate label, e.g. "GPU gate: 10%".</summary>
    public string GpuThresholdLabel => $"GPU gate: {GpuThreshold:F0}%";

    /// <summary>Effective trim label, e.g. "Trim 1.0 s".</summary>
    public string TrimLabel => $"Trim {TrimBufferSeconds:F1} s";

    /// <summary>GPU filtering controls are meaningful only while exclusion is enabled.</summary>
    public bool FilteringControlsEnabled => IsEnabled && ExcludeTransitionsEnabled;

    /// <summary>The manual GPU slider is usable only when automatic thresholding is off.</summary>
    public bool ManualGpuThresholdEnabled => FilteringControlsEnabled && !AutoGpuThresholdEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(FilteringControlsEnabled));
        OnPropertyChanged(nameof(ManualGpuThresholdEnabled));
    }

    partial void OnGpuThresholdChanged(double value)
    {
        OnPropertyChanged(nameof(GpuThresholdLabel));
        UpdateFilterHelpText();
    }

    partial void OnTrimBufferSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(TrimLabel));
    }

    partial void OnAutoGpuThresholdEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ManualGpuThresholdEnabled));
        UpdateFilterHelpText();
    }

    partial void OnExcludeTransitionsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(FilteringControlsEnabled));
        OnPropertyChanged(nameof(ManualGpuThresholdEnabled));
        UpdateFilterHelpText();

        // This is a discrete mode switch, not a continuously-changing slider.
        // Raise synchronously so BusyState can disable/dim the window before
        // the CPU-bound re-analysis begins on the next dispatcher turn.
        ApplyModeImmediately();
    }

    /// <summary>
    /// Returns the canonical live options for the selected data mode.
    /// Off is truly raw (no exclusions and no trim); on runs the complete
    /// automatic Precision filtering pipeline.
    /// </summary>
    public AnalysisOptions SnapshotOptions() =>
        new(
            GpuThreshold: Math.Clamp(GpuThreshold, MinGpuThreshold, MaxGpuThreshold),
            TrimBufferSeconds: ExcludeTransitionsEnabled
                ? AnalysisConstants.DefaultTrimBufferSeconds
                : 0.0,
            AutoGpuThreshold: true,
            ExcludeTransitions: ExcludeTransitionsEnabled);

    /// <summary>
    /// Adopts a Pair session's effective options into the controls without
    /// raising OptionsChanged, and refreshes the diagnostic text.
    /// </summary>
    public void Attach(SessionAnalysis? baseSession, SessionAnalysis? comparisonSession)
    {
        _isMultiSessionMode = false;
        AttachCore(baseSession);
        UpdateDiagnostics(baseSession, comparisonSession);
    }

    /// <summary>
    /// Adopts the shared effective options for an N-session Multi workspace.
    /// Multi peers deliberately share one range-control snapshot so every
    /// threshold/trim/transition change is applied consistently to all of them.
    /// </summary>
    public void AttachMulti(IReadOnlyList<SessionAnalysis> sessions)
    {
        _isMultiSessionMode = true;
        AttachCore(sessions.Count > 0 ? sessions[0] : null);
        UpdateMultiDiagnostics(sessions);
    }

    private void AttachCore(SessionAnalysis? session)
    {
        _supportsMultimetricValidation = session is not null
            && !CaptureSourceDetector.IsNvidiaAppPerformanceLog(session.Capture);
        _suppressEvents = true;
        try
        {
            IsEnabled = session is not null;
            if (session is null)
            {
                FilterHelpText = PrecisionHelpText;
                FilterMethodText = "No active filter";
                return;
            }

            var options = session.EffectiveOptions;
            AutoGpuThresholdEnabled = options.AutoGpuThreshold;
            GpuThreshold = Math.Clamp(options.GpuThreshold, MinGpuThreshold, MaxGpuThreshold);
            TrimBufferSeconds = Math.Clamp(options.TrimBufferSeconds, MinTrimSeconds, MaxTrimSeconds);
            ExcludeTransitionsEnabled = options.ExcludeTransitions;
            OnPropertyChanged(nameof(FilteringControlsEnabled));
            OnPropertyChanged(nameof(ManualGpuThresholdEnabled));
            UpdateFilterHelpText();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    /// <summary>Updates only the diagnostic/help text for the current Pair sessions.</summary>
    public void UpdateDiagnostics(SessionAnalysis? baseSession, SessionAnalysis? comparisonSession)
    {
        if (baseSession is null)
        {
            FilterHelpText = PrecisionHelpText;
            FilterMethodText = "No active filter";
            AnalysisSummaryText = "Load a capture to configure the analysis range.";
            return;
        }

        UpdateFilterHelpText();

        var diagnostics = baseSession.Diagnostics;
        var total = diagnostics.TotalBins;
        var visible = diagnostics.VisibleBins;
        var percent = total > 0 ? visible * 100.0 / total : 0.0;
        var recordedSamples = baseSession.Samples.Count;
        var analyzedSamples = CountAnalyzedSamples(baseSession);
        var sampleNoun = CaptureSourceDetector.IsNvidiaAppPerformanceLog(baseSession.Capture)
            ? "samples"
            : "frames";
        var parts = new List<string>
        {
            $"{recordedSamples:N0} recorded {sampleNoun} · {analyzedSamples:N0} analyzed {sampleNoun} · {visible:N0} chart samples",
            $"{visible:N0} / {total:N0} seconds analyzed ({percent:F0}%)",
        };
        AddExclusionDiagnostics(parts, baseSession);

        if (comparisonSession is not null)
        {
            parts.Add("Applied to Base and Comparison.");
        }

        AnalysisSummaryText = string.Join("  ·  ", parts);
    }

    /// <summary>Updates the aggregate diagnostic text for all Multi peers.</summary>
    public void UpdateMultiDiagnostics(IReadOnlyList<SessionAnalysis> sessions)
    {
        if (sessions.Count == 0)
        {
            FilterHelpText = PrecisionHelpText;
            FilterMethodText = "No active filter";
            AnalysisSummaryText = "Select two or more benchmarks to configure the Multi analysis range.";
            return;
        }

        UpdateFilterHelpText();

        var total = sessions.Sum(session => session.Diagnostics.TotalBins);
        var visible = sessions.Sum(session => session.Diagnostics.VisibleBins);
        var percent = total > 0 ? visible * 100.0 / total : 0.0;
        var recorded = sessions.Sum(session => session.Samples.Count);
        var analyzed = sessions.Sum(CountAnalyzedSamples);
        var outlierBins = sessions.Sum(session => session.Diagnostics.FpsOutlierBins);
        var transitionEdgeBins = sessions.Sum(session => session.Diagnostics.TransitionEdgeBins);
        var belowGpuBins = sessions.Sum(session => session.Diagnostics.BelowGpuBins);
        var edgeTrimmedBins = sessions.Sum(session => session.Diagnostics.EdgeTrimmedBins);
        var parts = new List<string>
        {
            $"{sessions.Count} benchmarks · {recorded:N0} recorded samples · {analyzed:N0} analyzed samples",
            $"{visible:N0} / {total:N0} benchmark-seconds analyzed ({percent:F0}%)",
        };

        if (belowGpuBins > 0)
        {
            parts.Add($"Excluded {belowGpuBins:N0} benchmark-seconds below the GPU utilization threshold.");
        }

        if (outlierBins > 0)
        {
            parts.Add($"Excluded {outlierBins:N0} benchmark-seconds of global FPS outliers.");
        }

        if (transitionEdgeBins > 0)
        {
            parts.Add($"Removed {transitionEdgeBins:N0} unstable transition-edge second(s).");
        }

        if (edgeTrimmedBins > 0)
        {
            parts.Add($"Trimmed the outer {TrimBufferSeconds:F1} s capture edges where applicable.");
        }

        if (!sessions[0].EffectiveOptions.ExcludeTransitions)
        {
            parts.Add(edgeTrimmedBins == 0
                ? "Raw data mode: no loading-screen, FPS, transition, or edge samples were excluded."
                : "Legacy unfiltered data: transition exclusion was disabled, but the saved edge trim remains.");
        }

        parts.Add($"Applied to all {sessions.Count} benchmarks.");
        AnalysisSummaryText = string.Join("  ·  ", parts);
    }

    private void UpdateFilterHelpText()
    {
        if (!ExcludeTransitionsEnabled)
        {
            FilterMethodText = "Raw data · Every recorded sample";
            FilterHelpText = RawHelpText;
            return;
        }

        FilterMethodText = _supportsMultimetricValidation
            ? $"Precision filtering · Automatic GPU gate ({GpuThreshold:F0}%) + multimetric validation"
            : $"Precision filtering · Automatic GPU gate ({GpuThreshold:F0}%) + FPS outlier filtering";
        FilterHelpText = _supportsMultimetricValidation
            ? PrecisionHelpText
            : "Automatically applies the GPU gate, FPS-outlier filtering, and a fixed 1.0 s outer-edge trim";
    }

    private void AddExclusionDiagnostics(List<string> parts, SessionAnalysis session)
    {
        var diagnostics = session.Diagnostics;
        if (diagnostics.BelowGpuBins > 0)
        {
            parts.Add($"Excluded {diagnostics.BelowGpuBins:N0} s below the GPU utilization threshold.");
        }

        if (diagnostics.FpsOutlierBins > 0)
        {
            parts.Add($"Excluded {diagnostics.FpsOutlierBins:N0} s of FPS outliers above {diagnostics.FpsUpperBound:F0} FPS.");
        }

        if (diagnostics.TransitionEdgeBins > 0)
        {
            parts.Add($"Removed {diagnostics.TransitionEdgeBins:N0} unstable transition-edge second(s).");
        }

        if (diagnostics.EdgeTrimmedBins > 0)
        {
            parts.Add($"Trimmed the outer {TrimBufferSeconds:F1} s capture edges where applicable.");
        }

        if (!session.EffectiveOptions.ExcludeTransitions)
        {
            parts.Add(diagnostics.EdgeTrimmedBins == 0
                ? "Raw data mode: no loading-screen, FPS, transition, or edge samples were excluded."
                : "Legacy unfiltered data: transition exclusion was disabled, but the saved edge trim remains.");
        }
    }

    private static int CountAnalyzedSamples(SessionAnalysis session)
    {
        var count = 0;
        foreach (var bin in session.ValidBins)
        {
            if (session.RowsByBin.TryGetValue(bin, out var rows))
            {
                count += rows.Length;
            }
        }

        return count;
    }

    /// <summary>Applies pending changes immediately (used by tests).</summary>
    public void ApplyNow() => RaiseOptionsChanged();

    private void RaiseOptionsChanged()
    {
        var options = SnapshotOptions();
        if (_isMultiSessionMode)
        {
            MultiOptionsChanged?.Invoke(this, options);
        }
        else
        {
            OptionsChanged?.Invoke(this, options);
        }
    }

    private void ApplyModeImmediately()
    {
        if (_suppressEvents || !IsEnabled)
        {
            return;
        }

        RaiseOptionsChanged();
    }
}
