using System.Windows;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.Views;

namespace FrameViewAnalyzer.App;

public partial class MainWindow
{
    private async void SelectMultiBenchmarks_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsMultiMode)
        {
            return;
        }

        if (_viewModel.Captures.Count == 0)
        {
            await _viewModel.RefreshCapturesCommand.ExecuteAsync(null);
        }

        if (_viewModel.Captures.Count == 0)
        {
            _dialogs.ShowInfo(
                "Multi benchmark",
                "No benchmark logs were found in the selected capture folder.");
            return;
        }

        var window = new MultiBenchmarkSelectionWindow(
            _viewModel.Captures.ToList(),
            _viewModel.MultiSelectedPaths)
        {
            Owner = this,
        };
        WindowThemeBootstrap.Attach(window, _themes);

        if (window.ShowDialog() != true)
        {
            return;
        }

        await _viewModel.LoadMultiBenchmarksAsync(window.SelectedPaths);
    }
}
