using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Filtering;
using FrameViewAnalyzer.Core.Formatting;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// Analysis-range controls mirroring the Python reference rail: automatic or
/// manual GPU threshold, edge trim, and transition exclusion. The view model
/// holds no analytics logic — it snapshots AnalysisOptions and raises the
/// appropriate debounced event so the owner re-analyzes Pair or Multi sessions.
/// </summary>
public partial class AnalysisRangeViewModel : ObservableObject
{
    public const double MinGpuThreshold = 0.0;
    public const double MaxGpuThreshold = 80.0;
    public const double MinTrimSeconds = 0.0;
    public const double MaxTrimSeconds = 10.0;

    /// <summary>Debounce for continuously-changing controls (sliders).</summary>
    public static readonly TimeSpan ChangeDebounce = TimeSpan.FromMilliseconds(400);

    private readonly DispatcherTimer _debounce;
    private bool _suppressEvents;
    private bool _isMultiSessionMode;

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
    private string _filterHelpText =
        "the threshold will use 55% of the per-second GPU 90th percentile (limited to 5–80%)";

    [ObservableProperty]
    private string _analysisSummaryText = "Load a capture to configure the analysis range.";

    /// <summary>Raised once per debounce window when Pair controls change.</summary>
    public event EventHandler<AnalysisOptions>? OptionsChanged;

    /// <summary>Raised once per debounce window when Multi controls change.</summary>
    public event EventHandler<AnalysisOptions>? MultiOptionsChanged;

