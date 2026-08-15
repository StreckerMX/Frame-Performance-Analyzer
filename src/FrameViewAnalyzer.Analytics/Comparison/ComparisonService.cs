using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.Analytics.Comparison;

public sealed class ComparisonService : IComparisonService
{
    public IReadOnlyList<ComparisonRow> Compare(
        SessionAnalysis baseSession,
        SessionAnalysis? comparisonSession = null)
    {
        var metrics = MetricUnion(baseSession, comparisonSession);
        var comparisonName = comparisonSession?.Capture.DisplayName ?? string.Empty;
        var rows = new List<ComparisonRow>();

        foreach (var metric in metrics)
        {
            var baseStats = StatisticsFor(baseSession, metric);
            var comparisonStats = comparisonSession is null
                ? new MetricStatistics(metric.Id, null, null, null, null, null)
                : StatisticsFor(comparisonSession, metric);

            foreach (var (key, label) in CoreMetricCatalog.StatFields(metric.Id))
            {
                var baseValue = ValueFor(baseStats, key);
                var comparisonValue = comparisonSession is null ? null : ValueFor(comparisonStats, key);
                var (delta, deltaPercent) = ComputeDelta(baseValue, comparisonValue);
                rows.Add(new ComparisonRow(
                    MetricId: metric.Id,
                    MetricLabel: metric.Label,
                    Category: metric.Category,
                    Unit: metric.Unit,
                    StatisticKey: key,
                    StatisticLabel: label,
                    BaseSession: baseSession.Capture.DisplayName,
                    BaseValue: baseValue,
                    ComparisonSession: comparisonName,
                    ComparisonValue: comparisonValue,
                    Delta: delta,
                    DeltaPercent: deltaPercent,
                    Kind: CoreMetricCatalog.ClassifyImprovement(metric.Direction, baseValue, comparisonValue)));
            }
        }

        return rows;
    }

    /// <summary>Comparison minus base, plus the percent change relative to the base.</summary>
    public static (double? Delta, double? DeltaPercent) ComputeDelta(
        double? baseValue,
        double? comparisonValue)
    {
        if (baseValue is null || comparisonValue is null)
        {
            return (null, null);
        }

        var delta = comparisonValue.Value - baseValue.Value;
        var deltaPercent = baseValue != 0
            ? delta / Math.Abs(baseValue.Value) * 100.0
            : (double?)null;
        return (delta, deltaPercent);
    }

    private static IReadOnlyList<MetricDefinition> MetricUnion(
        SessionAnalysis baseSession,
        SessionAnalysis? comparisonSession)
    {
        var metrics = new List<MetricDefinition>(baseSession.Catalog);
        var seen = new HashSet<string>(metrics.Select(metric => metric.Id), StringComparer.Ordinal);
        if (comparisonSession is not null)
        {
            foreach (var metric in comparisonSession.Catalog)
            {
                if (seen.Add(metric.Id))
                {
                    metrics.Add(metric);
                }
            }
        }

        return metrics;
    }

    private static MetricStatistics StatisticsFor(SessionAnalysis session, MetricDefinition metric) =>
        StatisticsCalculator.Compute(metric, SeriesBuilder.Build(session, metric.Id).Y);

    private static double? ValueFor(MetricStatistics stats, string key) => key switch
    {
        "avg" => stats.Avg,
        "min" => stats.Min,
        "max" => stats.Max,
        "p1" => stats.P1,
        "p01" => stats.P01,
        _ => null,
    };
}
