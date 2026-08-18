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
/// One analyzed session shown by the chart workspace. The reference session is
/// always normalized to index 0; Pair mode therefore remains a two-item
/// workspace while Multi mode can provide any number of additional sessions.
/// </summary>
public sealed record ChartWorkspaceSession(
    SessionAnalysis Session,
    string? Label = null,
    bool IsReference = false);

/// <summary>
/// Chart-side state: the active workspace sessions, metric catalog, selected
/// metric, full-resolution series, interaction toggles, and metric-aware
/// visible-range KPI tiles. Rendering lives in the chart layer; this view
/// model only carries data.
/// </summary>
public partial class ChartViewModel : ObservableObject
{
    private ScottPlot.AxisLimits? _visibleBounds;
    private IReadOnlyList<ChartWorkspaceSession> _workspaceSessions = [];
    private IReadOnlyList<MetricSeries> _seriesList = [];

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

    /// <summary>All active workspace sessions, with the reference first.</summary>
    public IReadOnlyList<ChartWorkspaceSession> WorkspaceSessions => _workspaceSessions;

    /// <summary>All series available for the selected metric, reference first when present.</summary>
    public IReadOnlyList<MetricSeries> SeriesList => _seriesList;

    public bool IsMultiWorkspace => _workspaceSessions.Count > 2;

    /// <summary>Compatibility entry point for the existing Pair workflow.</summary>
    public void SetSessions(SessionAnalysis? baseSession, SessionAnalysis? comparisonSession)
    {
        if (baseSession is null)
        {
            Clear();
            return;
        }

        var sessions = new List<ChartWorkspaceSession>
        {
            new(baseSession, IsReference: true),
        };
        if (comparisonSession is not null)
        {
            sessions.Add(new ChartWorkspaceSession(comparisonSession, "Comparison"));
        }

        SetWorkspace(sessions);
    }

    /// <summary>
    /// Replaces the complete chart workspace. A declared reference is moved to
    /// index 0, preserving deterministic legend order and reference KPIs.
    /// </summary>
    public void SetWorkspace(IReadOnlyList<ChartWorkspaceSession> sessions)
    {
        var normalized = sessions.Where(item => item.Session is not null).ToList();
        if (normalized.Count == 0)
        {
            Clear();
            return;
        }

        var referenceIndex = normalized.FindIndex(item => item.IsReference);
        if (referenceIndex < 0)
        {
            referenceIndex = 0;
        }

        if (referenceIndex != 0)
        {
            var reference = normalized[referenceIndex];
            normalized.RemoveAt(referenceIndex);
            normalized.Insert(0, reference);
        }

        // Exactly one item is authoritative even if callers accidentally mark
        // more than one reference.
        _workspaceSessions = normalized
            .Select((item, index) => item with { IsReference = index == 0 })
            .ToList();

        Session = _workspaceSessions[0].Session;
        ComparisonSession = _workspaceSessions.Count == 2
            ? _workspaceSessions[1].Session
            : null;
        _visibleBounds = null;

        var previousMetricId = SelectedMetric?.Id;
        Metrics.Clear();
        foreach (var metric in MetricUnion(_workspaceSessions.Select(item => item.Session)))
        {
            Metrics.Add(metric);
        }

        SelectedMetric = Metrics.FirstOrDefault(metric => metric.Id == previousMetricId)
            ?? (Metrics.Count > 0 ? Metrics[0] : null);
        RefreshSeries();
        ConfigureKpiTiles(SelectedMetric);
        UpdateVisibleRange(null);
        OnPropertyChanged(nameof(WorkspaceSessions));
        OnPropertyChanged(nameof(IsMultiWorkspace));
    }

