using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// Chart-side state: the loaded session, the metric catalog, the selected
/// metric, and its full-resolution series. Rendering (decimation, plotting)
/// lives in the chart layer; this view model only carries data.
/// </summary>
public partial class ChartViewModel : ObservableObject
{
    private readonly ICaptureAnalysisService _analysis;

    [ObservableProperty]
    private SessionAnalysis? _session;

    [ObservableProperty]
    private MetricDefinition? _selectedMetric;

    [ObservableProperty]
    private MetricSeries? _series;

    [ObservableProperty]
    private bool _hasData;

    public ObservableCollection<MetricDefinition> Metrics { get; } = [];

    public ChartViewModel(ICaptureAnalysisService analysis) => _analysis = analysis;

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

        SelectedMetric = Metrics.Count > 0 ? Metrics[0] : null;
        RefreshSeries();
    }

    public void Clear()
    {
        Session = null;
        Metrics.Clear();
        SelectedMetric = null;
        RefreshSeries();
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
