using System.Windows;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Legacy;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Benchmark Library browser: search, filters, sorting, A/B selection, and
/// the recent-comparisons bar. Loading requests are forwarded to the owner.
/// </summary>
public partial class BenchmarkLibraryWindow : Window
{
    private readonly BenchmarkLibraryViewModel _viewModel;
    private readonly ILegacyDataImporter _legacyImporter;

    public BenchmarkLibraryWindow(
        ILibraryStore libraryStore,
        IManualMetadataStore manualStore,
        CaptureFolderScanner scanner,
        ILegacyDataImporter legacyImporter,
        string? captureDirectory = null)
    {
        InitializeComponent();
        // Small screens / high DPI: cap to the working area; the row list is
        // inside a ScrollViewer and the footer stays visible.
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        _legacyImporter = legacyImporter;
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
}
