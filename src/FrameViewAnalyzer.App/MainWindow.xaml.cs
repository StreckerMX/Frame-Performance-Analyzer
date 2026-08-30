using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Busy;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Exports;
using FrameViewAnalyzer.Infrastructure.Legacy;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App;

public partial class MainWindow : Window
{
    private readonly IWindowPlacementService _placement;
    private readonly IThemeService _themes;
    private readonly MainWindowViewModel _viewModel;
    private readonly ILibraryStore _libraryStore;
    private readonly IManualMetadataStore _manualMetadataStore;
    private readonly CaptureFolderScanner _scanner;
    private readonly ISettingsStore _settings;
    private readonly ILegacyDataImporter _legacyImporter;
    private readonly IExportService _exportService;
    private readonly IDialogService _dialogs;
    private readonly IFrameViewCsvReader _reader;
    private readonly ICaptureAnalysisService _analysis;
    private readonly BusyState _busy;
    private int _framePointGeneration;

    public MainWindow(
        MainWindowViewModel viewModel,
        IWindowPlacementService placement,
        IThemeService themes,
        ILibraryStore libraryStore,
        IManualMetadataStore manualMetadataStore,
        CaptureFolderScanner scanner,
        ISettingsStore settings,
        ILegacyDataImporter legacyImporter,
        IExportService exportService,
        IDialogService dialogs,
        IFrameViewCsvReader reader,
        ICaptureAnalysisService analysis,
        BusyState busy)
    {
        InitializeComponent();
        _placement = placement;
        _themes = themes;
        _viewModel = viewModel;
        _libraryStore = libraryStore;
        _manualMetadataStore = manualMetadataStore;
        _scanner = scanner;
        _settings = settings;
        _legacyImporter = legacyImporter;
        _exportService = exportService;
        _dialogs = dialogs;
        _reader = reader;
        _analysis = analysis;
        _busy = busy;
        DataContext = viewModel;
        WindowBusy.Attach(this, _busy);

        // Restore once the native window exists; save on every close. The
        // native caption theme is painted as soon as the HWND is available.
        SourceInitialized += (_, _) =>
        {
            _placement.Restore(this);
            ApplyTitleBarTheme();
        };
        Closing += (_, _) => _placement.Save(this);

        // Presentation glue: forward chart data, interaction toggles, and
        // view-range changes; refresh the chart style on theme switches.
        viewModel.Chart.PropertyChanged += OnChartPropertyChanged;
        viewModel.AnalyzeRangeRequested += OnAnalyzeRangeRequested;
        viewModel.MetadataEditorRequested += OnMetadataEditorRequested;
        viewModel.SummaryRequested += async (_, path) =>
        {
            // MainWindow owns the summary load; once the table window opens,
            // this window returns to READY.
            var capture = await _busy.RunAsync("Reading capture data", () => _reader.LoadCaptureAsync(path));
            var document = await _busy.RunOnThreadPoolAsync(
                "Loading summary data",
                () => SummaryTable.Build(capture));
            var window = new SummaryTableWindow(new SummaryTableViewModel(document))
            {
                Owner = this,
            };
            WindowThemeBootstrap.Attach(window, _themes);
            window.Show();
        };
        viewModel.ExportPngReportRequested += (_, _) => ExportPng_Click(this, new RoutedEventArgs());
        viewModel.ExportStatisticsCsvRequested += (_, _) => ExportCsv_Click(this, new RoutedEventArgs());
        viewModel.ExportBenchmarkJsonRequested += (_, _) => ExportJson_Click(this, new RoutedEventArgs());
        ChartView.ViewChanged += bounds => viewModel.Chart.UpdateVisibleRange(bounds);
        themes.Changed += (_, _) =>
        {
            ChartView.RefreshStyle();
            ApplyTitleBarTheme();
        };
        OnChartPropertyChanged(this, new PropertyChangedEventArgs(nameof(ChartViewModel.Series)));
        SyncInteractions();
    }

