using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.App.Busy;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Exports;
using FrameViewAnalyzer.Infrastructure.Legacy;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Benchmark Library browser: search, filters, sorting, Multi selection,
/// recent Pair comparisons, non-destructive record removal, legacy import,
/// and package export/import. Loading requests are forwarded to the owner.
/// The Library owns its own busy state: opening the window is instant, and
/// any work the Library performs (loading, imports, exports) is presented
/// on the Library's own status bar, not the owner's.
/// </summary>
public partial class BenchmarkLibraryWindow : Window
{
    private readonly BenchmarkLibraryViewModel _viewModel;
    private readonly ILegacyDataImporter _legacyImporter;
    private readonly IExportService _exportService;
    private readonly IDialogService _dialogs;
    private readonly ILibraryStore _libraryStore;
    private readonly IManualMetadataStore _manualStore;
    private readonly IFrameViewCsvReader _reader;
    private readonly ICaptureAnalysisService _analysis;
    private readonly ISettingsStore _settings;
    private readonly BusyState _busy;
    private readonly BenchmarkBrowserMode _mode;
    private string? _captureDirectory;

    public BenchmarkLibraryWindow(
        ILibraryStore libraryStore,
        IManualMetadataStore manualStore,
        CaptureFolderScanner scanner,
        ISettingsStore settings,
        ILegacyDataImporter legacyImporter,
        IExportService exportService,
        IDialogService dialogs,
        IFrameViewCsvReader reader,
        ICaptureAnalysisService analysis,
        string? captureDirectory = null,
        BenchmarkBrowserMode mode = BenchmarkBrowserMode.Library,
        IReadOnlyList<string>? initiallySelectedPaths = null,
        string? excludedSelectionPath = null)
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
        _settings = settings;
        _reader = reader;
        _analysis = analysis;
        _mode = mode;
        _captureDirectory = captureDirectory;
        _busy = new BusyState();
        _viewModel = new BenchmarkLibraryViewModel(
            libraryStore,
            manualStore,
            scanner,
            captureDirectory,
            _busy,
            mode,
            initiallySelectedPaths,
            excludedSelectionPath);
        DataContext = _viewModel;
        Title = _viewModel.WindowTitle;
        WindowBusy.Attach(this, _busy);

        _viewModel.LoadBaseRequested += path => LoadBaseRequested?.Invoke(path);
        _viewModel.LoadComparisonRequested += path => LoadComparisonRequested?.Invoke(path);
        _viewModel.CompareRequested += (first, second) => CompareRequested?.Invoke(first, second);
        _viewModel.SelectionConfirmedRequested += ForwardContextSelection;
        _viewModel.CompareSelectedRequested += async paths =>
        {
            if (CompareSelectedRequested is { } requested)
            {
                requested(paths);
            }
            else if (Owner is FrameViewAnalyzer.App.MainWindow mainWindow)
            {
                await mainWindow.LoadMultiBenchmarksFromLibraryAsync(paths);
            }

            Close();
        };
        _viewModel.RemoveRequested += ConfirmRemoveFromLibrary;

