using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Math;
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

    public string VersionText => "FrameView Analyzer v2";

    public MainWindowViewModel(
        ISettingsStore settings,
        IThemeService themes,
        ChartViewModel chart,
        IFrameViewCsvReader reader,
        ICaptureAnalysisService analysis,
        IDialogService dialogs)
    {
        _settings = settings;
        _themes = themes;
        _reader = reader;
        _analysis = analysis;
        _dialogs = dialogs;
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

        try
        {
            var session = await LoadSessionAsync(path);
            BaseSession = session;
            RefreshSessionCards();
            Chart.SetSessions(BaseSession, ComparisonSession);
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

        try
        {
            var session = await LoadSessionAsync(path);
            ComparisonSession = session;
            RefreshSessionCards();
            Chart.SetSessions(BaseSession, ComparisonSession);
            StatusText = $"COMPARISON OPENED  ·  {session.Capture.DisplayName}  ·  {ResolutionOf(session)}";
        }
        catch (Exception error)
        {
            _dialogs.ShowError("CSV loading error", error.Message);
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
        BaseSessionName = BaseSession is null
            ? "No capture loaded"
            : GameNameOf(BaseSession);
        BaseMetaLine = BaseSession is null
            ? "Load a FrameView *_Log.csv capture"
            : MetaLineOf(BaseSession);
        BaseLoadButtonText = BaseSession is null ? "Load capture..." : "Change...";

        ComparisonSessionName = ComparisonSession is null
            ? "No comparison loaded"
            : GameNameOf(ComparisonSession);
        ComparisonMetaLine = ComparisonSession is null
            ? "Load a second capture to compare performance."
            : MetaLineOf(ComparisonSession);
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
    }

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
