using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.Analytics.Series;

/// <summary>
/// Lazily builds true frame-level chart samples. Nothing in this type runs
/// during normal capture loading: callers opt in explicitly, and completed
/// results are cached by immutable SessionAnalysis + metric.
///
/// FPS uses each analyzed frame's actual 1000/MsBetweenPresents value. The
/// interactive chart is responsible for viewport decimation, so zooming can
/// progressively reveal the original frames without smoothing real stutters
/// or spikes out of the data. Other metrics expose their row-level values.
/// </summary>
public static class FramePointSeriesBuilder
{
    private static readonly ConditionalWeakTable<
        SessionAnalysis,
        ConcurrentDictionary<string, Lazy<MetricSeries>>> Caches = new();

    public static MetricSeries Build(SessionAnalysis session, string metricId)
    {
        var cache = Caches.GetValue(
            session,
            static _ => new ConcurrentDictionary<string, Lazy<MetricSeries>>(StringComparer.Ordinal));
        return cache.GetOrAdd(
            metricId,
            id => new Lazy<MetricSeries>(
                () => BuildUncached(session, id),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public static bool IsCached(SessionAnalysis session, string metricId) =>
        Caches.TryGetValue(session, out var cache)
        && cache.TryGetValue(metricId, out var lazy)
        && lazy.IsValueCreated;

    private static MetricSeries BuildUncached(SessionAnalysis session, string metricId)
    {
        var metric = ResolveMetric(session, metricId);
        if (metric is null)
        {
            return new MetricSeries(CoreMetricCatalog.CoreById["fps"], [], [], SourceSession: session);
        }

        // Portable analyzed-data exports intentionally contain only the
        // one-second analyzed series, not raw capture rows.
        if (session.IsPortableImport || session.Samples.Count == 0 || session.ValidBins.Count == 0)
        {
            return new MetricSeries(metric, [], [], SourceSession: session);
        }

        var analyzedXByBin = SeriesBuilder.BuildAnalyzedTimeline(session.ValidBins);
        return metricId == "fps"
            ? BuildRawFps(session, metric, analyzedXByBin)
            : BuildRawMetric(session, metric, analyzedXByBin);
    }

    private static MetricSeries BuildRawFps(
        SessionAnalysis session,
        MetricDefinition metric,
        IReadOnlyDictionary<int, double> analyzedXByBin)
    {
        var xs = new List<double>();
        var ys = new List<double>();

        foreach (var bin in session.ValidBins.Order())
        {
            if (!analyzedXByBin.TryGetValue(bin, out var binX)
                || !session.RowsByBin.TryGetValue(bin, out var sampleIndices))
            {
                continue;
            }

            foreach (var sampleIndex in sampleIndices)
            {
                var frameTime = session.Samples.FrametimeMs[sampleIndex];
                if (!double.IsFinite(frameTime) || frameTime <= 0)
                {
                    continue;
                }

                var fps = 1000.0 / frameTime;
                if (!double.IsFinite(fps) || fps <= 0 || fps > AnalysisConstants.FpsChartCap)
                {
                    continue;
                }

                var time = session.Samples.TimeSeconds[sampleIndex];
                xs.Add(CompressedFrameX(binX, time, bin));
                ys.Add(fps);
            }
        }

        return new MetricSeries(metric, xs.ToArray(), ys.ToArray(), SourceSession: session);
    }

    private static MetricSeries BuildRawMetric(
        SessionAnalysis session,
        MetricDefinition metric,
        IReadOnlyDictionary<int, double> analyzedXByBin)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        var columns = MetricValueResolver.MetricColumns.Resolve(session.Capture, metric);

        foreach (var bin in session.ValidBins.Order())
        {
            if (!analyzedXByBin.TryGetValue(bin, out var binX)
                || !session.RowsByBin.TryGetValue(bin, out var sampleIndices))
            {
                continue;
            }

            foreach (var sampleIndex in sampleIndices)
            {
                var value = MetricValueResolver.GetMetricValue(
                    session.Capture,
                    metric,
                    session.Samples.RowIndex[sampleIndex],
                    columns);
                if (value is null || !double.IsFinite(value.Value))
                {
                    continue;
                }

                var time = session.Samples.TimeSeconds[sampleIndex];
                xs.Add(CompressedFrameX(binX, time, bin));
                ys.Add(value.Value);
            }
        }

        return new MetricSeries(metric, xs.ToArray(), ys.ToArray(), SourceSession: session);
    }

    private static double CompressedFrameX(double binX, double captureTime, int captureBin)
    {
        var fraction = captureTime - captureBin;
        if (!double.IsFinite(fraction))
        {
            fraction = 0.0;
        }

        return binX + Math.Clamp(fraction, 0.0, AnalysisConstants.FpsBinSeconds - 1e-9);
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
