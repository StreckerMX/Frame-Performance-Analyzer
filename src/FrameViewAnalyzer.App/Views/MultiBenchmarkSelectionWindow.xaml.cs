using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.App.ViewModels;

namespace FrameViewAnalyzer.App.Views;

public partial class MultiBenchmarkSelectionWindow : Window
{
    private readonly MultiBenchmarkSelectionViewModel _viewModel;

    public MultiBenchmarkSelectionWindow(
        IReadOnlyList<CaptureOption> captures,
        IReadOnlyList<string>? selectedPaths = null,
        string? referencePath = null)
    {
        InitializeComponent();
        _viewModel = new MultiBenchmarkSelectionViewModel(
            captures,
            selectedPaths ?? [],
            referencePath);
        DataContext = _viewModel;
    }

    public IReadOnlyList<string> SelectedPaths =>
        _viewModel.Choices.Where(choice => choice.IsSelected).Select(choice => choice.Path).ToList();

    public string? ReferencePath =>
        _viewModel.Choices.FirstOrDefault(choice => choice.IsReference)?.Path;

    private void Reference_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { DataContext: MultiBenchmarkChoiceViewModel selected })
        {
            return;
        }

        selected.IsSelected = true;
        foreach (var choice in _viewModel.Choices)
        {
            if (!ReferenceEquals(choice, selected))
            {
                choice.IsReference = false;
            }
        }

        _viewModel.RefreshSummary();
    }

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
            choice.IsReference = false;
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
        IReadOnlyList<string> selectedPaths,
        string? referencePath)
    {
        var selected = new HashSet<string>(selectedPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var capture in captures)
        {
            var choice = new MultiBenchmarkChoiceViewModel(
                capture,
                selected.Contains(capture.Path),
                string.Equals(capture.Path, referencePath, StringComparison.OrdinalIgnoreCase));
            choice.PropertyChanged += Choice_PropertyChanged;
            Choices.Add(choice);
        }

        // If a restored selection has no reference, prefer its first checked
        // benchmark rather than forcing the user to rediscover the state.
        if (!Choices.Any(choice => choice.IsReference))
        {
            var first = Choices.FirstOrDefault(choice => choice.IsSelected);
            if (first is not null)
            {
                first.IsReference = true;
            }
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

        if (!Choices.Any(choice => choice.IsSelected && choice.IsReference))
        {
            ValidationText = "Choose one selected benchmark as the reference.";
            return false;
        }

        ValidationText = string.Empty;
        return true;
    }

    public void RefreshSummary()
    {
        var count = Choices.Count(choice => choice.IsSelected);
        var reference = Choices.FirstOrDefault(choice => choice.IsReference)?.Display;
        SelectionSummary = reference is null
            ? $"{count} benchmark(s) selected  ·  No reference"
            : $"{count} benchmark(s) selected  ·  Reference: {reference}";
        ValidationText = string.Empty;
    }

    private void Choice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MultiBenchmarkChoiceViewModel choice)
        {
            return;
        }

        if (e.PropertyName == nameof(MultiBenchmarkChoiceViewModel.IsSelected)
            && !choice.IsSelected
            && choice.IsReference)
        {
            choice.IsReference = false;
        }

        if (e.PropertyName is nameof(MultiBenchmarkChoiceViewModel.IsSelected)
            or nameof(MultiBenchmarkChoiceViewModel.IsReference))
        {
            RefreshSummary();
        }
    }
}

internal partial class MultiBenchmarkChoiceViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isReference;

    public MultiBenchmarkChoiceViewModel(CaptureOption capture, bool isSelected, bool isReference)
    {
        Path = capture.Path;
        Display = capture.Display;
        FileName = System.IO.Path.GetFileName(capture.Path);
        _isSelected = isSelected;
        _isReference = isReference && isSelected;
    }

    public string Path { get; }

    public string Display { get; }

    public string FileName { get; }

    partial void OnIsReferenceChanged(bool value)
    {
        if (value)
        {
            IsSelected = true;
        }
    }
}
