using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Busy;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Math;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>One quick-capture option in the capture dropdown.</summary>
public sealed record CaptureOption(string Path, string Display);

/// <summary>
/// Main-window state: theme mode, the Base/Comparison session pair, session
/// cards, capture loading, status line, and the application version.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IThemeService _themes;
    private readonly IFrameViewCsvReader _reader;
    private readonly ICaptureAnalysisService _analysis;
    private readonly IDialogService _dialogs;
    private readonly IRangeAnalysisService _rangeAnalysis;
    private readonly IManualMetadataStore _metadataStore;
    private readonly ILibraryStore _libraryStore;
    private readonly CaptureFolderScanner _scanner;
    private readonly BusyState _busy;

    [ObservableProperty]
    private string _appearanceMode = "dark";

    [ObservableProperty]
    private bool _isDark = true;

    [ObservableProperty]
    private bool _isLight;

    [ObservableProperty]
    private string _statusText = "READY  ·  Ctrl+O to open a capture";

    [ObservableProperty]
    private SessionAnalysis? _baseSession;

    [ObservableProperty]
    private SessionAnalysis? _comparisonSession;

    [ObservableProperty]
    private bool _hasComparison;

    [ObservableProperty]
    private bool _hasBaseSession;

    [ObservableProperty]
    private string _baseSessionName = "No capture loaded";

    [ObservableProperty]
    private string _baseMetaLine = "Load a FrameView *_Log.csv capture";

    [ObservableProperty]
    private string _comparisonSessionName = "No comparison loaded";

    [ObservableProperty]
    private string _comparisonMetaLine = "Load a second capture to compare performance.";

    [ObservableProperty]
    private string _comparisonDeltaLine = string.Empty;

    [ObservableProperty]
    private string _baseLoadButtonText = "Load capture...";

    [ObservableProperty]
    private string _comparisonLoadButtonText = "Load comparison...";

    [ObservableProperty]
    private string _captureFolderPath = string.Empty;

    [ObservableProperty]
    private CaptureOption? _selectedCapture;

    public ObservableCollection<CaptureOption> Captures { get; } = [];

    public ChartViewModel Chart { get; }

    /// <summary>Analysis-range controls (GPU threshold / trim / transitions).</summary>
    public AnalysisRangeViewModel AnalysisRange { get; }

    /// <summary>Main-window busy state (shared with the window's status bar and overlay).</summary>
    public BusyState Busy => _busy;

    /// <summary>
    /// Raised when an Analyze action wants the chart to jump somewhere; a
    /// null range means "Full capture" (reset to the complete series).
    /// </summary>
    public event EventHandler<TimeRange?>? AnalyzeRangeRequested;

    /// <summary>Raised when the manual metadata editor should open for a session.</summary>
    public event EventHandler<MetadataEditorRequest>? MetadataEditorRequested;

    /// <summary>Raised when a FrameView_Summary.csv should open in the summary table.</summary>
    public event EventHandler<string>? SummaryRequested;

    /// <summary>Raised when a keyboard shortcut requests the PNG report export.</summary>
    public event EventHandler? ExportPngReportRequested;

    /// <summary>Raised when a keyboard shortcut requests the Statistics CSV export.</summary>
    public event EventHandler? ExportStatisticsCsvRequested;

    /// <summary>Raised when a keyboard shortcut requests the Benchmark JSON export.</summary>
    public event EventHandler? ExportBenchmarkJsonRequested;

    public string VersionText => $"FrameView Analyzer v{ReleaseInfo.InformationalVersion}";

    public MainWindowViewModel(
        ISettingsStore settings,
        IThemeService themes,
        ChartViewModel chart,
        IFrameViewCsvReader reader,
        ICaptureAnalysisService analysis,
        IRangeAnalysisService rangeAnalysis,
        IManualMetadataStore metadataStore,
        ILibraryStore libraryStore,
        CaptureFolderScanner scanner,
        IDialogService dialogs,
        BusyState busy)
    {
        _settings = settings;
        _themes = themes;
        _reader = reader;
        _analysis = analysis;
        _dialogs = dialogs;
        _rangeAnalysis = rangeAnalysis;
        _metadataStore = metadataStore;
        _libraryStore = libraryStore;
        _scanner = scanner;
        _busy = busy;
        Chart = chart;
        AnalysisRange = new AnalysisRangeViewModel();
        AnalysisRange.OptionsChanged += (_, options) => _ = ApplyAnalysisOptionsAsync(options);
        _busy.BusyChanged += (_, _) =>
        {
            LoadBaseCommand.NotifyCanExecuteChanged();
            LoadComparisonCommand.NotifyCanExecuteChanged();
            RefreshCapturesCommand.NotifyCanExecuteChanged();
            ExportPngReportCommand.NotifyCanExecuteChanged();
            ExportStatisticsCsvCommand.NotifyCanExecuteChanged();
            ExportBenchmarkJsonCommand.NotifyCanExecuteChanged();
        };

        var mode = Normalize(settings.Load().AppearanceMode);
        _appearanceMode = mode;
        _isDark = mode == "dark";
        _isLight = mode == "light";
        CaptureFolderPath = settings.Load().CaptureDirectory
            ?? PlatformFolders.FrameViewDirectory();
    }

    /// <summary>True when no operation is in flight; guards commands that mutate the workspace.</summary>
    private bool CanLoadCapture => !_busy.IsBusy;

    partial void OnSelectedCaptureChanged(CaptureOption? value)
    {
        // Switching captures mid-operation would race the loader.
        if (value is not null && !_busy.IsBusy)
        {
            _ = LoadBaseFromPathAsync(value.Path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadCapture))]
    private Task RefreshCapturesAsync() =>
        _busy.RunAsync("Scanning capture folder...", RefreshCapturesCoreAsync);

    private async Task RefreshCapturesCoreAsync()
    {
        try
        {
            var directory = _settings.Load().CaptureDirectory
                ?? PlatformFolders.FrameViewDirectory();
            CaptureFolderPath = directory;
            Captures.Clear();
            SelectedCapture = null;
            if (!Directory.Exists(directory))
            {
                StatusText = "CAPTURE FOLDER MISSING  ·  " + directory;
                return;
            }

            foreach (var path in CaptureFolderScanner.DiscoverLogFiles(directory).Take(500))
            {
                var info = await _scanner.ReadCaptureInfoAsync(path);
                if (info is null)
                {
                    continue;
                }

                Captures.Add(new CaptureOption(
                    info.Path,
                    CaptureFileNaming.SanitizeDisplayName(info.Name)));
            }

            StatusText = Captures.Count == 0
                ? "READY  ·  No FrameView logs in the folder"
                : $"READY  ·  {Captures.Count:N0} capture(s) in the folder";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            StatusText = "READY  ·  Capture folder unavailable";
        }
    }

    [RelayCommand]
    private async Task ChooseCaptureFolderAsync()
    {
        var folder = _dialogs.PickFolder(CaptureFolderPath);
        if (folder is null)
        {
            return;
        }

        await PersistCaptureFolderAsync(folder);
    }

    [RelayCommand]
    private Task ResetCaptureFolderAsync() =>
        PersistCaptureFolderAsync(PlatformFolders.FrameViewDirectory());

    private async Task PersistCaptureFolderAsync(string folder)
    {
        _settings.Save(_settings.Load() with { CaptureDirectory = folder });
        CaptureFolderPath = folder;
        await RefreshCapturesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanLoadCapture))]
    private async Task LoadBaseAsync()
    {
        var path = _dialogs.PickCsvFile(null);
        if (path is null)
        {
            return;
        }

        await LoadBaseFromPathAsync(path);
    }

    /// <summary>Loads a capture by path (Library "Load as Base").</summary>
    public Task LoadBaseFromPathAsync(string path) =>
        _busy.RunAsync("Loading base capture...", () => LoadBaseFromPathCoreAsync(path));

    private async Task LoadBaseFromPathCoreAsync(string path)
    {
        try
        {
            var session = await LoadSessionAsync(path);
            if (session is null)
            {
                return;
            }

            BaseSession = session;
            RefreshSessionCards();
            Chart.SetSessions(BaseSession, ComparisonSession);
            AnalysisRange.Attach(BaseSession, ComparisonSession);
            IndexSession(session);
            StatusText = $"CAPTURE OPENED  ·  {session.Capture.DisplayName}  ·  {ResolutionOf(session)}";
        }
        catch (Exception error)
        {
            _dialogs.ShowError("CSV loading error", error.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadCapture))]
    private async Task LoadComparisonAsync()
    {
        if (BaseSession is null)
        {
            _dialogs.ShowInfo(
                "Benchmark Library",
                "Load a base session before loading a comparison.");
            return;
        }

        var path = _dialogs.PickCsvFile(null);
        if (path is null)
        {
            return;
        }

        await LoadComparisonFromPathAsync(path);
    }

    /// <summary>Loads a comparison by path (Library "Load as Comparison").</summary>
    public Task LoadComparisonFromPathAsync(string path) =>
        _busy.RunAsync("Loading comparison capture...", () => LoadComparisonFromPathCoreAsync(path));

    private async Task LoadComparisonFromPathCoreAsync(string path)
    {
        if (BaseSession is null)
        {
            _dialogs.ShowInfo(
                "Benchmark Library",
                "Load a base session before loading a comparison.");
            return;
        }

        try
        {
            var session = await LoadSessionAsync(path);
            if (session is null)
            {
                return;
            }

            ComparisonSession = session;
            RefreshSessionCards();
            Chart.SetSessions(BaseSession, ComparisonSession);
            AnalysisRange.Attach(BaseSession, ComparisonSession);
            IndexSession(session);
            RecordComparison();
            StatusText = $"COMPARISON OPENED  ·  {session.Capture.DisplayName}  ·  {ResolutionOf(session)}";
        }
        catch (Exception error)
        {
            _dialogs.ShowError("CSV loading error", error.Message);
        }
    }

    private void IndexSession(SessionAnalysis session)
    {
        try
        {
            var identity = CaptureIdentityResolver.TryBuild(session.Capture.Path);
            if (identity is null)
            {
                return;
            }

            var metadata = session.Metadata;
            var info = new CaptureInfo(
                session.Capture.Path,
                Path.GetFileName(session.Capture.Path),
                metadata?.Application ?? string.Empty,
                metadata?.Resolution ?? string.Empty,
                metadata?.Gpu ?? string.Empty,
                metadata?.Cpu ?? string.Empty,
                DurationSeconds: null);

            var library = _libraryStore.Load();
            var indexer = new LibraryIndexer();
            if (indexer.Upsert(library, info, LibraryUpdater.NowIso()))
            {
                LibraryUpdater.UpdateStats(library, session, identity);
                _libraryStore.Save(library);
            }
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            // Library bookkeeping is best-effort; loading must never fail.
        }
    }

    private void RecordComparison()
    {
        try
        {
            var baseIdentity = BaseSession is null
                ? null
                : CaptureIdentityResolver.TryBuild(BaseSession.Capture.Path);
            var comparisonIdentity = ComparisonSession is null
                ? null
                : CaptureIdentityResolver.TryBuild(ComparisonSession.Capture.Path);
            if (baseIdentity is null || comparisonIdentity is null)
            {
                return;
            }

            var library = _libraryStore.Load();
            library.RecentComparisons.Clear();
            library.RecentComparisons.AddRange(
                LibraryUpdater.WithComparison(
                    library.RecentComparisons,
                    baseIdentity,
                    comparisonIdentity));
            _libraryStore.Save(library);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            // Library bookkeeping is best-effort.
        }
    }

    [RelayCommand]
    private void RemoveComparison()
    {
        if (ComparisonSession is null)
        {
            return;
        }

        ComparisonSession = null;
        RefreshSessionCards();
        Chart.SetSessions(BaseSession, null);
        AnalysisRange.Attach(BaseSession, null);
        StatusText = "COMPARISON SESSION REMOVED";
    }

    [RelayCommand]
    private void RemoveBase()
    {
        if (BaseSession is null)
        {
            return;
        }

        var (baseSession, comparisonSession, promoted) = SessionSlots.Remove(
            BaseSession, ComparisonSession, SessionSlots.BaseSlot);
        BaseSession = baseSession;
        ComparisonSession = comparisonSession;
        RefreshSessionCards();
        Chart.SetSessions(BaseSession, ComparisonSession);
        AnalysisRange.Attach(BaseSession, ComparisonSession);
        StatusText = promoted
            ? "BASE SESSION REMOVED  ·  The comparison is now the base session"
            : "READY  ·  No captures loaded";
    }

    /// <summary>
    /// Monotonic request generation: every re-analysis request (Pair or
    /// Multi) bumps it, and a result is only applied while its generation is
    /// still the newest. An older overlapping request completing afterwards
    /// is discarded, so it can never overwrite a newer user-requested
    /// analysis state.
    /// </summary>
    private int _analysisGeneration;

    /// <summary>
    /// Re-analyzes the loaded Base (and Comparison) with the new analysis
    /// options, rebuilds the chart series (preserving the selected metric),
    /// refreshes the KPI tiles, and updates the library digest together with
    /// the persisted analysis options.
    /// </summary>
    public async Task ApplyAnalysisOptionsAsync(AnalysisOptions options)
    {
        if (BaseSession is null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _analysisGeneration);
        try
        {
            var previousBase = BaseSession;
            var previousComparison = ComparisonSession;
            var baseSession = await _busy.RunOnThreadPoolAsync(
                "Processing capture data...",
                () => _analysis.Reanalyze(previousBase, options));
            var comparisonSession = previousComparison is null
                ? null
                : await _busy.RunOnThreadPoolAsync(
                    "Processing capture data...",
                    () => _analysis.Reanalyze(previousComparison, options));

            // A newer request superseded this one while it was computing;
            // the stale result must never overwrite the newer state.
            if (generation != Volatile.Read(ref _analysisGeneration))
            {
                return;
            }

            BaseSession = baseSession;
            ComparisonSession = comparisonSession;
            RefreshSessionCards();
            Chart.SetSessions(BaseSession, ComparisonSession);
            AnalysisRange.Attach(BaseSession, ComparisonSession);
            IndexSession(baseSession);
            if (comparisonSession is not null)
            {
                IndexSession(comparisonSession);
            }

            StatusText = $"REANALYZED  ·  {baseSession.Capture.DisplayName}";
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            if (generation == Volatile.Read(ref _analysisGeneration))
            {
                // Library bookkeeping failures never break re-analysis.
                StatusText = "REANALYZED";
            }
        }
    }

    [RelayCommand]
    private void ChangeAppearance(string mode)
    {
        var normalized = Normalize(mode);
        if (normalized == AppearanceMode)
        {
            return;
        }

        AppearanceMode = normalized;
        IsDark = normalized == "dark";
        IsLight = normalized == "light";
        _themes.Apply(normalized);

        var settings = _settings.Load();
        _settings.Save(settings with { AppearanceMode = normalized });
    }

    partial void OnIsDarkChanged(bool value)
    {
        if (value && AppearanceMode != "dark")
        {
            ChangeAppearance("dark");
        }
    }

    partial void OnIsLightChanged(bool value)
    {
        if (value && AppearanceMode != "light")
        {
            ChangeAppearance("light");
        }
    }

    partial void OnHasBaseSessionChanged(bool value)
    {
        ExportPngReportCommand.NotifyCanExecuteChanged();
        ExportStatisticsCsvCommand.NotifyCanExecuteChanged();
        ExportBenchmarkJsonCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Exports are blocked while any operation is in flight.</summary>
    private bool CanExportStatistics => HasBaseSession && !_busy.IsBusy;

    [RelayCommand(CanExecute = nameof(CanExportStatistics))]
    private void ExportPngReport() => ExportPngReportRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanExportStatistics))]
    private void ExportStatisticsCsv() => ExportStatisticsCsvRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanExportStatistics))]
    private void ExportBenchmarkJson() => ExportBenchmarkJsonRequested?.Invoke(this, EventArgs.Empty);

    private async Task<SessionAnalysis?> LoadSessionAsync(string path)
    {
        var capture = await _busy.RunAsync("Reading capture data...", () => _reader.LoadCaptureAsync(path));
        var kind = _reader.DetectKind(capture.Headers, Path.GetFileName(path));
        if (kind == CsvKind.Summary)
        {
            // Summary CSVs never occupy the Base/Comparison slots and never
            // run log analytics; they open the read-only summary table.
            SummaryRequested?.Invoke(this, path);
            return null;
        }

        // Sample parsing and statistics are CPU-bound; run them off the UI
        // thread so the busy presentation keeps animating.
        return await _busy.RunOnThreadPoolAsync("Processing capture data...", () => _analysis.Analyze(capture));
    }

    private void RefreshSessionCards()
    {
        var baseManual = ManualMetadataOf(BaseSession);
        var comparisonManual = ManualMetadataOf(ComparisonSession);

        BaseSessionName = BaseSession is null
            ? "No capture loaded"
            : CardNameOf(BaseSession, baseManual);
        BaseMetaLine = BaseSession is null
            ? "Load a FrameView *_Log.csv capture"
            : (baseManual?.ConfigLine ?? MetaLineOf(BaseSession));
        BaseLoadButtonText = BaseSession is null ? "Load capture..." : "Change...";

        ComparisonSessionName = ComparisonSession is null
            ? "No comparison loaded"
            : CardNameOf(ComparisonSession, comparisonManual);
        ComparisonMetaLine = ComparisonSession is null
            ? "Load a second capture to compare performance."
            : (comparisonManual?.ConfigLine ?? MetaLineOf(ComparisonSession));
        ComparisonLoadButtonText = ComparisonSession is null ? "Load comparison..." : "Change...";

        ComparisonDeltaLine = string.Empty;
        if (BaseSession is not null && ComparisonSession is not null)
        {
            var baseValues = SeriesBuilder.Build(BaseSession, "fps").Y;
            var comparisonValues = SeriesBuilder.Build(ComparisonSession, "fps").Y;
            var baseAverage = Statistics.Mean(baseValues);
            var comparisonAverage = Statistics.Mean(comparisonValues);
            if (baseAverage is > 0 && comparisonAverage is not null)
            {
                var deltaPercent = (comparisonAverage.Value - baseAverage.Value) / baseAverage.Value * 100.0;
                var sign = deltaPercent >= 0 ? "+" : string.Empty;
                ComparisonDeltaLine =
                    $"{baseAverage:F0} → {comparisonAverage:F0} FPS  {sign}{deltaPercent:F1}%";
            }
        }

        HasBaseSession = BaseSession is not null;
        HasComparison = ComparisonSession is not null;
    }

    [RelayCommand]
    private void EditBaseMetadata() => RequestMetadataEditor(BaseSession);

    [RelayCommand]
    private void EditComparisonMetadata() => RequestMetadataEditor(ComparisonSession);

    private void RequestMetadataEditor(SessionAnalysis? session)
    {
        if (session is null)
        {
            return;
        }

        var identity = CaptureIdentityResolver.TryBuild(session.Capture.Path);
        var current = identity is null ? null : _metadataStore.Get(identity);
        MetadataEditorRequested?.Invoke(
            this,
            new MetadataEditorRequest(session, current ?? new ManualMetadata()));
    }

    /// <summary>
    /// Persists manual metadata for a session and refreshes the cards.
    /// Empty metadata removes the stored entry, like the Python reference.
    /// </summary>
    public void PersistMetadata(SessionAnalysis session, ManualMetadata metadata)
    {
        var identity = CaptureIdentityResolver.TryBuild(session.Capture.Path);
        if (identity is null)
        {
            _dialogs.ShowError("Benchmark metadata", "The capture file could not be inspected.");
            return;
        }

        try
        {
            _metadataStore.Set(identity, metadata);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            AppLog.ErrorOperation("Manual metadata persistence", error);
            _dialogs.ShowError("Benchmark metadata", $"Metadata could not be saved: {error.Message}");
            return;
        }

        RefreshSessionCards();
        StatusText = $"METADATA SAVED  ·  {session.Capture.DisplayName}";
    }

    private ManualMetadata? ManualMetadataOf(SessionAnalysis? session)
    {
        if (session is null)
        {
            return null;
        }

        var identity = CaptureIdentityResolver.TryBuild(session.Capture.Path);
        return identity is null ? null : _metadataStore.Get(identity);
    }

    /// <summary>Stored manual metadata for a session, used by exports.</summary>
    public ManualMetadata? ManualMetadataFor(SessionAnalysis session) => ManualMetadataOf(session);

    private static string CardNameOf(SessionAnalysis session, ManualMetadata? manual)
    {
        if (manual is not null && manual.BenchmarkName.Length > 0)
        {
            return manual.BenchmarkName;
        }

        if (manual is not null && manual.Game.Length > 0)
        {
            return manual.Game;
        }

        return GameNameOf(session);
    }

    public sealed record MetadataEditorRequest(SessionAnalysis Session, ManualMetadata Current);

    [RelayCommand]
    private void AnalyzeFullCapture()
    {
        if (!Chart.HasData)
        {
            return;
        }

        AnalyzeRangeRequested?.Invoke(this, null);
    }

    [RelayCommand]
    private void AnalyzeWorstRegion()
    {
        if (!Chart.HasData)
        {
            return;
        }

        if (DirectionOf() is not { } higherIsBetter)
        {
            _dialogs.ShowInfo(
                "Analyze",
                "This metric has no defined performance direction, so a "
                + "worst region cannot be determined.");
            return;
        }

        var result = _rangeAnalysis.WorstPerformanceRegion(Chart.CurrentPoints().Base, higherIsBetter);
        ApplyAnalyzeRange(result, "Not enough valid data to find a 10-second worst-performance region.");
    }

    [RelayCommand]
    private void AnalyzeStableRegion()
    {
        if (!Chart.HasData)
        {
            return;
        }

        var result = _rangeAnalysis.MostStableRegion(Chart.CurrentPoints().Base);
        ApplyAnalyzeRange(result, "Not enough valid data to find a stable 10-second region.");
    }

    [RelayCommand]
    private void AnalyzeLargestDrop()
    {
        if (!Chart.HasData)
        {
            return;
        }

        if (DirectionOf() is not { } higherIsBetter)
        {
            _dialogs.ShowInfo(
                "Analyze",
                "This metric has no defined performance direction, so a "
                + "performance drop cannot be determined.");
            return;
        }

        var result = _rangeAnalysis.LargestDropRegion(Chart.CurrentPoints().Base, higherIsBetter);
        ApplyAnalyzeRange(result, "No meaningful performance drop was found in this capture.");
    }

    [RelayCommand]
    private void AnalyzeLargestAbDifference()
    {
        if (!Chart.HasData)
        {
            return;
        }

        var points = Chart.CurrentPoints();
        if (points.Comparison.Count == 0)
        {
            _dialogs.ShowInfo(
                "Analyze",
                "Load a comparison session to analyze A/B differences.");
            return;
        }

        var result = _rangeAnalysis.LargestAbDifferenceRegion(points.Base, points.Comparison);
        ApplyAnalyzeRange(
            result,
            "The sessions do not overlap enough to measure a meaningful A/B difference.");
    }

    private void ApplyAnalyzeRange(TimeRange? result, string emptyMessage)
    {
        if (result is null)
        {
            _dialogs.ShowInfo("Analyze", emptyMessage);
            return;
        }

        AnalyzeRangeRequested?.Invoke(this, result);
    }

    private bool? DirectionOf() => Chart.SelectedMetric?.Direction switch
    {
        MetricDirection.HigherIsBetter => true,
        MetricDirection.LowerIsBetter => false,
        _ => null,
    };

    private static string GameNameOf(SessionAnalysis session)
    {
        var application = DisplayText.CleanGameName(session.Metadata?.Application ?? string.Empty);
        return string.IsNullOrWhiteSpace(application) || application == "--"
            ? session.Capture.DisplayName
            : application;
    }

    private static string MetaLineOf(SessionAnalysis session)
    {
        var metadata = session.Metadata;
        if (metadata is null)
        {
            return "No data";
        }

        var parts = new List<string>();
        foreach (var value in new[] { metadata.Resolution, metadata.Gpu, metadata.Cpu })
        {
            if (!string.IsNullOrEmpty(value) && value != "--")
            {
                parts.Add(value);
            }
        }

        return parts.Count > 0 ? string.Join("  ·  ", parts) : "No data";
    }

    private static string ResolutionOf(SessionAnalysis session) =>
        session.Metadata?.Resolution ?? "--";

    private static string Normalize(string? mode) =>
        string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
}
