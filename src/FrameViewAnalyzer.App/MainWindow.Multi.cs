using System.Windows;
using FrameViewAnalyzer.App.ViewModels;

namespace FrameViewAnalyzer.App;

public partial class MainWindow
{
    private void SelectMultiBenchmarks_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsMultiMode)
        {
            return;
        }

        OpenBenchmarkBrowser(
            BenchmarkBrowserMode.Multi,
            _viewModel.MultiSelectedPaths);
    }
}
