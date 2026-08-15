using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// One visible-range KPI tile. In comparison mode the value shows
/// "base → comparison" and DeltaText carries the direction-aware delta.
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

    public KpiTileViewModel(string label) => _label = label;

    public void Apply(string value, string deltaText = "", ImprovementKind kind = ImprovementKind.None)
    {
        Value = value;
        DeltaText = deltaText;
        Kind = kind;
    }
}
