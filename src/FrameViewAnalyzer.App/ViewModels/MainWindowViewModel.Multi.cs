using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using FrameViewAnalyzer.Analytics;

namespace FrameViewAnalyzer.App.ViewModels;

public enum BenchmarkWorkspaceMode
{
    Pair,
    Multi,
}

/// <summary>One loaded benchmark in the Multi workspace.</summary>
public sealed record MultiBenchmarkSession(
    SessionAnalysis Session,
    string Label,
    bool IsReference)
{
    public string Path => Session.Capture.Path;
}

public partial class MainWindowViewModel
{
    private BenchmarkWorkspaceMode _workspaceMode = BenchmarkWorkspaceMode.Pair;

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

    public string MultiReferenceText => MultiSessions.FirstOrDefault(item => item.IsReference) is { } reference
        ? $"Reference: {reference.Label}"
        : "Reference: not selected";

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

    public string? MultiReferencePath => MultiSessions.FirstOrDefault(item => item.IsReference)?.Path;

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
    /// Loads a checked set of folder captures and activates the declared
    /// reference. Loading is transactional: the current Multi workspace is
    /// left untouched if any selected file fails.
    /// </summary>
    public async Task LoadMultiBenchmarksAsync(
        IReadOnlyList<string> selectedPaths,
        string referencePath)
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
                "Select up to 8 benchmarks so the chart and legend remain readable.");
            return;
        }

        if (!paths.Contains(referencePath, StringComparer.OrdinalIgnoreCase))
        {
            _dialogs.ShowInfo("Multi benchmark", "Choose one selected benchmark as the reference.");
            return;
        }

        try
        {
            var loaded = new List<MultiBenchmarkSession>(paths.Count);
            foreach (var path in paths)
            {
                var session = await LoadSessionAsync(path);
                if (session is null)
                {
                    throw new InvalidOperationException(
                        $"The selected file is not a benchmark log: {System.IO.Path.GetFileName(path)}");
                }

                var label = CardNameOf(session, ManualMetadataOf(session));
                loaded.Add(new MultiBenchmarkSession(
                    session,
                    label,
                    string.Equals(path, referencePath, StringComparison.OrdinalIgnoreCase)));
            }

            var reference = loaded.Single(item => item.IsReference);
            var ordered = new[] { reference }
                .Concat(loaded.Where(item => !item.IsReference))
                .ToList();

            MultiSessions.Clear();
            foreach (var item in ordered)
            {
                MultiSessions.Add(item);
                IndexSession(item.Session);
            }

            SetWorkspaceMode(BenchmarkWorkspaceMode.Multi);
            ActivateMultiWorkspace();
            NotifyMultiStateChanged();
            StatusText = $"MULTI WORKSPACE  ·  {MultiSessions.Count} benchmarks  ·  Reference: {reference.Label}";
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Multi benchmark loading error", error.Message);
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
        if (MultiSessions.Count == 0)
        {
            Chart.Clear();
            AnalysisRange.Attach(null, null);
            AnalysisRange.AnalysisSummaryText =
                "Select two or more benchmarks to start a Multi comparison.";
            StatusText = "MULTI WORKSPACE  ·  Select benchmarks from the capture folder";
            NotifyMultiStateChanged();
            return;
        }

        Chart.SetWorkspace(MultiSessions.Select(item => new ChartWorkspaceSession(
            item.Session,
            item.Label,
            item.IsReference)).ToList());

        // Phase 3 keeps the existing pair-only range editor disabled in Multi
        // so changing a slider can never silently re-analyze only two of N
        // sessions. The next Multi statistics phase will make this N-aware.
        AnalysisRange.Attach(null, null);
        AnalysisRange.AnalysisSummaryText =
            $"Multi workspace loaded with {MultiSessions.Count} benchmarks. "
            + "Range controls are temporarily locked while the N-way analysis path is being integrated.";
        NotifyMultiStateChanged();
    }

    private void NotifyMultiStateChanged()
    {
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(MultiSelectionSummary));
        OnPropertyChanged(nameof(MultiReferenceText));
        OnPropertyChanged(nameof(MultiBenchmarkNames));
        OnPropertyChanged(nameof(MultiSelectedPaths));
        OnPropertyChanged(nameof(MultiReferencePath));
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
    }
}
