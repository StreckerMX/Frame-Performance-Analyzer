using FrameViewAnalyzer.Core.Math;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.Analytics.Series;

/// <summary>
/// Builds per-bin metric series for a session. FPS points come from the
/// harmonic bin summaries; other metrics average the per-frame values of
/// each valid bin. Bins with fewer than three usable values are skipped.
/// </summary>
public static class SeriesBuilder
{
    public static MetricSeries Build(SessionAnalysis session, string metricId)
    {
        var metric = ResolveMetric(session, metricId);
        if (metric is null)
        {
            return new MetricSeries(CoreMetricCatalog.CoreById["fps"], [], []);
        }

        if (session.Samples.Count == 0 || session.Window is null)
        {
            return new MetricSeries(metric, [], []);
        }

        var origin = session.Window.Start;
        var xs = new List<double>();
        var ys = new List<double>();

        if (metricId == "fps")
        {
            foreach (var summary in session.Bins)
            {
                if (!session.ValidBins.Contains(summary.Index))
                {
                    continue;
                }

                if (summary.FrameCount < AnalysisConstants.MinFramesPerBin
                    || summary.Fps is null
                    || summary.Fps <= 0
                    || summary.Fps > AnalysisConstants.FpsChartCap)
                {
                    continue;
                }

                xs.Add(summary.Start - origin);
                ys.Add(summary.Fps.Value);
            }
        }
        else
        {
            foreach (var index in session.ValidBins.Order())
            {
                if (!session.RowsByBin.TryGetValue(index, out var sampleIndices))
                {
                    continue;
                }

                var values = new List<double>(sampleIndices.Length);
                foreach (var sampleIndex in sampleIndices)
                {
                    var value = MetricValueResolver.GetMetricValue(
                        session.Capture,
                        metric,
                        session.Samples.RowIndex[sampleIndex]);
                    if (value is not null)
                    {
                        values.Add(value.Value);
                    }
                }

                if (values.Count < AnalysisConstants.MinFramesPerBin)
                {
                    continue;
                }

                xs.Add(index * AnalysisConstants.FpsBinSeconds - origin);
                ys.Add(FrameViewAnalyzer.Core.Math.Statistics.Mean(values) ?? 0.0);
            }
        }

        return new MetricSeries(metric, xs.ToArray(), ys.ToArray());
    }

    private static MetricDefinition? ResolveMetric(SessionAnalysis session, string metricId)
    {
        foreach (var metric in session.Catalog)
        {
            if (metric.Id == metricId)
            {
                return metric;
            }
        }

        return metricId == "fps" ? CoreMetricCatalog.CoreById["fps"] : null;
    }
}
