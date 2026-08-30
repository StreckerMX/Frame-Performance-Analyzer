using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// One analyzed session shown by the chart workspace. Pair mode may declare a
/// Base reference; Multi mode deliberately treats every item as an equal peer.
/// </summary>
public sealed record ChartWorkspaceSession(
    SessionAnalysis Session,
    string? Label = null,
    bool IsReference = false);

/// <summary>
/// Chart-side state: active workspace sessions, metric catalog, selected
/// metric, full-resolution series, interaction toggles, and metric-aware
/// visible-range KPI tiles. Rendering lives in the chart layer.
/// </summary>
public partial class ChartViewModel : ObservableObject
{
    private ScottPlot.AxisLimits? _visibleBounds;
    private IReadOnlyList<ChartWorkspaceSession> _workspaceSessions = [];
    private IReadOnlyList<MetricSeries> _seriesList = [];
    private IReadOnlyList<MetricSeries> _framePointSeriesList = [];
    private bool _isMultiWorkspace;

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

    /// <summary>All active workspace sessions in stable display order.</summary>
    public IReadOnlyList<ChartWorkspaceSession> WorkspaceSessions => _workspaceSessions;

    /// <summary>All one-second summary series available for the selected metric.</summary>
    public IReadOnlyList<MetricSeries> SeriesList => _seriesList;

    public bool IsMultiWorkspace => _isMultiWorkspace;

    /// <summary>
    /// Switches KPI calculations to the full-resolution frame representation.
    /// Rendering decimation never reaches this method, so every statistic uses
    /// the original per-frame values in the active viewport.
    /// </summary>
    public void SetFramePointSeries(IReadOnlyList<MetricSeries> seriesList)
    {
        var metricId = SelectedMetric?.Id;
        _framePointSeriesList = metricId is null
            ? []
            : seriesList
                .Where(series => string.Equals(series.Metric.Id, metricId, StringComparison.Ordinal))
                .ToList();
        UpdateVisibleRange(_visibleBounds);
    }

