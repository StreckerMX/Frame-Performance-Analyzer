using System.IO;
using System.Windows;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Exports;
using FrameViewAnalyzer.Infrastructure.Legacy;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Benchmark Library browser: search, filters, sorting, A/B selection, the
/// recent-comparisons bar, legacy import, and package export/import.
/// Loading requests are forwarded to the owner.
/// </summary>
public partial class BenchmarkLibraryWindow : Window
{
    private readonly BenchmarkLibraryViewModel _viewModel;
    private readonly ILegacyDataImporter _legacyImporter;
    private readonly IExportService _exportService;
    private readonly IDialogService _dialogs;
    private readonly ILibraryStore _libraryStore;
    private readonly IManualMetadataStore _manualStore;

    public BenchmarkLibraryWindow(
        ILibraryStore libraryStore,
        IManualMetadataStore manualStore,
        CaptureFolderScanner scanner,
        ILegacyDataImporter legacyImporter,
        IExportService exportService,
        IDialogService dialogs,
        string? captureDirectory = null)
    {
        InitializeComponent();
        // Small screens / high DPI: cap to the working area; the row list is
        // inside a ScrollViewer and the footer stays visible.
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        _legacyImporter = legacyImporter;
        _exportService = exportService;
        _dialogs = dialogs;
        _libraryStore = libraryStore;
        _manualStore = manualStore;
        _viewModel = new BenchmarkLibraryViewModel(libraryStore, manualStore, scanner, captureDirectory);
        DataContext = _viewModel;

        _viewModel.LoadBaseRequested += path => LoadBaseRequested?.Invoke(path);
        _viewModel.LoadComparisonRequested += path => LoadComparisonRequested?.Invoke(path);
        _viewModel.CompareRequested += (first, second) => CompareRequested?.Invoke(first, second);

        Loaded += async (_, _) => await _viewModel.RefreshAsync();
    }

    public event Action<string>? LoadBaseRequested;

    public event Action<string>? LoadComparisonRequested;

    public event Action<string, string>? CompareRequested;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void ImportLegacy_Click(object sender, RoutedEventArgs e)
    {
        var result = _legacyImporter.Import();
        await _viewModel.RefreshAsync();
        MessageBox.Show(
            this,
            result.Summary(),
            "Legacy import",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ExportPackage_Click(object sender, RoutedEventArgs e)
    {
        var path = _dialogs.PickSaveFile(
            "FrameView_benchmarks.json",
            "JSON (*.json)|*.json",
            ".json");
        if (path is null)
        {
            return;
        }

        try
        {
            var package = ExportReport.BuildBenchmarkPackage(
                _libraryStore.Load(),
                _manualStore.Load());
            _exportService.WriteBenchmarkPackage(path, package);
            _dialogs.ShowInfo(
                "Export",
                $"Package saved with {package.Captures.Count} captures to:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Export", error.Message);
        }
    }

    private async void ImportPackage_Click(object sender, RoutedEventArgs e)
    {
        var path = _dialogs.PickOpenFile("JSON (*.json)|*.json");
        if (path is null)
        {
            return;
        }

        try
        {
            var library = _libraryStore.Load();
            var result = _exportService.ImportBenchmarkPackage(
                library,
                File.ReadAllText(path));
            _libraryStore.Save(library);
            if (result.ManualMetadataByIdentity.Count > 0)
            {
                var merged = new Dictionary<string, ManualMetadata>(
                    _manualStore.Load(),
                    StringComparer.Ordinal);
                foreach (var (identity, metadata) in result.ManualMetadataByIdentity)
                {
                    merged[identity] = metadata;
                }

                _manualStore.Save(merged);
            }

            await _viewModel.RefreshAsync();
            _dialogs.ShowInfo(
                "Benchmark package",
                $"Imported {result.Imported} capture(s), {result.Skipped} skipped.");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Benchmark package", error.Message);
        }
    }
}
