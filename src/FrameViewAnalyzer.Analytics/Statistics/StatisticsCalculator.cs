using FrameViewAnalyzer.Core.Math;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.Analytics.Statistics;

/// <summary>
/// Computes per-metric statistics over a series' values. Percentile tails
/// are direction-aware and match the Python reference exactly.
/// </summary>
public static class StatisticsCalculator
{
    private static readonly IReadOnlySet<string> HighTailMetrics = new HashSet<string>(StringComparer.Ordinal)
    {
        "frametime",
        "latency",
        "render_present_latency",
        "until_displayed",
        "in_present_api",
        "flip_delay",
    };

    public static MetricStatistics Compute(MetricDefinition metric, IReadOnlyList<double> values)
    {
        var fields = CoreMetricCatalog.StatFields(metric.Id);
        var requested = new HashSet<string>(fields.Select(field => field.Key), StringComparer.Ordinal);

        double? avg = null;
        double? min = null;
        double? max = null;
        double? p1 = null;
        double? p01 = null;

        if (values.Count > 0)
        {
            var sorted = values.OrderBy(value => value).ToList();
            if (requested.Contains("avg"))
            {
                avg = FrameViewAnalyzer.Core.Math.Statistics.Mean(values);
            }

            if (requested.Contains("min"))
            {
                min = sorted[0];
            }

            if (requested.Contains("max"))
            {
                max = sorted[^1];
            }

            if (requested.Contains("p1"))
            {
                p1 = FrameViewAnalyzer.Core.Math.Statistics.Percentile(
                    sorted,
                    HighTailMetrics.Contains(metric.Id) ? 0.99 : 0.01);
            }

            if (requested.Contains("p01"))
            {
                p01 = FrameViewAnalyzer.Core.Math.Statistics.Percentile(
                    sorted,
                    HighTailMetrics.Contains(metric.Id) ? 0.999 : 0.001);
            }
        }

        return new MetricStatistics(metric.Id, avg, min, max, p1, p01);
    }
}
