using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// Chart-side state: the base/comparison sessions, the metric catalog, the
/// selected metric, its full-resolution series for both sessions, interaction
/// toggles, and the visible-range KPI tiles. Rendering lives in the chart
/// layer; this view model only carries data.
/// </summary>
public partial class ChartViewModel : ObservableObject
{
    private MetricSeries? _fpsSeries;
    private MetricSeries? _fpsComparisonSeries;

    [ObservableProperty]
    private SessionAnalysis? _session;

    [ObservableProperty]
    private SessionAnalysis? _comparisonSession;

    [ObservableProperty]
    private MetricDefinition? _selectedMetric;

    [ObservableProperty]
    private MetricSeries? _series;

    [ObservableProperty]
    private MetricSeries? _comparisonSeries;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private bool _wheelZoomEnabled = true;

    [ObservableProperty]
    private bool _panEnabled = true;

    [ObservableProperty]
    private bool _markersVisible;

    public ObservableCollection<MetricDefinition> Metrics { get; } = [];

    public ObservableCollection<KpiTileViewModel> KpiTiles { get; } =
    [
        new("AVERAGE FPS"),
        new("1% LOW"),
        new("0.1% LOW"),
        new("MAXIMUM"),
        new("MINIMUM"),
        new("VISIBLE TIME"),
    ];

    public int SampleCount => Session?.Samples.Count ?? 0;

    public int SeriesPointCount => Series?.X.Length ?? 0;

    /// <summary>All series to plot for the selected metric (base first).</summary>
    public IReadOnlyList<MetricSeries> SeriesList
    {
        get
        {
            var list = new List<MetricSeries>();
            if (Series is not null)
            {
                list.Add(Series);
            }

            if (ComparisonSeries is not null)
            {
                list.Add(ComparisonSeries);
            }

            return list;
        }
    }

    public void SetSessions(SessionAnalysis? baseSession, SessionAnalysis? comparisonSession)
    {
        if (baseSession is null)
        {
            Clear();
            return;
        }

        Session = baseSession;
        ComparisonSession = comparisonSession;

        var keepSelection = Metrics.FirstOrDefault(metric => metric.Id == SelectedMetric?.Id);
        Metrics.Clear();
        foreach (var metric in ComparisonService.MetricUnion(baseSession, comparisonSession))
        {
            Metrics.Add(metric);
        }

        SelectedMetric = keepSelection ?? (Metrics.Count > 0 ? Metrics[0] : null);
        RefreshSeries();
        UpdateVisibleRange(null);
    }