    public void Clear()
    {
        _workspaceSessions = [];
        _seriesList = [];
        Session = null;
        ComparisonSession = null;
        Metrics.Clear();
        SelectedMetric = null;
        Series = null;
        ComparisonSeries = null;
        HasData = false;
        _visibleBounds = null;
        ConfigureKpiTiles(CoreMetricCatalog.CoreById["fps"]);
        ResetKpiValues();
        OnPropertyChanged(nameof(WorkspaceSessions));
        OnPropertyChanged(nameof(SeriesList));
        OnPropertyChanged(nameof(IsMultiWorkspace));
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
    /// Pair mode compares Base and Comparison. Multi mode intentionally keeps
    /// the compact KPI strip focused on the reference benchmark; the full
    /// N-way comparison table is a separate presentation concern.
    /// </summary>
    public void UpdateVisibleRange(ScottPlot.AxisLimits? bounds)
    {
        _visibleBounds = bounds;
        var metric = SelectedMetric;
        if (metric is null || SeriesList.Count == 0)
        {
            ResetKpiValues();
            return;
        }

        var populated = SeriesList.Where(series => series.X.Length > 0).ToList();
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
        if (_workspaceSessions.Count == 2
            && ComparisonSeries is { X.Length: > 0 } comparisonSeries)
        {
            (comparisonStats, comparisonCount) = VisibleRangeCalculator.Compute(
                metric, comparisonSeries.X, comparisonSeries.Y, minX, maxX);
        }

        var fields = KpiFields(metric);
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
        var labels = KpiFields(selected)
            .Select(field => field.Label)
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

    /// <summary>
    /// The dashboard deliberately uses one compact statistic vocabulary.
    /// Every metric shows Average/Max/Min; only FPS adds the two low-tail
    /// metrics because those are the stutter indicators users expect there.
    /// </summary>
    private static IReadOnlyList<(string Key, string Label)> KpiFields(MetricDefinition metric) =>
        metric.Id == "fps"
            ?
            [
                ("avg", "AVERAGE"),
                ("p1", "1% LOW"),
                ("p01", "0.1% LOW"),
                ("max", "Max"),
                ("min", "Min"),
            ]
            :
            [
                ("avg", "AVERAGE"),
                ("max", "Max"),
                ("min", "Min"),
            ];

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
        if (_workspaceSessions.Count == 0 || SelectedMetric is null)
        {
            _seriesList = [];
            Series = null;
            ComparisonSeries = null;
            HasData = false;
            OnPropertyChanged(nameof(SeriesList));
            return;
        }

        var built = new List<MetricSeries>();
        MetricSeries? referenceSeries = null;
        MetricSeries? firstComparisonSeries = null;

        for (var index = 0; index < _workspaceSessions.Count; index++)
        {
            var workspace = _workspaceSessions[index];
            var raw = SeriesBuilder.Build(workspace.Session, SelectedMetric.Id);
            if (raw.Y.Length == 0)
            {
                continue;
            }

            var series = raw with
            {
                Label = workspace.Label,
                Role = index == 0 ? SessionRole.Base : SessionRole.Comparison,
            };
            built.Add(series);
            if (index == 0)
            {
                referenceSeries = series;
            }
            else if (firstComparisonSeries is null)
            {
                firstComparisonSeries = series;
            }
        }

        _seriesList = built;
        Series = referenceSeries;
        ComparisonSeries = firstComparisonSeries;
        HasData = built.Count > 0;

        // Force a presentation refresh even when the reference does not carry
        // a comparison-only metric and the nullable adapter properties remain
        // unchanged. The chart itself consumes SeriesList.
        OnPropertyChanged(nameof(SeriesList));
        OnPropertyChanged(nameof(Series));
        OnPropertyChanged(nameof(ComparisonSeries));
    }

    /// <summary>
    /// Pair-only points consumed by the legacy A/B range-analysis actions.
    /// Multi mode intentionally exposes no second point set here.
    /// </summary>
    public (IReadOnlyList<ChartPoint> Base, IReadOnlyList<ChartPoint> Comparison) CurrentPoints() =>
        (ToPoints(Series), ToPoints(_workspaceSessions.Count == 2 ? ComparisonSeries : null));

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

    private static IReadOnlyList<MetricDefinition> MetricUnion(IEnumerable<SessionAnalysis> sessions)
    {
        var result = new List<MetricDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            foreach (var metric in session.Catalog)
            {
                if (seen.Add(metric.Id))
                {
                    result.Add(metric);
                }
            }
        }

        return result;
    }
}
