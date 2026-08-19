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
    private readonly BusyState _busy;

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

    public bool CanCompareSelected => SelectedCount is >= 2 and <= MaxMultiSelection;

    /// <summary>Selection valid AND the window idle; the footer button binding.</summary>
    public bool CanCompareSelectedNow => CanCompareSelected && !IsBusy;

    public string SelectionSummary => SelectedCount switch
    {
        0 => "Select 2–8 benchmarks for Multi comparison.",
        1 => "1 benchmark selected · select at least one more.",
        MaxMultiSelection => $"{MaxMultiSelection} benchmarks selected · maximum reached.",
        _ => $"{SelectedCount} benchmarks selected.",
    };

    public event Action<string>? LoadBaseRequested;

    public event Action<string>? LoadComparisonRequested;

    /// <summary>Recent two-capture comparison request; remains a Pair action.</summary>
    public event Action<string, string>? CompareRequested;

    /// <summary>Selected Library captures to load into the shared Multi workspace.</summary>
    public event Action<IReadOnlyList<string>>? CompareSelectedRequested;

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
        BusyState? busy = null)
    {
        _store = store;
        _manualStore = manualStore;
        _scanner = scanner;
        _captureDirectory = captureDirectory;
        _busy = busy ?? new BusyState();
        _busy.BusyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanCompareSelectedNow));
            LoadBaseCommand.NotifyCanExecuteChanged();
            LoadComparisonCommand.NotifyCanExecuteChanged();
            ComparePairCommand.NotifyCanExecuteChanged();
            CompareSelectedCommand.NotifyCanExecuteChanged();
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
        _busy.RunAsync("Loading benchmark library...", RefreshCoreAsync);

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

        var paths = _selectedIdentities
            .Select(identity => _library.Records.TryGetValue(identity, out var record) ? record : null)
            .Where(record => record is { Available: true })
            .Select(record => record!.SourcePath)
            .ToList();
        if (paths.Count is >= 2 and <= MaxMultiSelection)
        {
            CompareSelectedRequested?.Invoke(paths);
        }
    }

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
        OnPropertyChanged(nameof(SelectionSummary));
        CompareSelectedCommand.NotifyCanExecuteChanged();
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