    public void Clear()
    {
        Session = null;
        ComparisonSession = null;
        Metrics.Clear();
        SelectedMetric = null;
        _fpsSeries = null;
        _fpsComparisonSeries = null;
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

    /// <summary>Recomputes the KPI tiles for the visible range (null = full).</summary>
    public void UpdateVisibleRange(ScottPlot.AxisLimits? bounds)
    {
        if (_fpsSeries is null || _fpsSeries.X.Length == 0)
        {
            foreach (var tile in KpiTiles)
            {
                tile.Apply("--");
            }

            return;
        }

        var metric = CoreMetricCatalog.CoreById["fps"];
        var (minX, maxX) = bounds is { } range
            ? (range.Left, range.Right)
            : (_fpsSeries.X[0], _fpsSeries.X[^1]);
        var (baseStats, baseCount) = VisibleRangeCalculator.Compute(metric, _fpsSeries.X, _fpsSeries.Y, minX, maxX);

        MetricStatistics? comparisonStats = null;
        var comparisonCount = 0;
        if (_fpsComparisonSeries is not null && _fpsComparisonSeries.X.Length > 0)
        {
            (comparisonStats, comparisonCount) = VisibleRangeCalculator.Compute(
                metric, _fpsComparisonSeries.X, _fpsComparisonSeries.Y, minX, maxX);
        }

        var comparisonMode = comparisonStats is not null;

        ApplyTile(KpiTiles[0], baseStats?.Avg, comparisonStats?.Avg, comparisonMode, formatFps: true);
        ApplyTile(KpiTiles[1], baseStats?.P1, comparisonStats?.P1, comparisonMode, formatFps: true);
        ApplyTile(KpiTiles[2], baseStats?.P01, comparisonStats?.P01, comparisonMode, formatFps: true);
        ApplyTile(KpiTiles[3], baseStats?.Max, comparisonStats?.Max, comparisonMode, formatFps: false);
        ApplyTile(KpiTiles[4], baseStats?.Min, comparisonStats?.Min, comparisonMode, formatFps: false);

        if (comparisonMode && comparisonCount != baseCount)
        {
            KpiTiles[5].Apply(
                DisplayText.FormatDurationHuman(baseCount),
                $"vs {DisplayText.FormatDurationHuman(comparisonCount)}",
                ImprovementKind.None);
        }
        else
        {
            KpiTiles[5].Apply(DisplayText.FormatDurationHuman(baseCount));
        }
    }

    private static void ApplyTile(
        KpiTileViewModel tile,
        double? baseValue,
        double? comparisonValue,
        bool comparisonMode,
        bool formatFps)
    {
        if (!comparisonMode || comparisonValue is null)
        {
            tile.Apply(FormatValue(baseValue, formatFps));
            return;
        }

        var deltaKind = CoreMetricCatalog.ClassifyImprovement(
            MetricDirection.HigherIsBetter, baseValue, comparisonValue);
        var (delta, deltaPercent) = ComparisonService.ComputeDelta(baseValue, comparisonValue);
        tile.Apply(
            $"{FormatValue(baseValue, formatFps)} → {FormatValue(comparisonValue, formatFps)}",
            ComparisonText.FormatDelta(delta, deltaPercent, deltaKind),
            deltaKind);
    }

    private static string FormatValue(double? value, bool formatFps)
    {
        if (value is null)
        {
            return "--";
        }

        return formatFps ? $"{value:F1}" : $"{value:F1} FPS";
    }

    partial void OnSelectedMetricChanged(MetricDefinition? value) => RefreshSeries();

    private void RefreshSeries()
    {
        if (Session is null || SelectedMetric is null)
        {
            Series = null;
            ComparisonSeries = null;
            HasData = false;
            return;
        }

        // Build each session's series independently: a metric that only
        // exists in one capture must not hide the other session's data.
        // The session role travels with the series so styling (SeriesA for
        // Base, SeriesB for Comparison) never depends on list position.
        var baseSeries = SeriesBuilder.Build(Session, SelectedMetric.Id);
        Series = baseSeries.Y.Length > 0
            ? baseSeries with { Role = SessionRole.Base }
            : null;

        if (ComparisonSession is not null)
        {
            var comparisonSeries = SeriesBuilder.Build(ComparisonSession, SelectedMetric.Id);
            ComparisonSeries = comparisonSeries.Y.Length > 0
                ? comparisonSeries with
                {
                    Label = "Comparison",
                    Role = SessionRole.Comparison,
                }
                : null;
        }
        else
        {
            ComparisonSeries = null;
        }

        _fpsSeries = SeriesBuilder.Build(Session, "fps");
        _fpsComparisonSeries = ComparisonSession is null
            ? null
            : SeriesBuilder.Build(ComparisonSession, "fps");

        HasData = Series is not null || ComparisonSeries is not null;
    }

    /// <summary>
    /// Base/comparison points for the selected metric, in the ChartPoint
    /// shape consumed by the Analyze range calculations.
    /// </summary>
    public (IReadOnlyList<ChartPoint> Base, IReadOnlyList<ChartPoint> Comparison) CurrentPoints() =>
        (ToPoints(Series), ToPoints(ComparisonSeries));

    public static IReadOnlyList<ChartPoint> ToPoints(MetricSeries? series)
    {
        if (series is null || series.X.Length == 0)
        {
            return [];
        }

        var points = new ChartPoint[series.X.Length];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = new ChartPoint(series.X[index], series.Y[index]);
        }

        return points;
    }
}
