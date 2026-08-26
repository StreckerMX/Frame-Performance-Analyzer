using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;
using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.App.Views;

public partial class MultiBenchmarkSelectionWindow : Window
{
    private readonly MultiBenchmarkSelectionViewModel _viewModel;
    private readonly IFrameViewCsvReader? _reader;
    private bool _detailsLoaded;

    public MultiBenchmarkSelectionWindow(
        IReadOnlyList<CaptureOption> captures,
        IReadOnlyList<string>? selectedPaths = null,
        IFrameViewCsvReader? reader = null,
        string? captureFolder = null)
    {
        InitializeComponent();
        _reader = reader;
        _viewModel = new MultiBenchmarkSelectionViewModel(
            captures,
            selectedPaths ?? [],
            captureFolder);
        DataContext = _viewModel;
        Loaded += MultiBenchmarkSelectionWindow_Loaded;
    }

    public IReadOnlyList<string> SelectedPaths =>
        _viewModel.Choices.Where(choice => choice.IsSelected).Select(choice => choice.Path).ToList();

    private async void MultiBenchmarkSelectionWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_detailsLoaded || _reader is null)
        {
            return;
        }

        _detailsLoaded = true;
        foreach (var choice in _viewModel.Choices)
        {
            try
            {
                var info = await _reader.ReadCaptureInfoAsync(choice.Path);
                if (info is not null)
                {
                    choice.ApplyInfo(info);
                }
                else
                {
                    choice.MarkDetailsUnavailable();
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                choice.MarkDetailsUnavailable();
            }
        }
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
        string? captureFolder = null)
    {
        CaptureFolder = ResolveCaptureFolder(captures, captureFolder);
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

    public string CaptureFolder { get; }

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

    private static string ResolveCaptureFolder(
        IReadOnlyList<CaptureOption> captures,
        string? captureFolder)
    {
        if (!string.IsNullOrWhiteSpace(captureFolder))
        {
            return captureFolder;
        }

        if (captures.Count > 0)
        {
            var directory = Path.GetDirectoryName(captures[0].Path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }

        return "Capture folder unavailable";
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

    [ObservableProperty]
    private string _technicalLine = "Reading capture details…";

    [ObservableProperty]
    private string _hardwareLine = " ";

    public MultiBenchmarkChoiceViewModel(CaptureOption capture, bool isSelected)
    {
        Path = capture.Path;
        Display = capture.Display;
        FileName = System.IO.Path.GetFileName(capture.Path);
        CaptureTimeText = BuildCaptureTimeText(capture.Path);
        _isSelected = isSelected;
    }

    public string Path { get; }

    public string Display { get; }

    public string FileName { get; }

    public string CaptureTimeText { get; }

    internal void ApplyInfo(CaptureInfo info)
    {
        var details = new List<string>();
        if (HasValue(info.Resolution))
        {
            details.Add(info.Resolution);
        }

        if (info.DurationSeconds is { } duration)
        {
            details.Add(FormatDuration(duration));
        }

        if (HasValue(info.Gpu))
        {
            details.Add(info.Gpu);
        }

        TechnicalLine = details.Count > 0
            ? string.Join("  ·  ", details)
            : "Capture details unavailable";
        HardwareLine = HasValue(info.Cpu)
            ? info.Cpu
            : "CPU information unavailable";
    }

    internal void MarkDetailsUnavailable()
    {
        TechnicalLine = "Capture details unavailable";
        HardwareLine = " ";
    }

    private static string BuildCaptureTimeText(string path)
    {
        if (CaptureFileNaming.TryParseCaptureStamp(path, out var stamp))
        {
            return $"Captured {CaptureFileNaming.FormatStamp(stamp)}";
        }

        try
        {
            var lastWrite = File.GetLastWriteTime(path);
            if (lastWrite > DateTime.MinValue)
            {
                return $"Modified {lastWrite.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The filename remains useful even if Windows cannot read file metadata.
        }

        return "Capture time unavailable";
    }

    private static string FormatDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            return "Duration unavailable";
        }

        var rounded = TimeSpan.FromSeconds(Math.Round(seconds));
        if (rounded.TotalHours >= 1)
        {
            return $"{(int)rounded.TotalHours}h {rounded.Minutes}m {rounded.Seconds}s";
        }

        if (rounded.TotalMinutes >= 1)
        {
            return $"{(int)rounded.TotalMinutes}m {rounded.Seconds}s";
        }

        return $"{rounded.Seconds}s";
    }

    private static bool HasValue(string value) =>
        !string.IsNullOrWhiteSpace(value) && value != "--";
}
