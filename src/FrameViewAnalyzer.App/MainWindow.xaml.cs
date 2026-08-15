using System.ComponentModel;
using System.Windows;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;

namespace FrameViewAnalyzer.App;

public partial class MainWindow : Window
{
    private readonly IWindowPlacementService _placement;
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(
        MainWindowViewModel viewModel,
        IWindowPlacementService placement,
        IThemeService themes)
    {
        InitializeComponent();
        _placement = placement;
        _viewModel = viewModel;
        DataContext = viewModel;

        // Restore once the native window exists; save on every close.
        SourceInitialized += (_, _) => _placement.Restore(this);
        Closing += (_, _) => _placement.Save(this);

        // Presentation glue: forward chart data changes and theme switches.
        viewModel.Chart.PropertyChanged += OnChartPropertyChanged;
        themes.Changed += (_, _) => ChartView.RefreshStyle();
        OnChartPropertyChanged(this, new PropertyChangedEventArgs(nameof(ChartViewModel.Series)));
    }

    private void OnChartPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChartViewModel.Series)
            && e.PropertyName != nameof(ChartViewModel.HasData))
        {
            return;
        }

        if (_viewModel.Chart.HasData
            && _viewModel.Chart.SelectedMetric is not null
            && _viewModel.Chart.Series is not null)
        {
            ChartView.ShowData(_viewModel.Chart.SelectedMetric, _viewModel.Chart.Series);
        }
        else
        {
            ChartView.Clear();
        }
    }
}