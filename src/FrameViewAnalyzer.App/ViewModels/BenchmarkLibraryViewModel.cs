using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.App.Busy;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// Context in which the shared benchmark browser was opened.
/// Library keeps the management workflow; the other modes turn the same
/// indexed/searchable surface into the corresponding Pair or Multi picker.
/// </summary>
public enum BenchmarkBrowserMode
{
    Library,
    PairBase,
    PairComparison,
    Multi,
}

public sealed record LibraryRow(
    LibraryRecord Record,
    ManualMetadata? Manual,
    string Title,
    string Subtitle,
    string Stamp,
    bool Available,
    bool IsSelected);

public sealed record RecentPairRow(
    string TitleA,
    string TitleB,
    LibraryRecord RecordA,
    LibraryRecord RecordB);

/// <summary>
/// Benchmark Library browser state: search, filters, sorting, row selection,
/// recent Pair comparisons, and non-destructive record removal. Pure
/// search/filter/sort logic lives in Analytics.LibrarySearch.
/// </summary>
public partial class BenchmarkLibraryViewModel : ObservableObject
{
    public const string AllValue = "All";
    public const int MaxMultiSelection = 8;

    private readonly ILibraryStore _store;
    private readonly IManualMetadataStore _manualStore;
    private readonly CaptureFolderScanner _scanner;
    private readonly LibraryIndexer _indexer = new();
    private readonly string? _captureDirectory;
    private readonly List<string> _selectedIdentities = [];
    private readonly IReadOnlyList<string> _initialSelectedPaths;
    private readonly BusyState _busy;
    private readonly BenchmarkBrowserMode _mode;
    private bool _initialSelectionApplied;

    private LibraryModel _library = new();
    private IReadOnlyDictionary<string, ManualMetadata> _manualLookup =
        new Dictionary<string, ManualMetadata>();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _tagsText = string.Empty;

    [ObservableProperty]
    private string _selectedGame = AllValue;

    [ObservableProperty]
    private string _selectedResolution = AllValue;

    [ObservableProperty]
    private string _selectedGpu = AllValue;

    [ObservableProperty]
    private bool _sortByDate = true;

    [ObservableProperty]
    private string _countText = string.Empty;

    public ObservableCollection<string> GameOptions { get; } = [AllValue];

    public ObservableCollection<string> ResolutionOptions { get; } = [AllValue];

    public ObservableCollection<string> GpuOptions { get; } = [AllValue];

    public ObservableCollection<LibraryRow> Rows { get; } = [];

    public ObservableCollection<RecentPairRow> RecentPairs { get; } = [];

    public int SelectedCount => _selectedIdentities.Count;

    public BenchmarkBrowserMode Mode => _mode;

    public bool IsLibraryMode => _mode == BenchmarkBrowserMode.Library;

    public bool IsSelectionMode => !IsLibraryMode;

    public bool IsPairSelectionMode =>
        _mode is BenchmarkBrowserMode.PairBase or BenchmarkBrowserMode.PairComparison;

    public bool IsMultiSelectionMode => _mode == BenchmarkBrowserMode.Multi;

    public bool ShowQuickPairActions => IsLibraryMode;

    public bool ShowLibraryActions => IsLibraryMode;

    public bool ShowBrowseAction => IsPairSelectionMode;

    public string WindowTitle => _mode switch
    {
        BenchmarkBrowserMode.PairBase => "Select base benchmark",
        BenchmarkBrowserMode.PairComparison => "Select comparison benchmark",
        BenchmarkBrowserMode.Multi => "Select benchmarks",
        _ => "Benchmark Library",
    };

    public string HeaderLabel => _mode switch
    {
        BenchmarkBrowserMode.PairBase => "PAIR · SELECT BASE",
        BenchmarkBrowserMode.PairComparison => "PAIR · SELECT COMPARISON",
        BenchmarkBrowserMode.Multi => "MULTI BENCHMARK",
        _ => "BENCHMARK LIBRARY",
    };

    public string HeaderDescription => _mode switch
    {
        BenchmarkBrowserMode.PairBase =>
            "Choose one indexed capture as the Pair base, or browse to another CSV.",
        BenchmarkBrowserMode.PairComparison =>
            "Choose one indexed capture as the Pair comparison, or browse to another CSV.",
        BenchmarkBrowserMode.Multi =>
            "Select 2–8 captures. Every selected benchmark is compared equally; there is no Base or Reference.",
        _ =>
            "Search and manage indexed captures, load Pair slots, or select 2–8 captures for Multi.",
    };

    public string CaptureFolder => string.IsNullOrWhiteSpace(_captureDirectory)
        ? "Capture folder not configured"
        : _captureDirectory!;

