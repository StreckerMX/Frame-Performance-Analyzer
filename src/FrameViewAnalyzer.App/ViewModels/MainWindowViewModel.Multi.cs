using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.Input;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Busy;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.ViewModels;

public enum BenchmarkWorkspaceMode
{
    Pair,
    Multi,
}

/// <summary>One loaded benchmark in the Multi workspace.</summary>
public sealed record MultiBenchmarkSession(
    SessionAnalysis Session,
    string Label)
{
    public string Path => Session.Capture.Path;
}

public partial class MainWindowViewModel
{
    private const int MultiLoadConcurrency = 3;
    private BenchmarkWorkspaceMode _workspaceMode = BenchmarkWorkspaceMode.Pair;
    private bool _multiAnalysisRangeSubscribed;

    public ObservableCollection<MultiBenchmarkSession> MultiSessions { get; } = [];

    public bool IsPairMode
    {
        get => _workspaceMode == BenchmarkWorkspaceMode.Pair;
        set
        {
            if (value)
            {
                SetWorkspaceMode(BenchmarkWorkspaceMode.Pair);
            }
        }
    }

    public bool IsMultiMode
    {
        get => _workspaceMode == BenchmarkWorkspaceMode.Multi;
        set
        {
            if (value)
            {
                SetWorkspaceMode(BenchmarkWorkspaceMode.Multi);
            }
        }
    }

    public bool HasMultiSelection => MultiSessions.Count > 0;

    public string MultiSelectionSummary => MultiSessions.Count switch
    {
        0 => "No benchmarks selected",
        1 => "1 benchmark selected",
        _ => $"{MultiSessions.Count} benchmarks selected",
    };

    public string MultiComparisonText => MultiSessions.Count >= 2
        ? "All selected benchmarks are compared equally."
        : "Select 2–8 benchmarks to compare them together.";

    // Compatibility binding for the current dashboard XAML. The old property
    // name is retained only so this feature branch does not need a broad XAML
    // rewrite; the displayed copy no longer describes any benchmark as a base.
    public string MultiReferenceText => MultiComparisonText;

    public string MultiBenchmarkNames
    {
        get
        {
            if (MultiSessions.Count == 0)
            {
                return "Choose two or more captures from the selected folder.";
            }

            const int previewCount = 4;
            var names = MultiSessions.Take(previewCount).Select(item => item.Label).ToList();
            var text = string.Join("  ·  ", names);
            var remaining = MultiSessions.Count - names.Count;
            return remaining > 0 ? $"{text}  ·  +{remaining} more" : text;
        }
    }

    public IReadOnlyList<string> MultiSelectedPaths => MultiSessions.Select(item => item.Path).ToList();

    /// <summary>Raised when the Multi checklist dialog should open.</summary>
    public event EventHandler? MultiBenchmarkSelectionRequested;

