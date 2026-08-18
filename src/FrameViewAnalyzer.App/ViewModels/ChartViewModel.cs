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
/// toggles, and the metric-aware visible-range KPI tiles. Rendering lives in
/// the chart layer; this view model only carries data.
/// </summary>
public partial class ChartViewModel : ObservableObject
{
    private ScottPlot.AxisLimits? _visibleBounds;

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

    public ObservableCollection<KpiTileViewModel> KpiTiles { get; } = [];

    public ChartViewModel() => ConfigureKpiTiles(CoreMetricCatalog.CoreById["fps"]);

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
        _visibleBounds = null;

        var keepSelection = Metrics.FirstOrDefault(metric => metric.Id == SelectedMetric?.Id);
        Metrics.Clear();
        foreach (var metric in ComparisonService.MetricUnion(baseSession, comparisonSession))
        {
            Metrics.Add(metric);
        }

        SelectedMetric = keepSelection ?? (Metrics.Count > 0 ? Metrics[0] : null);
        RefreshSeries();
        ConfigureKpiTiles(SelectedMetric);
        UpdateVisibleRange(null);
    }

    public void Clear()
    {
        Session = null;
        ComparisonSession = null;
        Metrics.Clear();
        SelectedMetric = null;
        _visibleBounds = null;
        RefreshSeries();
        ConfigureKpiTiles(CoreMetricCatalog.CoreById["fps"]);
        ResetKpiValues();
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

    /// <summary>
    /// Recomputes statistics for the selected metric over the visible range.
    /// The same X bounds are retained when the user switches metrics, so KPI
    /// values continue to describe exactly the currently inspected time span.
    /// </summary>
    public void UpdateVisibleRange(ScottPlot.AxisLimits? bounds)
    {
        _visibleBounds = bounds;
        var metric = SelectedMetric;
        if (metric is null || (Series is null && ComparisonSeries is null))
        {
            ResetKpiValues();
            return;
        }

        var populated = new[] { Series, ComparisonSeries }
            .Where(series => series is { X.Length: > 0 })
            .Cast<MetricSeries>()
            .ToList();
        if (populated.Count == 0)
        {
            ResetKpiValues();
            return;
        }

        var (minX, maxX) = bounds is { } range
            ? (range.Left, range.Right)
            : (populated.Min(series => series.X[0]), populated.Max(series => series.X[^1]));

        MetricStatistics? baseStats = null;
        var baseCount = 0;
        if (Series is { X.Length: > 0 } baseSeries)
        {
            (baseStats, baseCount) = VisibleRangeCalculator.Compute(
                metric, baseSeries.X, baseSeries.Y, minX, maxX);
        }

        MetricStatistics? comparisonStats = null;
        var comparisonCount = 0;
        if (ComparisonSeries is { X.Length: > 0 } comparisonSeries)
        {
            (comparisonStats, comparisonCount) = VisibleRangeCalculator.Compute(
                metric, comparisonSeries.X, comparisonSeries.Y, minX, maxX);
        }

        var fields = CoreMetricCatalog.StatFields(metric.Id);
        for (var index = 0; index < fields.Count; index++)
        {
            var (key, _) = fields[index];
            ApplyMetricTile(
                KpiTiles[index],
                metric,
                key,
                ValueFor(baseStats, key),
                ValueFor(comparisonStats, key));
        }

        ApplyVisibleTimeTile(KpiTiles[^1], baseStats, baseCount, comparisonStats, comparisonCount);
    }

    private static void ApplyMetricTile(
        KpiTileViewModel tile,
        MetricDefinition metric,
        string statisticKey,
        double? baseValue,
        double? comparisonValue)
    {
        if (baseValue is null && comparisonValue is null)
        {
            tile.Apply("--");
            return;
        }

        if (baseValue is null)
        {
            tile.Apply(FormatValue(metric, statisticKey, comparisonValue));
            return;
        }

        if (comparisonValue is null)
        {
            tile.Apply(FormatValue(metric, statisticKey, baseValue));
            return;
        }

        var deltaKind = CoreMetricCatalog.ClassifyImprovement(
            metric.Direction, baseValue, comparisonValue);
        var (delta, deltaPercent) = ComparisonService.ComputeDelta(baseValue, comparisonValue);
        tile.Apply(
            $"{FormatValue(metric, statisticKey, baseValue)} → {FormatValue(metric, statisticKey, comparisonValue)}",
            ComparisonText.FormatDelta(delta, deltaPercent, deltaKind),
            deltaKind);
    }

    private static void ApplyVisibleTimeTile(
        KpiTileViewModel tile,
        MetricStatistics? baseStats,
        int baseCount,
        MetricStatistics? comparisonStats,
        int comparisonCount)
    {
        var hasBase = baseStats is not null;
        var hasComparison = comparisonStats is not null;
        if (!hasBase && !hasComparison)
        {
            tile.Apply("--");
            return;
        }

        if (!hasBase)
        {
            tile.Apply(DisplayText.FormatDurationHuman(comparisonCount));
            return;
        }

        if (!hasComparison || comparisonCount == baseCount)
        {
            tile.Apply(DisplayText.FormatDurationHuman(baseCount));
            return;
        }

        tile.Apply(
            DisplayText.FormatDurationHuman(baseCount),
            $"vs {DisplayText.FormatDurationHuman(comparisonCount)}",
            ImprovementKind.None);
    }

    private static string FormatValue(
        MetricDefinition metric,
        string statisticKey,
        double? value)
    {
        if (value is null)
        {
            return "--";
        }

        // Preserve the compact FPS presentation already used by the app.
        if (metric.Id == "fps")
        {
            return statisticKey is "max" or "min"
                ? $"{value:F1} FPS"
                : $"{value:F1}";
        }

        return string.IsNullOrWhiteSpace(metric.Unit)
            ? $"{value:F1}"
            : $"{value:F1} {metric.Unit}";
    }

    private void ConfigureKpiTiles(MetricDefinition? metric)
    {
        var selected = metric ?? CoreMetricCatalog.CoreById["fps"];
        var fields = CoreMetricCatalog.StatFields(selected.Id);
        var labels = fields
            .Select(field => KpiLabel(selected, field.Key, field.Label))
            .Append("VISIBLE TIME")
            .ToList();

        if (KpiTiles.Count == labels.Count
            && KpiTiles.Select(tile => tile.Label).SequenceEqual(labels, StringComparer.Ordinal))
        {
            return;
        }

        KpiTiles.Clear();
        foreach (var label in labels)
        {
            KpiTiles.Add(new KpiTileViewModel(label));
        }
    }

    private static string KpiLabel(MetricDefinition metric, string key, string catalogLabel) =>
        key switch
        {
            "avg" when metric.Id == "fps" => "AVERAGE FPS",
            "max" => "MAX",
            "min" => "MIN",
            _ => catalogLabel.ToUpperInvariant(),
        };

    private void ResetKpiValues()
    {
        foreach (var tile in KpiTiles)
        {
            tile.Apply("--");
        }
    }

    private static double? ValueFor(MetricStatistics? stats, string key) => key switch
    {
        "avg" => stats?.Avg,
        "min" => stats?.Min,
        "max" => stats?.Max,
        "p1" => stats?.P1,
        "p01" => stats?.P01,
        _ => null,
    };

    partial void OnSelectedMetricChanged(MetricDefinition? value)
    {
        ConfigureKpiTiles(value);
        RefreshSeries();
        UpdateVisibleRange(_visibleBounds);
    }

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
