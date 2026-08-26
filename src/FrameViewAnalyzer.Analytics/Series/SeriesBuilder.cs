using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Series;

/// <summary>
/// Builds per-bin metric series for a session. FrameView FPS points come from
/// harmonic per-frame summaries. Sampled telemetry sources use their
/// source-aware bin summaries. Other metrics average usable values per bin.
/// Portable imports return the analyzed series embedded by FrameView Analyzer
/// directly, so a round-trip never reinterprets already-processed data.
///
/// Session analyses are immutable, so completed metric series are cached by
/// session + metric. The cache is weakly rooted by SessionAnalysis and is
/// released automatically with the session. This makes repeated Pair/Multi
/// metric switches allocation-free and avoids rescanning long captures.
/// </summary>
public static class SeriesBuilder
{
    private static readonly ConditionalWeakTable<
        SessionAnalysis,
        ConcurrentDictionary<string, Lazy<MetricSeries>>> SeriesCaches = new();

    public static MetricSeries Build(SessionAnalysis session, string metricId)
    {
        var cache = SeriesCaches.GetValue(
            session,
            static _ => new ConcurrentDictionary<string, Lazy<MetricSeries>>(StringComparer.Ordinal));
        return cache.GetOrAdd(
            metricId,
            id => new Lazy<MetricSeries>(
                () => BuildUncached(session, id),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>
    /// Y values only. The complete analyzed series is cached because the chart
    /// will usually need the X coordinates immediately afterwards; doing the
    /// work once is substantially faster than rescanning the capture twice.
    /// </summary>
    public static double[] Values(SessionAnalysis session, string metricId) =>
        Build(session, metricId).Y;

    /// <summary>
    /// Pre-computes every metric for one immutable analysis snapshot. Safe to
    /// call from a background thread and safe to race with normal Build calls.
    /// Lazy cache entries guarantee each metric is produced at most once.
    /// </summary>
    public static void Warm(SessionAnalysis session, CancellationToken cancellationToken = default)
    {
        foreach (var metric in session.Catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = Build(session, metric.Id);
        }
    }

    private static MetricSeries BuildUncached(SessionAnalysis session, string metricId)
    {
        var metric = ResolveMetric(session, metricId);
        if (metric is null)
        {
            return new MetricSeries(CoreMetricCatalog.CoreById["fps"], [], [], SourceSession: session);
        }

        if (session.ImportedSeries is { } imported
            && imported.TryGetValue(metricId, out var importedSeries))
        {
            return new MetricSeries(
                metric,
                importedSeries.X,
                importedSeries.Y,
                SourceSession: session);
        }

        if (session.Samples.Count == 0 || session.Window is null)
        {
            return new MetricSeries(metric, [], [], SourceSession: session);
        }

        var minimumSamplesPerBin = CaptureSourceDetector.IsNvidiaAppPerformanceLog(session.Capture)
            ? 1
            : AnalysisConstants.MinFramesPerBin;
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

                if (summary.FrameCount < minimumSamplesPerBin
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

                if (count < minimumSamplesPerBin)
                {
                    continue;
                }

                var sum = 0.0;
                for (var i = 0; i < count; i++)
                {
                    sum += buffer[i];
                }

                xs.Add(index * AnalysisConstants.FpsBinSeconds - origin);
                ys.Add(sum / count);
            }
        }

        return new MetricSeries(metric, xs.ToArray(), ys.ToArray(), SourceSession: session);
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