    /// <summary>Restores KPI calculations to the one-second summary series.</summary>
    public void ClearFramePointSeries()
    {
        if (_framePointSeriesList.Count == 0)
        {
            return;
        }

        _framePointSeriesList = [];
        UpdateVisibleRange(_visibleBounds);
    }

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
            new(baseSession, "Base", IsReference: true),
        };
        if (comparisonSession is not null)
        {
            sessions.Add(new ChartWorkspaceSession(comparisonSession, "Comparison"));
        }

        SetWorkspace(sessions, isMultiWorkspace: false);
    }

    /// <summary>
    /// Replaces the complete chart workspace. Pair mode keeps Base first.
    /// Multi mode preserves selection order and intentionally has no reference.
    /// </summary>
    public void SetWorkspace(
        IReadOnlyList<ChartWorkspaceSession> sessions,
        bool isMultiWorkspace = false)
    {
        var normalized = sessions.Where(item => item.Session is not null).ToList();
        if (normalized.Count == 0)
        {
            Clear();
            return;
        }

        _isMultiWorkspace = isMultiWorkspace;
        if (_isMultiWorkspace)
        {
            _workspaceSessions = normalized
                .Select(item => item with { IsReference = false })
                .ToList();
        }
        else
        {
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

            _workspaceSessions = normalized
                .Select((item, index) => item with { IsReference = index == 0 })
                .ToList();
        }

        // Session/ComparisonSession remain compatibility adapters used by the
        // Pair-only commands. Multi keeps Session as the first loaded item but
        // never treats it as a statistical baseline.
        Session = _workspaceSessions[0].Session;
        ComparisonSession = !_isMultiWorkspace && _workspaceSessions.Count == 2
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
        _framePointSeriesList = [];
        _isMultiWorkspace = false;
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
    /// the selection actually changed.
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
    /// Pair mode compares Base and Comparison. Multi mode shows one colored
    /// row per benchmark and highlights the best value versus the runner-up.
    /// </summary>
    public void UpdateVisibleRange(ScottPlot.AxisLimits? bounds)
    {
        _visibleBounds = bounds;
        var metric = SelectedMetric;
        var statisticsSeries = ActiveStatisticsSeries();
        if (metric is null || statisticsSeries.Count == 0)
        {
            ResetKpiValues();
            return;
        }

        var populated = statisticsSeries.Where(series => series.X.Length > 0).ToList();
        if (populated.Count == 0)
        {
            ResetKpiValues();
            return;
        }

        var (minX, maxX) = bounds is { } range
            ? (range.Left, range.Right)
            : (populated.Min(series => series.X[0]), populated.Max(series => series.X[^1]));

        if (_isMultiWorkspace)
        {
            var multiStats = new List<MultiVisibleStats>(populated.Count);
            foreach (var series in populated)
            {
                var (stats, _) = VisibleRangeCalculator.Compute(
                    metric, series.X, series.Y, minX, maxX);
                var summaryCount = VisibleSummaryCount(metric, series, minX, maxX);
                multiStats.Add(new MultiVisibleStats(series, stats, summaryCount));
            }

            var fields = KpiFields(metric);
            for (var index = 0; index < fields.Count; index++)
            {
                ApplyMultiMetricTile(KpiTiles[index], metric, fields[index].Key, multiStats);
            }

            ApplyMultiVisibleTimeTile(KpiTiles[^1], multiStats);
            return;
        }

        MetricStatistics? baseStats = null;
        var baseCount = 0;
        var baseSeries = populated.FirstOrDefault(series => series.Role == SessionRole.Base);
        if (baseSeries is not null)
        {
            (baseStats, _) = VisibleRangeCalculator.Compute(
                metric, baseSeries.X, baseSeries.Y, minX, maxX);
            baseCount = VisibleSummaryCount(metric, baseSeries, minX, maxX);
        }

        MetricStatistics? comparisonStats = null;
        var comparisonCount = 0;
        var comparisonSeries = populated.FirstOrDefault(
            series => series.Role == SessionRole.Comparison);
        if (comparisonSeries is not null)
        {
            (comparisonStats, _) = VisibleRangeCalculator.Compute(
                metric, comparisonSeries.X, comparisonSeries.Y, minX, maxX);
            comparisonCount = VisibleSummaryCount(metric, comparisonSeries, minX, maxX);
        }

        var pairFields = KpiFields(metric);
        for (var index = 0; index < pairFields.Count; index++)
        {
            var (key, _) = pairFields[index];
            ApplyMetricTile(
                KpiTiles[index],
                metric,
                key,
                ValueFor(baseStats, key),
                ValueFor(comparisonStats, key));
        }

        ApplyVisibleTimeTile(KpiTiles[^1], baseStats, baseCount, comparisonStats, comparisonCount);
    }

    private IReadOnlyList<MetricSeries> ActiveStatisticsSeries() =>
        _framePointSeriesList.Count > 0 ? _framePointSeriesList : _seriesList;

    private int VisibleSummaryCount(
        MetricDefinition metric,
        MetricSeries statisticsSeries,
        double minX,
        double maxX)
    {
        var summarySeries = _seriesList.FirstOrDefault(
            series => series.WorkspaceIndex == statisticsSeries.WorkspaceIndex);
        if (summarySeries is null)
        {
            return 0;
        }

        var (_, count) = VisibleRangeCalculator.Compute(
            metric,
            summarySeries.X,
            summarySeries.Y,
            minX,
            maxX);
        return count;
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

    private static void ApplyMultiMetricTile(
        KpiTileViewModel tile,
        MetricDefinition metric,
        string statisticKey,
        IReadOnlyList<MultiVisibleStats> entries)
    {
        var values = entries
            .Select(entry => new MultiMetricValue(
                entry.Series,
                ValueFor(entry.Stats, statisticKey)))
            .ToList();

        MultiMetricValue? best = null;
        MultiMetricValue? runnerUp = null;
        if (metric.Direction != MetricDirection.Undefined)
        {
            var valid = values.Where(value => value.Value is not null).ToList();
            if (valid.Count >= 2)
            {
                var ordered = metric.Direction == MetricDirection.HigherIsBetter
                    ? valid.OrderByDescending(value => value.Value!.Value).ToList()
                    : valid.OrderBy(value => value.Value!.Value).ToList();
                best = ordered[0];
                runnerUp = ordered[1];
            }
        }

        var bestDeltaText = string.Empty;
        if (best?.Value is { } bestValue && runnerUp?.Value is { } nextValue)
        {
            var percent = SignedDifferenceVsRunnerUp(bestValue, nextValue);
            if (percent is { } signedPercent && Math.Abs(signedPercent) > 0.0001)
            {
                bestDeltaText = $"{signedPercent:+0.0;-0.0;0.0}%";
            }
        }

        tile.ApplySeries(values.Select(value =>
        {
            var isBest = best is not null && ReferenceEquals(value.Series, best.Series);
            return new KpiSeriesValueViewModel(
                value.Series.LabelOrDefault,
                FormatMultiValue(metric, value.Value),
                MultiSeriesPalette.HexAt(value.Series.WorkspaceIndex),
                isBest ? bestDeltaText : string.Empty,
                isBest);
        }));
    }

    private static double? SignedDifferenceVsRunnerUp(double best, double next)
    {
        var denominator = Math.Abs(next);
        if (denominator < 1e-12)
        {
            return null;
        }

        return (best - next) / denominator * 100.0;
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

    private static void ApplyMultiVisibleTimeTile(
        KpiTileViewModel tile,
        IReadOnlyList<MultiVisibleStats> entries)
    {
        tile.ApplySeries(entries.Select(entry => new KpiSeriesValueViewModel(
            entry.Series.LabelOrDefault,
            DisplayText.FormatDurationHuman(entry.Count),
            MultiSeriesPalette.HexAt(entry.Series.WorkspaceIndex))));
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

    private static string FormatMultiValue(MetricDefinition metric, double? value)
    {
        if (value is null)
        {
            return "--";
        }

        if (metric.Id == "fps")
        {
            return $"{value:F1} FPS";
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
    /// Every metric shows Average/Max/Min; only FPS adds the low-tail metrics.
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
        // A metric/workspace refresh invalidates any lazily-built frame series.
        _framePointSeriesList = [];

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
        MetricSeries? firstSeries = null;
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
                Role = _isMultiWorkspace
                    ? SessionRole.Comparison
                    : index == 0 ? SessionRole.Base : SessionRole.Comparison,
                WorkspaceIndex = index,
                IsReference = !_isMultiWorkspace && workspace.IsReference,
            };
            built.Add(series);
            firstSeries ??= series;
            if (!_isMultiWorkspace && index > 0 && firstComparisonSeries is null)
            {
                firstComparisonSeries = series;
            }
        }

        _seriesList = built;
        Series = firstSeries;
        ComparisonSeries = firstComparisonSeries;
        HasData = built.Count > 0;

        OnPropertyChanged(nameof(SeriesList));
        OnPropertyChanged(nameof(Series));
        OnPropertyChanged(nameof(ComparisonSeries));
    }

    /// <summary>
    /// Pair-only points consumed by the legacy A/B range-analysis actions.
    /// Multi mode intentionally exposes no comparison point set here.
    /// </summary>
    public (IReadOnlyList<ChartPoint> Base, IReadOnlyList<ChartPoint> Comparison) CurrentPoints() =>
        (ToPoints(Series), ToPoints(_isMultiWorkspace ? null : ComparisonSeries));

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

    /// <summary>
    /// Metrics shown in the chart selector must produce at least one analyzed
    /// point in the current workspace. Raw logs can contain numeric telemetry
    /// columns whose usable values all disappear after filtering; those remain
    /// part of the session catalog/details, but are not useful chart choices.
    /// </summary>
    private static IReadOnlyList<MetricDefinition> MetricUnion(IEnumerable<SessionAnalysis> sessions)
    {
        var result = new List<MetricDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            foreach (var metric in session.Catalog)
            {
                if (seen.Contains(metric.Id)
                    || SeriesBuilder.Values(session, metric.Id).Length == 0)
                {
                    continue;
                }

                seen.Add(metric.Id);
                result.Add(metric);
            }
        }

        return result;
    }

    private sealed record MultiVisibleStats(
        MetricSeries Series,
        MetricStatistics? Stats,
        int Count);

    private sealed record MultiMetricValue(
        MetricSeries Series,
        double? Value);
}
