using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Math;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.ViewModels;

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

    public ChartViewModel Chart { get; }

    /// <summary>
    /// Raised when an Analyze action wants the chart to jump somewhere; a
    /// null range means "Full capture" (reset to the complete series).
    /// </summary>
    public event EventHandler<TimeRange?>? AnalyzeRangeRequested;

    /// <summary>Raised when the manual metadata editor should open for a session.</summary>
    public event EventHandler<MetadataEditorRequest>? MetadataEditorRequested;

    public string VersionText => "FrameView Analyzer v2";

    public MainWindowViewModel(
        ISettingsStore settings,
        IThemeService themes,
        ChartViewModel chart,
        IFrameViewCsvReader reader,
        ICaptureAnalysisService analysis,
        IRangeAnalysisService rangeAnalysis,
        IManualMetadataStore metadataStore,
        ILibraryStore libraryStore,
        IDialogService dialogs)
    {
        _settings = settings;
        _themes = themes;
        _reader = reader;
        _analysis = analysis;
        _dialogs = dialogs;
        _rangeAnalysis = rangeAnalysis;
        _metadataStore = metadataStore;
        _libraryStore = libraryStore;
        Chart = chart;

        var mode = Normalize(settings.Load().AppearanceMode);
        _appearanceMode = mode;
        _isDark = mode == "dark";
        _isLight = mode == "light";
    }

    [RelayCommand]
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
    public async Task LoadBaseFromPathAsync(string path)
    {
        try
        {
            var session = await LoadSessionAsync(path);
            BaseSession = session;
            RefreshSessionCards();
            Chart.SetSessions(BaseSession, ComparisonSession);
            IndexSession(session);
            StatusText = $"CAPTURE OPENED  ·  {session.Capture.DisplayName}  ·  {ResolutionOf(session)}";
        }
        catch (Exception error)
        {
            _dialogs.ShowError("CSV loading error", error.Message);
        }
    }

    [RelayCommand]
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
    public async Task LoadComparisonFromPathAsync(string path)
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
            ComparisonSession = session;
            RefreshSessionCards();
            Chart.SetSessions(BaseSession, ComparisonSession);
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
        StatusText = promoted
            ? "BASE SESSION REMOVED  ·  The comparison is now the base session"
            : "READY  ·  No captures loaded";
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

    private async Task<SessionAnalysis> LoadSessionAsync(string path)
    {
        var capture = await _reader.LoadCaptureAsync(path);
        return _analysis.Analyze(capture);
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
