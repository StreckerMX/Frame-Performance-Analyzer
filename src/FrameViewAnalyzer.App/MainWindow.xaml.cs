using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
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

        // Presentation glue: forward chart data, interaction toggles, and
        // view-range changes; refresh the chart style on theme switches.
        viewModel.Chart.PropertyChanged += OnChartPropertyChanged;
        viewModel.AnalyzeRangeRequested += OnAnalyzeRangeRequested;
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

    private void MetricSelector_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Windows convention: positive delta scrolls up (previous metric).
        var direction = e.Delta > 0 ? -1 : 1;
        if (_viewModel.Chart.StepSelectedMetric(direction))
        {
            e.Handled = true;
        }
    }
}