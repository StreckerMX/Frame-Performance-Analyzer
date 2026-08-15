using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.Analytics.Series;
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
        ICaptureAnalysisService analysis)
    {
        InitializeComponent();
        _placement = placement;
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
        DataContext = viewModel;

        // Restore once the native window exists; save on every close.
        SourceInitialized += (_, _) => _placement.Restore(this);
        Closing += (_, _) => _placement.Save(this);

        // Presentation glue: forward chart data, interaction toggles, and
        // view-range changes; refresh the chart style on theme switches.
        viewModel.Chart.PropertyChanged += OnChartPropertyChanged;
        viewModel.AnalyzeRangeRequested += OnAnalyzeRangeRequested;
        viewModel.MetadataEditorRequested += OnMetadataEditorRequested;
        ChartView.ViewChanged += bounds => viewModel.Chart.UpdateVisibleRange(bounds);
        themes.Changed += (_, _) => ChartView.RefreshStyle();
        OnChartPropertyChanged(this, new PropertyChangedEventArgs(nameof(ChartViewModel.Series)));
        SyncInteractions();
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
            }
            else
            {
                ChartView.Clear();
            }
        }
        else if (e.PropertyName == nameof(ChartViewModel.MarkersVisible)
                 || e.PropertyName == nameof(ChartViewModel.WheelZoomEnabled)
                 || e.PropertyName == nameof(ChartViewModel.PanEnabled))
        {
            SyncInteractions();
        }
    }

    private void SyncInteractions() => ChartView.ApplyInteractions(
        _viewModel.Chart.WheelZoomEnabled,
        _viewModel.Chart.PanEnabled,
        _viewModel.Chart.MarkersVisible);

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

    private void Library_Click(object sender, RoutedEventArgs e)
    {
        var captureDirectory = _settings.Load().CaptureDirectory;
        var library = new BenchmarkLibraryWindow(
            _libraryStore,
            _manualMetadataStore,
            _scanner,
            _legacyImporter,
            _exportService,
            _dialogs,
            _reader,
            _analysis,
            captureDirectory)
        {
            Owner = this,
        };
        library.LoadBaseRequested += async path => await _viewModel.LoadBaseFromPathAsync(path);
        library.LoadComparisonRequested += async path => await _viewModel.LoadComparisonFromPathAsync(path);
        library.CompareRequested += async (first, second) =>
        {
            await _viewModel.LoadBaseFromPathAsync(first);
            await _viewModel.LoadComparisonFromPathAsync(second);
        };
        library.ShowDialog();
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        var baseSession = _viewModel.BaseSession;
        if (baseSession is null)
        {
            _dialogs.ShowInfo("Export", "Load at least one base session.");
            return;
        }

        var options = new List<(SessionAnalysis Session, string Label)>
        {
            (baseSession, ExportReport.SessionExportLabel(baseSession)),
        };
        if (_viewModel.ComparisonSession is { } comparison)
        {
            options.Add((comparison, ExportReport.SessionExportLabel(comparison)));
        }

        var dialog = new ExportReportWindow(options) { Owner = this };
        dialog.ExportRequested += (scope, session) => PerformPngExport(scope, session);
        dialog.ShowDialog();
    }

    private void PerformPngExport(ExportScope scope, SessionAnalysis? session)
    {
        var baseSession = _viewModel.BaseSession;
        if (baseSession is null)
        {
            return;
        }

        if (scope == ExportScope.Single && session is null)
        {
            return;
        }

        var byId = baseSession.Catalog.ToDictionary(metric => metric.Id, StringComparer.Ordinal);
        var metricIds = ExportReport.SelectReportMetricIds(
            baseSession.Catalog,
            _viewModel.Chart.SelectedMetric?.Id ?? "fps");
        var groups = new List<ReportPlotBuilder.ReportGroup>();
        foreach (var metricId in metricIds)
        {
            if (!byId.TryGetValue(metricId, out var metric))
            {
                continue;
            }

            var seriesList = new List<MetricSeries>();
            if (scope == ExportScope.Single)
            {
                var singleSeries = SeriesBuilder.Build(session!, metricId);
                if (singleSeries.Y.Length > 0)
                {
                    seriesList.Add(singleSeries with { Role = SessionRole.Base });
                }
            }
            else
            {
                var baseSeries = SeriesBuilder.Build(baseSession, metricId);
                if (baseSeries.Y.Length > 0)
                {
                    seriesList.Add(baseSeries with { Role = SessionRole.Base });
                }

                if (_viewModel.ComparisonSession is { } comparisonSession)
                {
                    var comparisonSeries = SeriesBuilder.Build(comparisonSession, metricId);
                    if (comparisonSeries.Y.Length > 0)
                    {
                        seriesList.Add(comparisonSeries with
                        {
                            Label = "Comparison",
                            Role = SessionRole.Comparison,
                        });
                    }
                }
            }

            if (seriesList.Count > 0)
            {
                groups.Add(new ReportPlotBuilder.ReportGroup(metric, seriesList));
            }
        }

        if (groups.Count == 0)
        {
            _dialogs.ShowInfo("Export", "No metrics are available to export.");
            return;
        }

        var stemSession = scope == ExportScope.Single ? session! : baseSession;
        var initialFile = ExportReport.BuildFileStem(stemSession, metricIds) + ".png";
        var path = _dialogs.PickSaveFile(initialFile, "PNG (*.png)|*.png", ".png");
        if (path is null)
        {
            return;
        }

        try
        {
            var multiplot = ReportPlotBuilder.Build(
                groups,
                ChartStyle.FromApplicationResources(),
                BuildReportHeader(scope, session));
            ReportPlotBuilder.SavePng(multiplot, path, 1600, groups.Count * 520);
            _dialogs.ShowInfo("Export", $"Report saved with {groups.Count} charts to:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Export", error.Message);
        }
    }

    private ReportPlotBuilder.ReportHeader BuildReportHeader(
        ExportScope scope,
        SessionAnalysis? singleSession)
    {
        var baseSession = scope == ExportScope.Single ? singleSession! : _viewModel.BaseSession!;
        var manual = _viewModel.ManualMetadataFor(baseSession);
        var game = manual is { BenchmarkName.Length: > 0 }
            ? manual.BenchmarkName
            : manual is { Game.Length: > 0 }
                ? manual.Game
                : ExportReport.SessionExportLabel(baseSession);
        var lines = new List<string>();
        if (baseSession.Metadata is { } metadata)
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

        if (scope == ExportScope.All && _viewModel.ComparisonSession is { } comparison)
        {
            lines.Add($"Base: {ExportReport.SessionExportLabel(baseSession)}");
            lines.Add($"Comparison: {ExportReport.SessionExportLabel(comparison)}");
        }

        return new ReportPlotBuilder.ReportHeader(game, lines);
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
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
            var rows = ExportReport.BuildStatisticsRows(baseSession, _viewModel.ComparisonSession);
            var count = _exportService.WriteStatisticsCsv(path, rows);
            _dialogs.ShowInfo("Export", $"Statistics saved with {count} rows to:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Export", error.Message);
        }
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
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
            var document = ExportReport.BuildStatisticsPayload(
                baseSession,
                _viewModel.ComparisonSession,
                _viewModel.ManualMetadataFor(baseSession),
                _viewModel.ComparisonSession is { } comparison
                    ? _viewModel.ManualMetadataFor(comparison)
                    : null);
            _exportService.WriteStatisticsJson(path, document);
            _dialogs.ShowInfo("Export", $"Benchmark data saved to:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Export", error.Message);
        }
    }
}