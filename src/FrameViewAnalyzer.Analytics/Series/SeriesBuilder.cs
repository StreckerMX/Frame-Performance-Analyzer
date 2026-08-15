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
        var (series, _) = BuildCore(session, metricId, includeXs: true);
        return series;
    }

    /// <summary>
    /// Y values only (comparison/statistics hot path); identical values to
    /// <see cref="Build"/> without the x coordinates.
    /// </summary>
    public static double[] Values(SessionAnalysis session, string metricId)
    {
        var (_, ys) = BuildCore(session, metricId, includeXs: false);
        return ys;
    }

    private static (MetricSeries Series, double[] Ys) BuildCore(
        SessionAnalysis session,
        string metricId,
        bool includeXs)
    {
        var metric = ResolveMetric(session, metricId);
        if (metric is null)
        {
            return (new MetricSeries(CoreMetricCatalog.CoreById["fps"], [], []), []);
        }

        if (session.Samples.Count == 0 || session.Window is null)
        {
            return (new MetricSeries(metric, [], []), []);
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

                if (includeXs)
                {
                    xs.Add(summary.Start - origin);
                }

                ys.Add(summary.Fps.Value);
            }
        }
        else
        {
            // Column indices are resolved once per metric and the scratch
            // buffer is reused across bins, so the bin loop allocates
            // neither a header-set per row nor a list per bin.
            var columns = MetricValueResolver.MetricColumns.Resolve(session.Capture, metric);
            var buffer = new double[16];
            foreach (var index in session.ValidBins.Order())
            {
                if (!session.RowsByBin.TryGetValue(index, out var sampleIndices))
                {
                    continue;
                }

                if (sampleIndices.Length > buffer.Length)
                {
                    buffer = new double[sampleIndices.Length];
                }

                var count = 0;
                foreach (var sampleIndex in sampleIndices)
                {
                    var value = MetricValueResolver.GetMetricValue(
                        session.Capture,
                        metric,
                        session.Samples.RowIndex[sampleIndex],
                        columns);
                    if (value is not null)
                    {
                        buffer[count++] = value.Value;
                    }
                }

                if (count < AnalysisConstants.MinFramesPerBin)
                {
                    continue;
                }

                var sum = 0.0;
                for (var i = 0; i < count; i++)
                {
                    sum += buffer[i];
                }

                if (includeXs)
                {
                    xs.Add(index * AnalysisConstants.FpsBinSeconds - origin);
                }

                ys.Add(sum / count);
            }
        }

        var yArray = ys.ToArray();
        return (new MetricSeries(metric, xs.ToArray(), yArray), yArray);
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
