using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// One per-benchmark value displayed inside a Multi KPI tile. ComparedColorHex
/// carries the runner-up color for the best row, so the UI can identify the
/// closest competitor without introducing a Base or Reference benchmark.
/// </summary>
public sealed record KpiSeriesValueViewModel(
    string Label,
    string Value,
    string ColorHex,
    string DeltaText = "",
    bool IsBest = false,
    string? ComparedColorHex = null)
{
    public bool HasComparedColor => !string.IsNullOrWhiteSpace(ComparedColorHex);
}

/// <summary>
/// One visible-range KPI tile. Pair mode keeps the compact Base → Comparison
/// presentation. Multi mode fills SeriesValues so every benchmark is visible
/// with the same color used by its chart line.
/// </summary>
public partial class KpiTileViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _value = "--";

    [ObservableProperty]
    private string _deltaText = string.Empty;

    [ObservableProperty]
    private ImprovementKind _kind = ImprovementKind.None;

    public ObservableCollection<KpiSeriesValueViewModel> SeriesValues { get; } = [];

    public bool HasSeriesValues => SeriesValues.Count > 0;

    public KpiTileViewModel(string label) => _label = label;

    public void Apply(string value, string deltaText = "", ImprovementKind kind = ImprovementKind.None)
    {
        SeriesValues.Clear();
        OnPropertyChanged(nameof(HasSeriesValues));
        Value = value;
        DeltaText = deltaText;
        Kind = kind;
    }

    public void ApplySeries(IEnumerable<KpiSeriesValueViewModel> values)
    {
        SeriesValues.Clear();
        foreach (var value in values)
        {
            SeriesValues.Add(value);
        }

        Value = string.Empty;
        DeltaText = string.Empty;
        Kind = ImprovementKind.None;
        OnPropertyChanged(nameof(HasSeriesValues));
    }
}
