using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Comparison;
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
        DataContext = viewModel;

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
            var capture = await _reader.LoadCaptureAsync(path);
            var window = new SummaryTableWindow(
                new SummaryTableViewModel(SummaryTable.Build(capture)))
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
        if (_viewModel.BaseSession is { } session)
        {
            OpenDetails(session);
        }
    }

    private void ComparisonDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ComparisonSession is { } session)
        {
            OpenDetails(session);
        }
    }

    private void OpenDetails(SessionAnalysis session)
    {
        var window = new SessionDetailsWindow(new SessionDetailsViewModel(session))
        {
            Owner = this,
        };
        WindowThemeBootstrap.Attach(window, _themes);
        window.ShowDialog();
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
        WindowThemeBootstrap.Attach(library, _themes);
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

        var options = new List<ExportSessionOption>
        {
            new(SessionRole.Base, SessionPickerLabel(baseSession), baseSession),
        };
        if (_viewModel.ComparisonSession is { } comparison)
        {
            options.Add(new ExportSessionOption(
                SessionRole.Comparison,
                SessionPickerLabel(comparison),
                comparison));
        }

        var metrics = ComparisonService.MetricUnion(baseSession, _viewModel.ComparisonSession);
        var dialog = new ExportReportWindow(options, metrics) { Owner = this };
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

    private void PerformPngExport(ExportReportSelection selection)
    {
        if (selection.Sessions.Count == 0 || selection.MetricIds.Count == 0)
        {
            return;
        }

        var byId = selection.Sessions
            .SelectMany(option => option.Session.Catalog)
            .GroupBy(metric => metric.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
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
                var series = SeriesBuilder.Build(option.Session, metricId);
                if (series.Y.Length == 0)
                {
                    continue;
                }

                seriesList.Add(series with
                {
                    Label = option.Label,
                    Role = option.Role,
                });
            }

            if (seriesList.Count > 0)
            {
                groups.Add(new ReportPlotBuilder.ReportGroup(metric, seriesList));
            }
        }

        if (groups.Count == 0)
        {
            _dialogs.ShowInfo("Export", "No selected metrics are available to export.");
            return;
        }

        var stemSession = selection.Sessions[0].Session;
        var initialFile = ExportReport.BuildFileStem(stemSession, selection.MetricIds) + ".png";
        var path = _dialogs.PickSaveFile(initialFile, "PNG (*.png)|*.png", ".png");
        if (path is null)
        {
            return;
        }

        try
        {
            var style = ChartStyle.FromApplicationResources();
            var multiplot = ReportPlotBuilder.Build(groups, style);
            var header = BuildReportHeader(selection);
            var height = groups.Count * 520 + ReportPlotBuilder.MeasureHeaderHeight(header);
            ReportPlotBuilder.SavePng(multiplot, style, header, path, 1600, height);
            _dialogs.ShowInfo("Export", $"Report saved with {groups.Count} charts to:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Export", error.Message);
        }
    }

    private ReportPlotBuilder.ReportHeader BuildReportHeader(ExportReportSelection selection)
    {
        var first = selection.Sessions[0];
        var headerSession = first.Session;
        var manual = _viewModel.ManualMetadataFor(headerSession);
        var game = manual is { BenchmarkName.Length: > 0 }
            ? manual.BenchmarkName
            : manual is { Game.Length: > 0 }
                ? manual.Game
                : ExportReport.SessionExportLabel(headerSession);
        var lines = new List<string>();
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

        foreach (var option in selection.Sessions)
        {
            lines.Add(ExportReport.SessionRoleLine(option.Role, option.DisplayName));
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