    public AnalysisRangeViewModel()
    {
        _debounce = new DispatcherTimer { Interval = ChangeDebounce };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RaiseOptionsChanged();
        };
    }

    /// <summary>Effective manual slider label, e.g. "10%".</summary>
    public string GpuThresholdLabel => $"{GpuThreshold:F0}%";

    /// <summary>Effective trim label, e.g. "Trim 1.0 s".</summary>
    public string TrimLabel => $"Trim {TrimBufferSeconds:F1} s";

    /// <summary>The manual GPU slider is only usable when a session is loaded and automatic mode is off.</summary>
    public bool ManualGpuThresholdEnabled => IsEnabled && !AutoGpuThresholdEnabled;

    partial void OnGpuThresholdChanged(double value)
    {
        OnPropertyChanged(nameof(GpuThresholdLabel));
        Schedule();
    }

    partial void OnTrimBufferSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(TrimLabel));
        Schedule();
    }

    partial void OnAutoGpuThresholdEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ManualGpuThresholdEnabled));
        Schedule();
    }

    partial void OnExcludeTransitionsEnabledChanged(bool value) => Schedule();

    /// <summary>Snapshots the current controls into an AnalysisOptions.</summary>
    public AnalysisOptions SnapshotOptions() =>
        new(
            GpuThreshold: Math.Clamp(GpuThreshold, MinGpuThreshold, MaxGpuThreshold),
            TrimBufferSeconds: Math.Clamp(TrimBufferSeconds, MinTrimSeconds, MaxTrimSeconds),
            AutoGpuThreshold: AutoGpuThresholdEnabled,
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
        _debounce.Stop();
        _suppressEvents = true;
        try
        {
            IsEnabled = session is not null;
            OnPropertyChanged(nameof(ManualGpuThresholdEnabled));
            if (session is null)
            {
                FilterHelpText =
                    "the threshold will use 55% of the per-second GPU 90th percentile (limited to 5–80%)";
                return;
            }

            var options = session.EffectiveOptions;
            AutoGpuThresholdEnabled = options.AutoGpuThreshold;
            GpuThreshold = Math.Clamp(options.GpuThreshold, MinGpuThreshold, MaxGpuThreshold);
            TrimBufferSeconds = Math.Clamp(options.TrimBufferSeconds, MinTrimSeconds, MaxTrimSeconds);
            ExcludeTransitionsEnabled = options.ExcludeTransitions;
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
            FilterHelpText =
                "the threshold will use 55% of the per-second GPU 90th percentile (limited to 5–80%)";
            AnalysisSummaryText = "Load a capture to configure the analysis range.";
            return;
        }

        UpdateFilterHelpText();

        var diagnostics = baseSession.Diagnostics;
        var total = diagnostics.TotalBins;
        var visible = diagnostics.VisibleBins;
        var percent = total > 0 ? visible * 100.0 / total : 0.0;
        var parts = new List<string>
        {
            $"{visible:N0} / {total:N0} seconds analyzed ({percent:F0}%)",
        };
        AddExclusionDiagnostics(parts, diagnostics);

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
            FilterHelpText =
                "the threshold will use 55% of the per-second GPU 90th percentile (limited to 5–80%)";
            AnalysisSummaryText = "Select two or more benchmarks to configure the Multi analysis range.";
            return;
        }

        UpdateFilterHelpText();

        var total = sessions.Sum(session => session.Diagnostics.TotalBins);
        var visible = sessions.Sum(session => session.Diagnostics.VisibleBins);
        var percent = total > 0 ? visible * 100.0 / total : 0.0;
        var outlierBins = sessions.Sum(session => session.Diagnostics.FpsOutlierBins);
        var belowGpuBins = sessions.Sum(session => session.Diagnostics.BelowGpuBins);
        var edgeTrimmedBins = sessions.Sum(session => session.Diagnostics.EdgeTrimmedBins);
        var parts = new List<string>
        {
            $"{sessions.Count} benchmarks  ·  {visible:N0} / {total:N0} benchmark-seconds analyzed ({percent:F0}%)",
        };

        if (outlierBins > 0)
        {
            parts.Add($"Excluded {outlierBins:N0} benchmark-seconds of FPS outliers.");
        }
        else if (belowGpuBins > 0)
        {
            parts.Add($"Excluded {belowGpuBins:N0} benchmark-seconds below the GPU utilization threshold.");
        }

        if (edgeTrimmedBins > 0)
        {
            parts.Add($"Removed {TrimBufferSeconds:F1} s from the detected segment edges in each benchmark.");
        }

        parts.Add($"Applied to all {sessions.Count} benchmarks.");
        AnalysisSummaryText = string.Join("  ·  ", parts);
    }

    private void UpdateFilterHelpText()
    {
        FilterHelpText = AutoGpuThresholdEnabled
            ? "the threshold will use 55% of the per-second GPU 90th percentile (limited to 5–80%)"
            : $"at least {GpuThreshold:F0}% GPU utilization will be required";
    }

    private void AddExclusionDiagnostics(List<string> parts, FilterDiagnostics diagnostics)
    {
        if (diagnostics.FpsOutlierBins > 0)
        {
            parts.Add($"Excluded: {diagnostics.FpsOutlierBins:N0} s of FPS outliers above {diagnostics.FpsUpperBound:F0} FPS.");
        }
        else if (diagnostics.BelowGpuBins > 0)
        {
            parts.Add($"Excluded: {diagnostics.BelowGpuBins:N0} s below the GPU utilization threshold.");
        }

        if (diagnostics.EdgeTrimmedBins > 0)
        {
            parts.Add($"Removed {TrimBufferSeconds:F1} s from the start and end of the detected segment.");
        }
    }

    /// <summary>Applies pending changes immediately (used by tests).</summary>
    public void ApplyNow()
    {
        _debounce.Stop();
        RaiseOptionsChanged();
    }

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

    /// <summary>
    /// Trailing-edge debounce: every control change restarts the 400 ms delay,
    /// so the active Pair/Multi event fires only after ~400 ms of inactivity.
    /// </summary>
    private void Schedule()
    {
        if (_suppressEvents || !IsEnabled)
        {
            return;
        }

        _debounce.Stop();
        _debounce.Start();
    }
}
