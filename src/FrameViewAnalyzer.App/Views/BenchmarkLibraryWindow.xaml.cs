using System.Windows;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Benchmark Library browser: search, filters, sorting, A/B selection, and
/// the recent-comparisons bar. Loading requests are forwarded to the owner.
/// </summary>
public partial class BenchmarkLibraryWindow : Window
{
    private readonly BenchmarkLibraryViewModel _viewModel;

    public BenchmarkLibraryWindow(
        ILibraryStore libraryStore,
        IManualMetadataStore manualStore,
        CaptureFolderScanner scanner,
        string? captureDirectory = null)
    {
        InitializeComponent();
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
}