    /// <summary>Paints the native caption to match the current app theme.</summary>
    private void ApplyTitleBarTheme()
    {
        var isDark = !string.Equals(_themes.Current, "light", StringComparison.OrdinalIgnoreCase);
        WindowTitleBarTheme.Apply(this, isDark);
    }

    private void OnChartPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChartViewModel.Series)
            || e.PropertyName == nameof(ChartViewModel.ComparisonSeries)
            || e.PropertyName == nameof(ChartViewModel.HasData))
        {
            if (_viewModel.Chart.HasData
                && _viewModel.Chart.SelectedMetric is not null
                && _viewModel.Chart.SeriesList.Count > 0)
            {
                ChartView.ShowData(_viewModel.Chart.SelectedMetric, _viewModel.Chart.SeriesList);
                SyncInteractions();
                if (_viewModel.Chart.MarkersVisible)
                {
                    _ = RefreshFramePointsAsync();
                }
            }
            else
            {
                Interlocked.Increment(ref _framePointGeneration);
                _viewModel.Chart.ClearFramePointSeries();
                ChartView.Clear();
            }
        }
        else if (e.PropertyName == nameof(ChartViewModel.MarkersVisible))
        {
            SyncInteractions();
            if (_viewModel.Chart.MarkersVisible)
            {
                _ = RefreshFramePointsAsync();
            }
            else
            {
                Interlocked.Increment(ref _framePointGeneration);
                _viewModel.Chart.ClearFramePointSeries();
                ChartView.ClearFramePoints();
            }
        }
        else if (e.PropertyName == nameof(ChartViewModel.WheelZoomEnabled)
                 || e.PropertyName == nameof(ChartViewModel.PanEnabled))
        {
            SyncInteractions();
        }
    }

    private void SyncInteractions() => ChartView.ApplyInteractions(
        _viewModel.Chart.WheelZoomEnabled,
        _viewModel.Chart.PanEnabled,
        _viewModel.Chart.MarkersVisible);

    /// <summary>
    /// Prepares frame-level detail only after the user enables Frame points.
    /// The builder is cached per immutable session + metric, so normal loading
    /// performs no extra work and repeated requests are effectively instant.
    /// A generation guard prevents stale work from replacing a newer metric or
    /// workspace selection.
    /// </summary>
    private async Task RefreshFramePointsAsync()
    {
        var generation = Interlocked.Increment(ref _framePointGeneration);
        var chart = _viewModel.Chart;
        var metric = chart.SelectedMetric;
        if (!chart.MarkersVisible || !chart.HasData || metric is null)
        {
            chart.ClearFramePointSeries();
            ChartView.ClearFramePoints();
            return;
        }

        var sourceSeries = chart.SeriesList
            .Where(series => series.SourceSession is not null)
            .ToList();
        if (sourceSeries.Count == 0)
        {
            chart.ClearFramePointSeries();
            ChartView.ClearFramePoints();
            return;
        }

        try
        {
            var metricId = metric.Id;
            var frameSeries = await _busy.RunOnThreadPoolAsync(
                "Preparing frame points",
                () =>
                {
                    var result = new List<MetricSeries>(sourceSeries.Count);
                    foreach (var source in sourceSeries)
                    {
                        var session = source.SourceSession!;
                        var built = FramePointSeriesBuilder.Build(session, metricId);
                        if (built.Y.Length == 0)
                        {
                            continue;
                        }

                        result.Add(built with
                        {
                            Label = source.Label,
                            Role = source.Role,
                            WorkspaceIndex = source.WorkspaceIndex,
                            IsReference = source.IsReference,
                        });
                    }

                    return (IReadOnlyList<MetricSeries>)result;
                });

            if (generation != Volatile.Read(ref _framePointGeneration)
                || !chart.MarkersVisible
                || chart.SelectedMetric?.Id != metricId)
            {
                return;
            }

            chart.SetFramePointSeries(frameSeries);
            ChartView.SetFramePoints(frameSeries);
        }
        catch (Exception error)
        {
            if (generation == Volatile.Read(ref _framePointGeneration))
            {
                chart.ClearFramePointSeries();
                ChartView.ClearFramePoints();
                AppLog.ErrorOperation("Frame point preparation", error);
                _viewModel.StatusText = "FRAME POINTS UNAVAILABLE";
            }
        }
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs e) => ChartView.ResetZoom();

    private void AutoZoom_Click(object sender, RoutedEventArgs e) => ChartView.AutoZoom();

    private void OnAnalyzeRangeRequested(object? sender, TimeRange? range)
    {
        if (range is null)
        {
            ChartView.ResetZoom();
        }
        else
        {
            ChartView.ZoomToRange(range.Value.Start, range.Value.End);
        }
    }

    private void OnMetadataEditorRequested(
        object? sender,
        MainWindowViewModel.MetadataEditorRequest request)
    {
        var editor = new MetadataEditorWindow(request.Session, request.Current) { Owner = this };
        WindowThemeBootstrap.Attach(editor, _themes);
        editor.Saved += metadata => _viewModel.PersistMetadata(request.Session, metadata);
        editor.ShowDialog();
    }

    private void MetricSelector_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Windows convention: positive delta scrolls up (previous metric).
        var direction = e.Delta > 0 ? -1 : 1;
        if (_viewModel.Chart.StepSelectedMetric(direction))
        {
            e.Handled = true;
        }
    }

    private void BaseDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.IsBusy)
        {
            return;
        }

        if (_viewModel.BaseSession is { } session)
        {
            _ = OpenDetailsAsync(session);
        }
    }

    private void ComparisonDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.IsBusy)
        {
            return;
        }

        if (_viewModel.ComparisonSession is { } session)
        {
            _ = OpenDetailsAsync(session);
        }
    }

    /// <summary>
    /// Prepares and shows the View Details window. MainWindow owns the
    /// preparation ("Loading capture details"); as soon as the child
    /// window opens, MainWindow returns to READY — the busy scope covers
    /// only the preparation, never the dialog itself.
    /// </summary>
    private async Task OpenDetailsAsync(SessionAnalysis session)
    {
        var window = await PrepareDetailsWindowAsync(session);
        window.Owner = this;
        WindowThemeBootstrap.Attach(window, _themes);
        window.ShowDialog();
    }

    /// <summary>
    /// Builds the read-only details window inside a busy scope. Extracted so
    /// tests can verify that the preparation completes with the main window
    /// READY again before the child window is shown.
    /// </summary>
    internal async Task<SessionDetailsWindow> PrepareDetailsWindowAsync(SessionAnalysis session)
    {
        var viewModel = await _busy.RunOnThreadPoolAsync(
            "Loading capture details",
            () => new SessionDetailsViewModel(session));
        return new SessionDetailsWindow(viewModel);
    }

    private void Library_Click(object sender, RoutedEventArgs e) =>
        OpenBenchmarkBrowser(BenchmarkBrowserMode.Library);

    private void LoadBaseBrowser_Click(object sender, RoutedEventArgs e) =>
        OpenBenchmarkBrowser(BenchmarkBrowserMode.PairBase);

    private void LoadComparisonBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.BaseSession is null)
        {
            _dialogs.ShowInfo(
                "Benchmark browser",
                "Load a Base benchmark before selecting a comparison.");
            return;
        }

        OpenBenchmarkBrowser(
            BenchmarkBrowserMode.PairComparison,
            excludedSelectionPath: _viewModel.BaseSession.Capture.Path);
    }

    /// <summary>
    /// Opens the same searchable/indexed browser for Library management,
    /// Pair slot selection, or equal-peer Multi selection.
    /// </summary>
    private void OpenBenchmarkBrowser(
        BenchmarkBrowserMode mode,
        IReadOnlyList<string>? initiallySelectedPaths = null,
        string? excludedSelectionPath = null)
    {
        // Opening the browser is instant; it owns its folder refresh and busy
        // presentation. MainWindow becomes busy only after a selection closes it.
        if (_busy.IsBusy)
        {
            return;
        }

        var captureDirectory = _settings.Load().CaptureDirectory
            ?? PlatformFolders.FrameViewDirectory();
        var library = new BenchmarkLibraryWindow(
            _libraryStore,
            _manualMetadataStore,
            _scanner,
            _settings,
            _legacyImporter,
            _exportService,
            _dialogs,
            _reader,
            _analysis,
            captureDirectory,
            mode,
            initiallySelectedPaths,
            excludedSelectionPath)
        {
            Owner = this,
        };
        WindowThemeBootstrap.Attach(library, _themes);
        library.LoadBaseRequested += async path => await _viewModel.LoadBaseFromPathAsync(path);
        library.LoadComparisonRequested += async path => await _viewModel.LoadComparisonFromPathAsync(path);
        library.CompareRequested += async (first, second) =>
        {
            await _viewModel.LoadBaseFromPathAsync(first);
            await _viewModel.LoadComparisonFromPathAsync(second);
        };
        library.SelectionConfirmedRequested += async (selectionMode, paths) =>
        {
            switch (selectionMode)
            {
                case BenchmarkBrowserMode.PairBase:
                    await _viewModel.LoadBaseFromPathAsync(paths[0]);
                    break;
                case BenchmarkBrowserMode.PairComparison:
                    await _viewModel.LoadComparisonFromPathAsync(paths[0]);
                    break;
                case BenchmarkBrowserMode.Multi:
                    await _viewModel.LoadMultiBenchmarksAsync(paths);
                    break;
            }
        };
        library.ShowDialog();
    }

    /// <summary>Keeps the main toolbar synchronized with folder changes made in the browser.</summary>
    internal Task RefreshCaptureFolderFromSettingsAsync() =>
        _viewModel.ReloadCaptureFolderAsync();

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.IsBusy)
        {
            return;
        }

        List<ExportSessionOption> options;
        IReadOnlyList<FrameViewAnalyzer.Core.Metrics.MetricDefinition> metrics;

        if (_viewModel.IsMultiMode)
        {
            if (_viewModel.MultiSessions.Count < 2)
            {
                _dialogs.ShowInfo("Export", "Select at least two Multi benchmarks first.");
                return;
            }

            options = _viewModel.MultiSessions
                .Select((item, index) => new ExportSessionOption(
                    SessionRole.Comparison,
                    item.Label,
                    item.Session,
                    WorkspaceIndex: index,
                    IsMultiPeer: true))
                .ToList();
            metrics = _viewModel.Chart.Metrics.ToList();
        }
        else
        {
            var baseSession = _viewModel.BaseSession;
            if (baseSession is null)
            {
                _dialogs.ShowInfo("Export", "Load at least one base session.");
                return;
            }

            options =
            [
                new ExportSessionOption(SessionRole.Base, SessionPickerLabel(baseSession), baseSession),
            ];
            if (_viewModel.ComparisonSession is { } comparison)
            {
                options.Add(new ExportSessionOption(
                    SessionRole.Comparison,
                    SessionPickerLabel(comparison),
                    comparison));
            }

            metrics = ComparisonService.MetricUnion(baseSession, _viewModel.ComparisonSession);
        }

        var previousMetricIds = _settings.Load().LastPngReportMetricIds;
        var dialog = new ExportReportWindow(options, metrics, previousMetricIds) { Owner = this };
        WindowThemeBootstrap.Attach(dialog, _themes);
        dialog.ExportRequested += PerformPngExport;
        dialog.ShowDialog();
    }

    /// <summary>
    /// Best human-readable name for the session picker: manual benchmark name
    /// first, then the metadata/display-name based export label; never an
    /// absolute path.
    /// </summary>
    private string SessionPickerLabel(SessionAnalysis session)
    {
        var manual = _viewModel.ManualMetadataFor(session);
        if (manual is { BenchmarkName.Length: > 0 })
        {
            return manual.BenchmarkName;
        }

        return ExportReport.SessionExportLabel(session);
    }

    private async void PerformPngExport(ExportReportSelection selection)
    {
        if (selection.Sessions.Count == 0 || selection.MetricIds.Count == 0)
        {
            return;
        }

        var isMultiReport = selection.Sessions.All(option => option.IsMultiPeer);
        var useFramePoints = _viewModel.Chart.MarkersVisible;
        var byId = selection.Sessions
            .SelectMany(option => option.Session.Catalog)
            .GroupBy(metric => metric.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        // Build the report from the same data representation the user is
        // inspecting. Frame-point mode sources true analyzed frames for every
        // selected metric; ReportPlotBuilder later performs visualization-only
        // extrema-preserving decimation to fit the PNG pixel budget.
        var groups = await _busy.RunOnThreadPoolAsync(
            "Preparing report",
            () => BuildReportGroups(selection, byId, isMultiReport, useFramePoints));

        if (groups.Count == 0)
        {
            _dialogs.ShowInfo("Export", "No selected metrics are available to export.");
            return;
        }

        var stemSession = selection.Sessions[0].Session;
        var initialFile = ExportReport.BuildPngFileName(
            stemSession,
            selection.MetricIds,
            isMultiReport,
            DateTime.Now);
        var path = _dialogs.PickSaveFile(initialFile, "PNG (*.png)|*.png", ".png");
        if (path is null)
        {
            return;
        }

        try
        {
            // ChartStyle reads WPF application resources; capture it on the
            // UI thread, then render and encode the PNG off the UI thread.
            var style = ChartStyle.FromApplicationResources();
            var header = BuildReportHeader(selection, useFramePoints);
            await _busy.RunOnThreadPoolAsync("Exporting report", () =>
            {
                var multiplot = ReportPlotBuilder.Build(groups, style);
                var height = groups.Count * 520 + ReportPlotBuilder.MeasureHeaderHeight(header);
                ReportPlotBuilder.SavePng(multiplot, style, header, path, 1600, height);
            });
            PersistPngReportMetricSelection(selection.MetricIds);
            _dialogs.ShowInfo(
                "Export",
                $"Report saved with {selection.Sessions.Count} benchmark(s) and {groups.Count} chart(s) to:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Export", error.Message);
        }
    }

    /// <summary>Remembers the metric checklist only after a PNG was written successfully.</summary>
    private void PersistPngReportMetricSelection(IEnumerable<string> metricIds)
    {
        try
        {
            var settings = _settings.Load();
            _settings.Save(settings with
            {
                LastPngReportMetricIds = metricIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .Take(ExportReport.MaxReportMetrics)
                    .ToArray(),
            });
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            // A preference write must never turn a successfully generated PNG
            // into an export failure. Keep the report and log the preference error.
            AppLog.ErrorOperation("PNG report metric preference persistence", error);
        }
    }

    /// <summary>One report plot group per selected metric, with the selected sessions' series.</summary>
    private static List<ReportPlotBuilder.ReportGroup> BuildReportGroups(
        ExportReportSelection selection,
        IReadOnlyDictionary<string, FrameViewAnalyzer.Core.Metrics.MetricDefinition> byId,
        bool isMultiReport,
        bool useFramePoints)
    {
        var groups = new List<ReportPlotBuilder.ReportGroup>();
        foreach (var metricId in selection.MetricIds)
        {
            if (!byId.TryGetValue(metricId, out var metric))
            {
                continue;
            }

            var seriesList = new List<MetricSeries>();
            foreach (var option in selection.Sessions)
            {
                var series = ReportSeriesBuilder.Build(option.Session, metricId, useFramePoints);
                if (series.Y.Length == 0)
                {
                    continue;
                }

                seriesList.Add(series with
                {
                    Label = option.Label,
                    Role = option.Role,
                    WorkspaceIndex = option.WorkspaceIndex,
                    IsReference = !option.IsMultiPeer && option.Role == SessionRole.Base,
                });
            }

            if (seriesList.Count > 0)
            {
                groups.Add(new ReportPlotBuilder.ReportGroup(
                    metric,
                    seriesList,
                    IsMultiWorkspace: isMultiReport));
            }
        }

        return groups;
    }

    private ReportPlotBuilder.ReportHeader BuildReportHeader(
        ExportReportSelection selection,
        bool useFramePoints)
    {
        var first = selection.Sessions[0];
        var headerSession = first.Session;
        var isMultiReport = selection.Sessions.All(option => option.IsMultiPeer);
        var manual = _viewModel.ManualMetadataFor(headerSession);
        var title = ExportReportTitles.NormalizeTitle(selection.ReportTitle, isMultiReport);
        var lines = new List<string>();

        // Pair can safely use the first session as report context. Multi may
        // contain different resolutions/configurations, so it avoids presenting
        // one capture's hardware/config as if it applied to every benchmark.
        if (!isMultiReport)
        {
            if (headerSession.Metadata is { } metadata)
            {
                var hardware = new List<string>();
                foreach (var value in new[] { metadata.Resolution, metadata.Gpu, metadata.Cpu })
                {
                    if (value.Length > 0 && value != "--")
                    {
                        hardware.Add(value);
                    }
                }

                if (hardware.Count > 0)
                {
                    lines.Add(string.Join("  ·  ", hardware));
                }
            }

            if (manual is not null && manual.ConfigLine is { } config)
            {
                lines.Add(config);
            }
        }

        foreach (var option in selection.Sessions)
        {
            lines.Add(option.HeaderLine);
        }

        var manualMetadataByPath = selection.Sessions
            .GroupBy(option => option.Session.Capture.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => _viewModel.ManualMetadataFor(group.First().Session),
                StringComparer.OrdinalIgnoreCase);

        return new ReportPlotBuilder.ReportHeader(
            title,
            lines,
            UseProfessionalLayout: true,
            IsMultiReport: isMultiReport,
            ManualMetadataByPath: manualMetadataByPath,
            UseFramePoints: useFramePoints);
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.IsBusy)
        {
            return;
        }

        if (_viewModel.BaseSession is not { } baseSession)
        {
            _dialogs.ShowInfo("Export statistics", "Load at least one base session.");
            return;
        }

        var path = _dialogs.PickSaveFile(
            $"frameview_{baseSession.Capture.DisplayName}_stats.csv",
            "CSV (*.csv)|*.csv",
            ".csv");
        if (path is null)
        {
            return;
        }

        try
        {
            var comparison = _viewModel.ComparisonSession;
            var count = await _busy.RunOnThreadPoolAsync("Exporting CSV", () =>
            {
                var rows = ExportReport.BuildStatisticsRows(baseSession, comparison);
                return _exportService.WriteStatisticsCsv(path, rows);
            });
            _dialogs.ShowInfo("Export", $"Statistics saved with {count} rows to:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Export", error.Message);
        }
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.IsBusy)
        {
            return;
        }

        if (_viewModel.BaseSession is not { } baseSession)
        {
            _dialogs.ShowInfo("Export statistics", "Load at least one base session.");
            return;
        }

        var path = _dialogs.PickSaveFile(
            $"frameview_{baseSession.Capture.DisplayName}_stats.json",
            "JSON (*.json)|*.json",
            ".json");
        if (path is null)
        {
            return;
        }

        try
        {
            var comparison = _viewModel.ComparisonSession;
            await _busy.RunOnThreadPoolAsync("Exporting benchmark data", () =>
            {
                var document = ExportReport.BuildStatisticsPayload(
                    baseSession,
                    comparison,
                    _viewModel.ManualMetadataFor(baseSession),
                    comparison is not null
                        ? _viewModel.ManualMetadataFor(comparison)
                        : null);
                _exportService.WriteStatisticsJson(path, document);
            });
            _dialogs.ShowInfo("Export", $"Benchmark data saved to:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Export", error.Message);
        }
    }
}