    public string PrimaryActionText => _mode switch
    {
        BenchmarkBrowserMode.PairBase => "Load as Base",
        BenchmarkBrowserMode.PairComparison => "Load as Comparison",
        BenchmarkBrowserMode.Multi => "Load selected",
        _ => "Compare selected",
    };

    public string SelectionCheckBoxToolTip => IsPairSelectionMode
        ? "Select this benchmark"
        : "Select for Multi comparison";

    public string FooterHelpText => _mode switch
    {
        BenchmarkBrowserMode.PairBase =>
            "One selection is required. Browse CSV keeps direct file loading available.",
        BenchmarkBrowserMode.PairComparison =>
            "One selection is required. Browse CSV keeps direct file loading available.",
        BenchmarkBrowserMode.Multi =>
            "Select between 2 and 8 available captures. Existing Multi selections are preserved when possible.",
        _ =>
            "Remove hides a Library record without deleting its CSV. Base and Comparison remain quick Pair actions.",
    };

    public bool CanCompareSelected => SelectedCount is >= 2 and <= MaxMultiSelection;

    /// <summary>Selection valid AND the window idle; the Library footer button binding.</summary>
    public bool CanCompareSelectedNow => CanCompareSelected && !IsBusy;

    public bool CanConfirmSelection => _mode switch
    {
        BenchmarkBrowserMode.PairBase or BenchmarkBrowserMode.PairComparison => SelectedCount == 1,
        BenchmarkBrowserMode.Multi => CanCompareSelected,
        _ => false,
    };

    /// <summary>Context selection valid AND the browser idle.</summary>
    public bool CanConfirmSelectionNow => CanConfirmSelection && !IsBusy;

    public string SelectionSummary
    {
        get
        {
            if (IsPairSelectionMode)
            {
                return SelectedCount == 0
                    ? "Select one benchmark."
                    : "1 benchmark selected · ready to load.";
            }

            return SelectedCount switch
            {
                0 => "Select 2–8 benchmarks for Multi comparison.",
                1 => "1 benchmark selected · select at least one more.",
                MaxMultiSelection => $"{MaxMultiSelection} benchmarks selected · maximum reached.",
                _ => $"{SelectedCount} benchmarks selected.",
            };
        }
    }

    public event Action<string>? LoadBaseRequested;

    public event Action<string>? LoadComparisonRequested;

    /// <summary>Recent two-capture comparison request; remains a Pair action.</summary>
    public event Action<string, string>? CompareRequested;

    /// <summary>Selected Library captures to load into the shared Multi workspace.</summary>
    public event Action<IReadOnlyList<string>>? CompareSelectedRequested;

    /// <summary>Contextual Pair/Multi selection confirmed in the shared browser.</summary>
    public event Action<BenchmarkBrowserMode, IReadOnlyList<string>>? SelectionConfirmedRequested;

    /// <summary>
    /// Raised before a record is removed so the view can ask for explicit
    /// confirmation. Confirmed removal is performed by RemoveFromLibrary.
    /// </summary>
    public event Action<LibraryRow>? RemoveRequested;

