using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// Chart-side state: the loaded session, the metric catalog, the selected
/// metric, its full-resolution series, interaction toggles, and the
/// visible-range statistics shown in the KPI strip. Rendering lives in the
/// chart layer; this view model only carries data.
/// </summary>
public partial class ChartViewModel : ObservableObject
{
    private MetricSeries? _fpsSeries;

    [ObservableProperty]
    private SessionAnalysis? _session;

    [ObservableProperty]
    private MetricDefinition? _selectedMetric;

    [ObservableProperty]
    private MetricSeries? _series;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private bool _wheelZoomEnabled = true;

    [ObservableProperty]
    private bool _panEnabled = true;

    [ObservableProperty]
    private bool _markersVisible;

    [ObservableProperty]
    private string _avgFpsText = "--";

    [ObservableProperty]
    private string _p1FpsText = "--";

    [ObservableProperty]
    private string _p01FpsText = "--";

    [ObservableProperty]
    private string _maxFpsText = "--";

    [ObservableProperty]
    private string _minFpsText = "--";

    [ObservableProperty]
    private string _visibleTimeText = "--";

    public ObservableCollection<MetricDefinition> Metrics { get; } = [];

    public int SampleCount => Session?.Samples.Count ?? 0;

    public int SeriesPointCount => Series?.X.Length ?? 0;

    /// <summary>Analyzes a capture and selects the first metric (FPS).</summary>
    public void Load(SessionAnalysis session)
    {
        Session = session;
        Metrics.Clear();
        foreach (var metric in session.Catalog)
        {
            Metrics.Add(metric);
        }

        _fpsSeries = SeriesBuilder.Build(session, "fps");
        SelectedMetric = Metrics.Count > 0 ? Metrics[0] : null;
        RefreshSeries();
        UpdateVisibleRange(null);
    }

    public void Clear()
    {
        Session = null;
        Metrics.Clear();
        SelectedMetric = null;
        _fpsSeries = null;
        RefreshSeries();
        UpdateVisibleRange(null);
    }

    /// <summary>
    /// Steps the selected metric ±1 without wrapping. Returns true only when
    /// the selection actually changed (the wheel handler marks the event
    /// handled only then).
    /// </summary>
    public bool StepSelectedMetric(int direction)
    {
        if (Metrics.Count == 0 || SelectedMetric is null)
        {
            return false;
        }

        var index = Metrics.IndexOf(SelectedMetric);
        var target = Math.Clamp(index + direction, 0, Metrics.Count - 1);
        if (target == index)
        {
            return false;
        }

        SelectedMetric = Metrics[target];
        return true;
    }

    /// <summary>Recomputes the KPI strip for the visible range (null = full).</summary>
    public void UpdateVisibleRange(ScottPlot.AxisLimits? bounds)
    {
        if (_fpsSeries is null || _fpsSeries.X.Length == 0)
        {
            AvgFpsText = "--";
            P1FpsText = "--";
            P01FpsText = "--";
            MaxFpsText = "--";
            MinFpsText = "--";
            VisibleTimeText = "--";
            return;
        }

        var metric = CoreMetricCatalog.CoreById["fps"];
        var (stats, pointCount) = bounds is { } range
            ? VisibleRangeCalculator.Compute(metric, _fpsSeries.X, _fpsSeries.Y, range.Left, range.Right)
            : VisibleRangeCalculator.Compute(
                metric, _fpsSeries.X, _fpsSeries.Y, _fpsSeries.X[0], _fpsSeries.X[^1]);

        AvgFpsText = stats.Avg is null ? "--" : $"{stats.Avg:F1}";
        P1FpsText = stats.P1 is null ? "--" : $"{stats.P1:F1}";
        P01FpsText = stats.P01 is null ? "--" : $"{stats.P01:F1}";
        MaxFpsText = stats.Max is null ? "--" : $"{stats.Max:F0} FPS";
        MinFpsText = stats.Min is null ? "--" : $"{stats.Min:F1} FPS";
        VisibleTimeText = DisplayText.FormatDurationHuman(pointCount);
    }

    partial void OnSelectedMetricChanged(MetricDefinition? value) => RefreshSeries();

    private void RefreshSeries()
    {
        if (Session is null || SelectedMetric is null)
        {
            Series = null;
            HasData = false;
            return;
        }

        var series = SeriesBuilder.Build(Session, SelectedMetric.Id);
        var hasData = series.Y.Length > 0;
        Series = hasData ? series : null;
        HasData = hasData;
    }
}