        Loaded += async (_, _) => await _viewModel.RefreshAsync();
    }

    public event Action<string>? LoadBaseRequested;

    public event Action<string>? LoadComparisonRequested;

    /// <summary>Recent two-capture comparison; remains a Pair workflow.</summary>
    public event Action<string, string>? CompareRequested;

    /// <summary>Selected Library captures to load as equal peers in Multi.</summary>
    public event Action<IReadOnlyList<string>>? CompareSelectedRequested;

    /// <summary>Selection made from the contextual Pair/Multi browser.</summary>
    public event Action<BenchmarkBrowserMode, IReadOnlyList<string>>? SelectionConfirmedRequested;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void ChooseCaptureFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.IsBusy)
        {
            return;
        }

        var folder = _dialogs.PickFolder(_captureDirectory);
        if (folder is null)
        {
            return;
        }

        try
        {
            using var scope = _busy.BeginVisible("Changing capture folder");
            _settings.Save(_settings.Load() with { CaptureDirectory = folder });
            _captureDirectory = folder;
            await _viewModel.ChangeCaptureFolderAsync(folder);

            if (Owner is FrameViewAnalyzer.App.MainWindow mainWindow)
            {
                await mainWindow.RefreshCaptureFolderFromSettingsAsync();
            }
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            AppLog.ErrorOperation("Capture folder change", error);
            _dialogs.ShowError("Capture folder", error.Message);
        }
    }

    private void BenchmarkRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_busy.IsBusy || IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        ToggleBenchmarkRow(sender);
        e.Handled = true;
    }

    private void BenchmarkRow_KeyDown(object sender, KeyEventArgs e)
    {
        if (_busy.IsBusy
            || e.Key is not (Key.Enter or Key.Space)
            || IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        ToggleBenchmarkRow(sender);
        e.Handled = true;
    }

    private void ToggleBenchmarkRow(object sender)
    {
        if (sender is FrameworkElement { DataContext: LibraryRow row })
        {
            _viewModel.ToggleSelectedCommand.Execute(row);
        }
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase)
            {
                return true;
            }

            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void ForwardContextSelection(
        BenchmarkBrowserMode mode,
        IReadOnlyList<string> paths)
    {
        // Close the modal browser before MainWindow begins the potentially
        // expensive load, so its immediate busy overlay is visible at once.
        Close();
        SelectionConfirmedRequested?.Invoke(mode, paths);
    }

    private void ConfirmRemoveFromLibrary(LibraryRow row)
    {
        var result = MessageBox.Show(
            this,
            $"Remove '{row.Title}' from Benchmark Library?\n\n"
            + "The source CSV will not be deleted. The record will stay hidden when the capture folder is refreshed.",
            "Remove from Library",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            _viewModel.RemoveFromLibrary(row);
        }
    }

    private async void ImportLegacy_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.IsBusy)
        {
            return;
        }

        try
        {
            // Legacy stores live in files outside this app; the whole import
            // is blocking file I/O, so it runs off the UI thread.
            var result = await _busy.RunOnThreadPoolAsync(
                "Reading legacy data",
                () => _legacyImporter.Import());
            await _viewModel.RefreshAsync();
            MessageBox.Show(
                this,
                result.Summary(),
                "Legacy import",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            AppLog.ErrorOperation("Legacy data import", error);
            _dialogs.ShowError("Legacy import", error.Message);
        }
    }

    private async void ExportPackage_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.IsBusy)
        {
            return;
        }

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
            var result = await _busy.RunAsync("Creating benchmark package", async () =>
            {
                var library = _libraryStore.Load();
                var prepared = await _exportService.PreparePackageAsync(
                    library,
                    _manualStore.Load(),
                    _reader,
                    _analysis);
                // Hydrated digests and the package write are blocking I/O;
                // keep them off the UI thread.
                await Task.Run(() =>
                {
                    if (prepared.Analyzed > 0)
                    {
                        _libraryStore.Save(library);
                    }

                    _exportService.WriteBenchmarkPackage(path, prepared.Package);
                });
                return prepared;
            });
            _dialogs.ShowInfo(
                "Export",
                $"Package saved to:\n{path}\n\n"
                + $"Exported: {result.Exported} capture(s)\n"
                + $"Analyzed to obtain statistics: {result.Analyzed}\n"
                + $"Excluded (no analyzable statistics): {result.Excluded}");
        }
        catch (Exception error)
        {
            AppLog.ErrorOperation("Benchmark package export", error);
            _dialogs.ShowError("Export", error.Message);
        }
    }

    private async void ImportPackage_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.IsBusy)
        {
            return;
        }

        var path = _dialogs.PickOpenFile("JSON (*.json)|*.json");
        if (path is null)
        {
            return;
        }

        try
        {
            // Reading, validating, and committing the package are blocking
            // file I/O; run them off the UI thread.
            var proposal = await _busy.RunOnThreadPoolAsync("Importing benchmark package", () =>
            {
                var parsed = _exportService.ImportBenchmarkPackage(
                    _libraryStore.Load(),
                    _manualStore.Load(),
                    File.ReadAllText(path));

                // Coordinated commit: both stores are serialized first, both
                // destinations are version-checked, and the two files are
                // written with rollback — a failure leaves both stores in
                // their original state and throws instead of claiming success.
                _exportService.CommitBenchmarkImport(parsed, _libraryStore, _manualStore);
                return parsed;
            });

            // Only after both stores are persisted is the live in-memory
            // state published (the metadata cache re-reads the files).
            _manualStore.Reload();

            await _viewModel.RefreshAsync();
            _dialogs.ShowInfo(
                "Benchmark package",
                $"Imported {proposal.Imported} capture(s), {proposal.Skipped} skipped.");
        }
        catch (Exception error)
        {
            AppLog.ErrorOperation("Benchmark package import", error);
            _dialogs.ShowError("Benchmark package", error.Message);
        }
    }
}