    public BenchmarkLibraryViewModel(
        ILibraryStore store,
        IManualMetadataStore manualStore,
        CaptureFolderScanner scanner,
        string? captureDirectory = null,
        BusyState? busy = null,
        BenchmarkBrowserMode mode = BenchmarkBrowserMode.Library,
        IReadOnlyList<string>? initiallySelectedPaths = null)
    {
        _store = store;
        _manualStore = manualStore;
        _scanner = scanner;
        _captureDirectory = captureDirectory;
        _mode = mode;
        _initialSelectedPaths = (initiallySelectedPaths ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxMultiSelection)
            .ToList();
        _busy = busy ?? new BusyState();
        _busy.BusyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanCompareSelectedNow));
            OnPropertyChanged(nameof(CanConfirmSelectionNow));
            LoadBaseCommand.NotifyCanExecuteChanged();
            LoadComparisonCommand.NotifyCanExecuteChanged();
            ComparePairCommand.NotifyCanExecuteChanged();
            CompareSelectedCommand.NotifyCanExecuteChanged();
            ConfirmSelectionCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>True while the Library window is busy; drives the footer button guards.</summary>
    public bool IsBusy => _busy.IsBusy;

    /// <summary>The busy state of the owning Library window (loading, imports, exports).</summary>
    public BusyState Busy => _busy;

    /// <summary>Library actions are blocked while any operation is in flight.</summary>
    private bool CanInteract => !_busy.IsBusy;

    /// <summary>
    /// Loads the persisted index, refreshes it against the capture folder,
    /// and rebuilds the browser. Unknown-version stores load empty and are
    /// left untouched; a failed save never breaks browsing.
    /// </summary>
    public Task RefreshAsync() =>
        _busy.RunAsync("Loading benchmark library", RefreshCoreAsync);

    private async Task RefreshCoreAsync()
    {
        _library = _store.Load();
        _manualLookup = _manualStore.Load();
        if (!string.IsNullOrEmpty(_captureDirectory))
        {
            // Deliberately NO ConfigureAwait(false): the continuation rebuilds
            // ObservableCollections that are bound to ComboBoxes/ItemsControls
            // in the Library window. WPF CollectionViews throw
            // NotSupportedException when their source collection changes from
            // a non-Dispatcher thread, so the rebuild must resume on the UI
            // thread that started the refresh.
            await _indexer.RefreshAsync(_library, _captureDirectory, _scanner);
            TrySave();
        }

        ApplyInitialSelection();
        _selectedIdentities.RemoveAll(identity =>
            !_library.Records.TryGetValue(identity, out var record) || !record.Available);
        NotifySelectionChanged();
        RebuildOptions();
        RebuildRows();
        RebuildRecentPairs();
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void LoadBase(LibraryRow? row)
    {
        if (row is { Available: true })
        {
            LoadBaseRequested?.Invoke(row.Record.SourcePath);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void LoadComparison(LibraryRow? row)
    {
        if (row is { Available: true })
        {
            LoadComparisonRequested?.Invoke(row.Record.SourcePath);
        }
    }

    [RelayCommand]
    private void ToggleSelected(LibraryRow? row)
    {
        if (row is not { Available: true })
        {
            return;
        }

        var identity = row.Record.Identity;
        if (_selectedIdentities.Contains(identity, StringComparer.Ordinal))
        {
            _selectedIdentities.Remove(identity);
        }
        else if (IsPairSelectionMode)
        {
            _selectedIdentities.Clear();
            _selectedIdentities.Add(identity);
        }
        else if (_selectedIdentities.Count < MaxMultiSelection)
        {
            _selectedIdentities.Add(identity);
        }

        NotifySelectionChanged();
        RebuildRows();
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void CompareSelected()
    {
        if (!CanCompareSelected)
        {
            return;
        }

        var paths = SelectedAvailablePaths();
        if (paths.Count is >= 2 and <= MaxMultiSelection)
        {
            CompareSelectedRequested?.Invoke(paths);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void ConfirmSelection()
    {
        if (!CanConfirmSelection)
        {
            return;
        }

        var paths = SelectedAvailablePaths();
        var valid = IsPairSelectionMode
            ? paths.Count == 1
            : paths.Count is >= 2 and <= MaxMultiSelection;
        if (valid)
        {
            SelectionConfirmedRequested?.Invoke(_mode, paths);
        }
    }

    private IReadOnlyList<string> SelectedAvailablePaths() =>
        _selectedIdentities
            .Select(identity => _library.Records.TryGetValue(identity, out var record) ? record : null)
            .Where(record => record is { Available: true })
            .Select(record => record!.SourcePath)
            .ToList();

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void ComparePair(RecentPairRow? pair)
    {
        if (pair is { RecordA.Available: true, RecordB.Available: true })
        {
            CompareRequested?.Invoke(pair.RecordA.SourcePath, pair.RecordB.SourcePath);
        }
    }

    [RelayCommand]
    private void RequestRemove(LibraryRow? row)
    {
        if (row is not null)
        {
            RemoveRequested?.Invoke(row);
        }
    }

    /// <summary>
    /// Removes only the Library index entry. The source CSV and manual
    /// metadata stay untouched; the identity is persisted as ignored so a
    /// folder refresh cannot immediately recreate the row.
    /// </summary>
    public void RemoveFromLibrary(LibraryRow row)
    {
        var identity = row.Record.Identity;
        if (!_library.Records.Remove(identity))
        {
            return;
        }

        _library.IgnoredIdentities.Add(identity);
        _library.RecentComparisons.RemoveAll(pair =>
            pair.Base == identity || pair.Comparison == identity);
        _selectedIdentities.Remove(identity);
        NotifySelectionChanged();

        TrySave();
        RebuildOptions();
        RebuildRows();
        RebuildRecentPairs();
    }

    partial void OnSearchTextChanged(string value) => RebuildRows();

    partial void OnTagsTextChanged(string value) => RebuildRows();

    partial void OnSelectedGameChanged(string value) => RebuildRows();

    partial void OnSelectedResolutionChanged(string value) => RebuildRows();

    partial void OnSelectedGpuChanged(string value) => RebuildRows();

    partial void OnSortByDateChanged(bool value) => RebuildRows();

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanCompareSelected));
        OnPropertyChanged(nameof(CanCompareSelectedNow));
        OnPropertyChanged(nameof(CanConfirmSelection));
        OnPropertyChanged(nameof(CanConfirmSelectionNow));
        OnPropertyChanged(nameof(SelectionSummary));
        CompareSelectedCommand.NotifyCanExecuteChanged();
        ConfirmSelectionCommand.NotifyCanExecuteChanged();
    }

    private void ApplyInitialSelection()
    {
        if (_initialSelectionApplied)
        {
            return;
        }

        _initialSelectionApplied = true;
        foreach (var path in _initialSelectedPaths)
        {
            var record = _library.Records.Values.FirstOrDefault(candidate =>
                candidate.Available
                && string.Equals(candidate.SourcePath, path, StringComparison.OrdinalIgnoreCase));
            if (record is null || _selectedIdentities.Contains(record.Identity, StringComparer.Ordinal))
            {
                continue;
            }

            _selectedIdentities.Add(record.Identity);
            if (IsPairSelectionMode || _selectedIdentities.Count == MaxMultiSelection)
            {
                break;
            }
        }
    }

    private void RebuildOptions()
    {
        static void Replace(ObservableCollection<string> options, IEnumerable<string> values)
        {
            options.Clear();
            options.Add(AllValue);
            foreach (var value in values)
            {
                options.Add(value);
            }
        }

        Replace(
            GameOptions,
            _library.Records.Values
                .Select(record => LibrarySearch.LibraryGame(
                    record,
                    _manualLookup.TryGetValue(record.Identity, out var manual) ? manual : null))
                .Where(game => game.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(game => game, StringComparer.CurrentCultureIgnoreCase));
        Replace(
            ResolutionOptions,
            _library.Records.Values
                .Select(record => record.Resolution)
                .Where(resolution => resolution.Length > 0 && resolution != "--")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(resolution => resolution, StringComparer.Ordinal));
        Replace(
            GpuOptions,
            _library.Records.Values
                .Select(record => record.Gpu)
                .Where(gpu => gpu.Length > 0 && gpu != "--")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(gpu => gpu, StringComparer.Ordinal));

        SelectedGame = GameOptions.Contains(SelectedGame) ? SelectedGame : AllValue;
        SelectedResolution = ResolutionOptions.Contains(SelectedResolution) ? SelectedResolution : AllValue;
        SelectedGpu = GpuOptions.Contains(SelectedGpu) ? SelectedGpu : AllValue;
    }

    private void RebuildRows()
    {
        var tags = TagsText.Split(',')
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .ToList();
        var searched = LibrarySearch.SearchRecords(_library.Records.Values, SearchText, _manualLookup);
        var filtered = LibrarySearch.FilterRecords(
            searched,
            _manualLookup,
            tags: tags,
            resolution: SelectedResolution == AllValue ? null : SelectedResolution,
            gpu: SelectedGpu == AllValue ? null : SelectedGpu,
            game: SelectedGame == AllValue ? null : SelectedGame);
        var sorted = LibrarySearch.SortRecords(
            filtered,
            SortByDate ? LibraryConstants.SortDate : LibraryConstants.SortName);

        Rows.Clear();
        foreach (var record in sorted)
        {
            var manual = _manualLookup.TryGetValue(record.Identity, out var value) ? value : null;
            Rows.Add(new LibraryRow(
                record,
                manual,
                LibrarySearch.LibraryRowTitle(record, manual),
                LibrarySearch.LibraryRowSubtitle(record, manual),
                LibrarySearch.LibraryStamp(record),
                record.Available,
                _selectedIdentities.Contains(record.Identity, StringComparer.Ordinal)));
        }

        CountText = $"{Rows.Count} record(s)";
    }

    private void RebuildRecentPairs()
    {
        RecentPairs.Clear();
        foreach (var (first, second) in _library.RecentComparisons)
        {
            if (!_library.Records.TryGetValue(first, out var recordA)
                || !_library.Records.TryGetValue(second, out var recordB))
            {
                continue;
            }

            var manualA = _manualLookup.TryGetValue(first, out var valueA) ? valueA : null;
            var manualB = _manualLookup.TryGetValue(second, out var valueB) ? valueB : null;
            RecentPairs.Add(new RecentPairRow(
                LibrarySearch.LibraryRowTitle(recordA, manualA),
                LibrarySearch.LibraryRowTitle(recordB, manualB),
                recordA,
                recordB));
        }
    }

    private void TrySave()
    {
        try
        {
            _store.Save(_library);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            // Browsing must survive store failures (e.g. unknown versions).
        }
    }
}
