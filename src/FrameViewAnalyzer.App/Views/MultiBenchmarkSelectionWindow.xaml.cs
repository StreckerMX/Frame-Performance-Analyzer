using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.App.ViewModels;

namespace FrameViewAnalyzer.App.Views;

public partial class MultiBenchmarkSelectionWindow : Window
{
    private readonly MultiBenchmarkSelectionViewModel _viewModel;

    public MultiBenchmarkSelectionWindow(
        IReadOnlyList<CaptureOption> captures,
        IReadOnlyList<string>? selectedPaths = null)
    {
        InitializeComponent();
        _viewModel = new MultiBenchmarkSelectionViewModel(
            captures,
            selectedPaths ?? []);
        DataContext = _viewModel;
    }

    public IReadOnlyList<string> SelectedPaths =>
        _viewModel.Choices.Where(choice => choice.IsSelected).Select(choice => choice.Path).ToList();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var choice in _viewModel.Choices.Take(MultiBenchmarkSelectionViewModel.MaximumSelection))
        {
            choice.IsSelected = true;
        }

        _viewModel.RefreshSummary();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var choice in _viewModel.Choices)
        {
            choice.IsSelected = false;
        }

        _viewModel.RefreshSummary();
    }

    private void LoadSelected_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Validate())
        {
            return;
        }

        DialogResult = true;
    }
}

internal partial class MultiBenchmarkSelectionViewModel : ObservableObject
{
    public const int MaximumSelection = 8;

    [ObservableProperty]
    private string _selectionSummary = "0 benchmarks selected";

    [ObservableProperty]
    private string _validationText = string.Empty;

    public ObservableCollection<MultiBenchmarkChoiceViewModel> Choices { get; } = [];

    public MultiBenchmarkSelectionViewModel(
        IReadOnlyList<CaptureOption> captures,
        IReadOnlyList<string> selectedPaths)
    {
        var selected = new HashSet<string>(selectedPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var capture in captures)
        {
            var choice = new MultiBenchmarkChoiceViewModel(
                capture,
                selected.Contains(capture.Path));
            choice.PropertyChanged += Choice_PropertyChanged;
            Choices.Add(choice);
        }

        RefreshSummary();
    }

    public bool Validate()
    {
        var selected = Choices.Count(choice => choice.IsSelected);
        if (selected < 2)
        {
            ValidationText = "Select at least two benchmarks.";
            return false;
        }

        if (selected > MaximumSelection)
        {
            ValidationText = $"Select no more than {MaximumSelection} benchmarks.";
            return false;
        }

        ValidationText = string.Empty;
        return true;
    }

    public void RefreshSummary()
    {
        var count = Choices.Count(choice => choice.IsSelected);
        SelectionSummary = $"{count} benchmark(s) selected  ·  All compared equally";
        ValidationText = string.Empty;
    }

    private void Choice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MultiBenchmarkChoiceViewModel.IsSelected))
        {
            RefreshSummary();
        }
    }
}

internal partial class MultiBenchmarkChoiceViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public MultiBenchmarkChoiceViewModel(CaptureOption capture, bool isSelected)
    {
        Path = capture.Path;
        Display = capture.Display;
        FileName = System.IO.Path.GetFileName(capture.Path);
        _isSelected = isSelected;
    }

    public string Path { get; }

    public string Display { get; }

    public string FileName { get; }
}