    [RelayCommand]
    private void SelectMultiBenchmarks()
    {
        if (!IsMultiMode)
        {
            return;
        }

        MultiBenchmarkSelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearMultiBenchmarks()
    {
        MultiSessions.Clear();
        NotifyMultiStateChanged();
        if (IsMultiMode)
        {
            ActivateMultiWorkspace();
        }
    }

    /// <summary>
    /// Loads a checked set of folder captures. Loading is transactional: the
    /// current Multi workspace is left untouched if any selected file fails.
    /// No benchmark is designated as a base or reference.
    /// </summary>
    public Task LoadMultiBenchmarksAsync(IReadOnlyList<string> selectedPaths) =>
        _busy.RunAsync("Loading benchmark captures", () => LoadMultiBenchmarksCoreAsync(selectedPaths));

    private async Task LoadMultiBenchmarksCoreAsync(IReadOnlyList<string> selectedPaths)
    {
        var paths = selectedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count < 2)
        {
            _dialogs.ShowInfo("Multi benchmark", "Select at least two benchmarks.");
            return;
        }

        if (paths.Count > 8)
        {
            _dialogs.ShowInfo(
                "Multi benchmark",
                "Select up to 8 benchmarks so the chart and statistics remain readable.");
            return;
        }

        try
        {
            // CSV reading and analysis are independent per capture. A bounded
            // fan-out cuts Multi load time substantially while avoiding the
            // memory/I/O spike of starting all eight long captures at once.
            using var gate = new SemaphoreSlim(Math.Min(MultiLoadConcurrency, paths.Count));
            var tasks = paths.Select(async (path, index) =>
            {
                await gate.WaitAsync();
                try
                {
                    var session = await LoadSessionAsync(path);
                    if (session is null)
                    {
                        throw new InvalidOperationException(
                            $"The selected file is not a benchmark log: {System.IO.Path.GetFileName(path)}");
                    }

                    var label = CardNameOf(session, ManualMetadataOf(session));
                    return (Index: index, Item: new MultiBenchmarkSession(session, label));
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            var loaded = results
                .OrderBy(result => result.Index)
                .Select(result => result.Item)
                .ToList();

            // Metric discovery asks the first peer for every available series.
            // Warm that immutable session off the UI thread so opening a large
            // workspace never freezes between the busy overlay and the chart.
            await _busy.RunOnThreadPoolAsync(
                "Preparing chart data",
                () => SeriesBuilder.Warm(loaded[0].Session));

            MultiSessions.Clear();
            foreach (var item in loaded)
            {
                MultiSessions.Add(item);
                IndexSession(item.Session);
            }

            SetWorkspaceMode(BenchmarkWorkspaceMode.Multi);
            ActivateMultiWorkspace();
            NotifyMultiStateChanged();
            StatusText = $"MULTI WORKSPACE  ·  Comparing {MultiSessions.Count} benchmarks";

            // The remaining peers warm quietly after the first frame is on
            // screen. SeriesBuilder's thread-safe lazy cache means a user can
            // switch metrics immediately; each peer/metric is still computed
            // at most once even if foreground and warm-up race.
            _ = Task.Run(() =>
            {
                foreach (var item in loaded.Skip(1))
                {
                    SeriesBuilder.Warm(item.Session);
                }
            });
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Multi benchmark loading error", error.Message);
        }
    }

    /// <summary>
    /// Re-analyzes every loaded Multi peer with one shared AnalysisOptions
    /// snapshot. All results are computed before the collection is mutated, so
    /// one failed benchmark leaves the previous N-session workspace untouched.
    /// The shared analysis generation guarantees that an older overlapping
    /// request (Pair or Multi) completing afterwards can never overwrite the
    /// state a newer request set.
    /// </summary>
    public async Task ApplyMultiAnalysisOptionsAsync(AnalysisOptions options)
    {
        if (!IsMultiMode || MultiSessions.Count == 0)
        {
            return;
        }

        var previous = MultiSessions.ToList();
        var generation = Interlocked.Increment(ref _analysisGeneration);
        using var busyScope = _busy.BeginVisible("Reanalyzing benchmarks");
        await Task.Yield();
        try
        {
            // Re-analysis is CPU-bound; run it off the UI thread so the busy
            // presentation keeps animating.
            var reanalyzed = await _busy.RunOnThreadPoolAsync(
                "Processing capture data",
                () => previous
                    .Select(item => item with { Session = _analysis.Reanalyze(item.Session, options) })
                    .ToList());

            // A newer request superseded this one while it was computing;
            // the stale result must never overwrite the newer state.
            if (generation != Volatile.Read(ref _analysisGeneration))
            {
                return;
            }

            MultiSessions.Clear();
            foreach (var item in reanalyzed)
            {
                MultiSessions.Add(item);
                IndexSession(item.Session);
            }

            ActivateMultiWorkspace();
            StatusText = $"REANALYZED  ·  {MultiSessions.Count} Multi benchmarks";
        }
        catch (Exception error)
        {
            if (generation != Volatile.Read(ref _analysisGeneration))
            {
                // Stale failure: a newer request owns the state now.
                return;
            }

            // The collection was not touched before every Reanalyze succeeded.
            // Re-attach the old snapshots so controls also return to the
            // effective options represented by the still-visible workspace.
            AnalysisRange.AttachMulti(previous.Select(item => item.Session).ToList());
            StatusText = "MULTI REANALYSIS FAILED  ·  Previous workspace kept";
            _dialogs.ShowError("Multi analysis error", error.Message);
        }
    }

    private void SetWorkspaceMode(BenchmarkWorkspaceMode mode)
    {
        if (_workspaceMode == mode)
        {
            if (mode == BenchmarkWorkspaceMode.Multi)
            {
                ActivateMultiWorkspace();
            }

            return;
        }

        _workspaceMode = mode;
        OnPropertyChanged(nameof(IsPairMode));
        OnPropertyChanged(nameof(IsMultiMode));

        if (mode == BenchmarkWorkspaceMode.Pair)
        {
            Chart.SetSessions(BaseSession, ComparisonSession);
            AnalysisRange.Attach(BaseSession, ComparisonSession);
            StatusText = BaseSession is null
                ? "READY  ·  Pair mode"
                : "PAIR WORKSPACE";
        }
        else
        {
            ActivateMultiWorkspace();
        }
    }

    private void ActivateMultiWorkspace()
    {
        EnsureMultiAnalysisRangeSubscription();

        if (MultiSessions.Count == 0)
        {
            Chart.Clear();
            AnalysisRange.AttachMulti([]);
            StatusText = "MULTI WORKSPACE  ·  Select benchmarks from the capture folder";
            NotifyMultiStateChanged();
            return;
        }

        Chart.SetWorkspace(
            MultiSessions.Select(item => new ChartWorkspaceSession(
                item.Session,
                item.Label)).ToList(),
            isMultiWorkspace: true);

        AnalysisRange.AttachMulti(MultiSessions.Select(item => item.Session).ToList());
        NotifyMultiStateChanged();
    }

    private void EnsureMultiAnalysisRangeSubscription()
    {
        if (_multiAnalysisRangeSubscribed)
        {
            return;
        }

        AnalysisRange.MultiOptionsChanged += (_, options) =>
            _ = ApplyMultiAnalysisOptionsAsync(options);
        _multiAnalysisRangeSubscribed = true;
    }

    private void NotifyMultiStateChanged()
    {
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(MultiSelectionSummary));
        OnPropertyChanged(nameof(MultiComparisonText));
        OnPropertyChanged(nameof(MultiReferenceText));
        OnPropertyChanged(nameof(MultiBenchmarkNames));
        OnPropertyChanged(nameof(MultiSelectedPaths));
    }

    // Library's legacy "Load as Base/Comparison" actions remain Pair actions.
    // If one is invoked while Multi is visible, switch back rather than leave
    // the mode selector and chart describing different workspaces.
    partial void OnBaseSessionChanged(SessionAnalysis? value)
    {
        if (IsMultiMode && value is not null)
        {
            SetWorkspaceMode(BenchmarkWorkspaceMode.Pair);
        }
    }

    partial void OnComparisonSessionChanged(SessionAnalysis? value)
    {
        if (IsMultiMode && value is not null)
        {
            SetWorkspaceMode(BenchmarkWorkspaceMode.Pair);
        }

        // Base metric discovery already warms the first Pair session. Prime
        // the second session quietly so switching Pair metrics does not pay a
        // second full-capture scan the first time each metric is selected.
        if (value is not null)
        {
            _ = Task.Run(() => SeriesBuilder.Warm(value));
        }
    }
}
