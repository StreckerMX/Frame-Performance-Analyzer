using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.Analytics.Series;

/// <summary>
/// Lazily builds frame-level chart samples. Nothing in this type runs during
/// normal capture loading: callers opt in explicitly, and completed results
/// are cached by immutable SessionAnalysis + metric.
///
/// FPS uses a centered 32-frame harmonic rolling estimate instead of raw
/// 1000/MsBetweenPresents points. Raw per-present FPS is extremely noisy with
/// modern presentation pipelines and Frame Generation; the rolling estimate
/// retains one point per recorded frame while tracking the meaningful local
/// frame rate. Other metrics expose their row-level values directly.
/// </summary>
public static class FramePointSeriesBuilder
{
    public const int FpsRollingWindowFrames = 32;

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
            ? BuildRollingFps(session, metric, analyzedXByBin)
            : BuildRawMetric(session, metric, analyzedXByBin);
    }

    private static MetricSeries BuildRollingFps(
        SessionAnalysis session,
        MetricDefinition metric,
        IReadOnlyDictionary<int, double> analyzedXByBin)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        var runs = ConsecutiveRuns(session.ValidBins.Order().ToList());

        foreach (var run in runs)
        {
            var sampleIndices = new List<int>();
            foreach (var bin in run)
            {
                if (!session.RowsByBin.TryGetValue(bin, out var rows))
                {
                    continue;
                }

                foreach (var sampleIndex in rows)
                {
                    var frameTime = session.Samples.FrametimeMs[sampleIndex];
                    if (double.IsFinite(frameTime) && frameTime > 0)
                    {
                        sampleIndices.Add(sampleIndex);
                    }
                }
            }

            if (sampleIndices.Count == 0)
            {
                continue;
            }

            var prefixMs = new double[sampleIndices.Count + 1];
            for (var i = 0; i < sampleIndices.Count; i++)
            {
                prefixMs[i + 1] = prefixMs[i] + session.Samples.FrametimeMs[sampleIndices[i]];
            }

            for (var i = 0; i < sampleIndices.Count; i++)
            {
                var start = Math.Max(0, i - (FpsRollingWindowFrames / 2 - 1));
                var end = Math.Min(sampleIndices.Count, start + FpsRollingWindowFrames);
                start = Math.Max(0, end - FpsRollingWindowFrames);
                var count = end - start;
                var totalMs = prefixMs[end] - prefixMs[start];
                if (count <= 0 || totalMs <= 0 || !double.IsFinite(totalMs))
                {
                    continue;
                }

                var sampleIndex = sampleIndices[i];
                var time = session.Samples.TimeSeconds[sampleIndex];
                var bin = (int)Math.Floor(time);
                if (!analyzedXByBin.TryGetValue(bin, out var binX))
                {
                    continue;
                }

                var fps = 1000.0 * count / totalMs;
                if (!double.IsFinite(fps) || fps <= 0 || fps > AnalysisConstants.FpsChartCap)
                {
                    continue;
                }

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

    private static List<List<int>> ConsecutiveRuns(List<int> indices)
    {
        if (indices.Count == 0)
        {
            return [];
        }

        var runs = new List<List<int>> { new() { indices[0] } };
        for (var i = 1; i < indices.Count; i++)
        {
            if (indices[i] == runs[^1][^1] + 1)
            {
                runs[^1].Add(indices[i]);
            }
            else
            {
                runs.Add(new List<int> { indices[i] });
            }
        }

        return runs;
    }
}
