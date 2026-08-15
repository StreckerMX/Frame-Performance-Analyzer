using FrameViewAnalyzer.Analytics.Bins;
using FrameViewAnalyzer.Analytics.Samples;
using FrameViewAnalyzer.Core.Math;

namespace FrameViewAnalyzer.Analytics.Filtering;

/// <summary>
/// Detects the analyzable segment of a capture: GPU-utilization filtering,
/// abnormal-FPS (transition) culling, sustained-run grouping, and edge
/// trimming. Mirrors the Python reference algorithms exactly.
/// </summary>
public static class FilterProfileDetector
{
    public static double ClampGpuThreshold(double value) => Math.Max(0.0, Math.Min(100.0, value));

    public static double NormalizeTrimBuffer(double value)
    {
        if (!double.IsFinite(value))
        {
            return AnalysisConstants.DefaultTrimBufferSeconds;
        }

        return Math.Max(0.0, Math.Min(10.0, value));
    }

    /// <summary>
    /// Automatic GPU threshold: 55% of the 90th percentile of per-second
    /// utilization means, rounded and clamped to [5, 80].
    /// </summary>
    public static double ComputeAutoGpuThreshold(ParsedSamples samples)
    {
        var utilByBin = new Dictionary<int, List<double>>();
        var utils = new List<double>();

        for (var i = 0; i < samples.Count; i++)
        {
            var util = samples.GpuUtilPercent[i];
            if (!double.IsFinite(util))
            {
                continue;
            }

            var binIndex = (int)Math.Floor(samples.TimeSeconds[i]);
            if (!utilByBin.TryGetValue(binIndex, out var list))
            {
                list = [];
                utilByBin[binIndex] = list;
            }

            list.Add(util);
        }

        if (utilByBin.Count > 0)
        {
            utils = utilByBin.Values
                .Where(values => values.Count > 0)
                .Select(values => values.Sum() / values.Count)
                .ToList();
        }

        if (utils.Count == 0)
        {
            return AnalysisConstants.DefaultGpuThreshold;
        }

        var sorted = utils.OrderBy(value => value).ToList();
        var reference = FrameViewAnalyzer.Core.Math.Statistics.Percentile(sorted, 0.90);
        if (reference is null)
        {
            return AnalysisConstants.DefaultGpuThreshold;
        }

        var threshold = Math.Round(reference.Value * AnalysisConstants.AutoGpuRatio, MidpointRounding.ToEven);
        return ClampGpuThreshold(Math.Max(
            AnalysisConstants.AutoGpuMin,
            Math.Min(AnalysisConstants.AutoGpuMax, threshold)));
    }

    /// <summary>
    /// Robust upper fence for bin FPS, used to cull transition frames:
    /// min(5000, max(q3 + 3·IQR, median·1.75, median + 30)) over bins with
    /// at least three frames and sufficient GPU utilization.
    /// </summary>
    public static double? ComputeFpsUpperBound(
        IReadOnlyList<BinSummary> summaries,
        double gpuThreshold)
    {
        var candidates = summaries
            .Where(summary =>
                summary.Fps.HasValue
                && summary.FrameCount >= AnalysisConstants.MinFramesPerBin
                && (!summary.GpuUtil.HasValue || summary.GpuUtil >= gpuThreshold))
            .Select(summary => summary.Fps!.Value)
            .OrderBy(value => value)
            .ToList();

        if (candidates.Count < 8)
        {
            return null;
        }

        var median = FrameViewAnalyzer.Core.Math.Statistics.Percentile(candidates, 0.50);
        var q1 = FrameViewAnalyzer.Core.Math.Statistics.Percentile(candidates, 0.25);
        var q3 = FrameViewAnalyzer.Core.Math.Statistics.Percentile(candidates, 0.75);
        if (median is null || q1 is null || q3 is null)
        {
            return null;
        }

        var iqr = Math.Max(0.0, q3.Value - q1.Value);
        // A far-outlier fence removes transition frames without clipping
        // normal scene-to-scene variation in a benchmark.
        var robustLimit = Math.Max(
            q3.Value + 3.0 * iqr,
            Math.Max(median.Value * 1.75, median.Value + 30.0));
        return Math.Min(AnalysisConstants.FpsChartCap, robustLimit);
    }

    public static FilterProfile Detect(
        IReadOnlyList<BinSummary> summaries,
        double threshold,
        double trimBufferSeconds,
        bool excludeTransitions)
    {
        if (summaries.Count == 0)
        {
            return new FilterProfile(null, new HashSet<int>(), new FilterDiagnostics());
        }

        var hasGpuData = summaries.Any(summary => summary.GpuUtil.HasValue);
        var fpsUpperBound = excludeTransitions
            ? ComputeFpsUpperBound(summaries, threshold)
            : null;

        var gpuCandidates = new List<int>();
        var transitionIndices = new HashSet<int>();
        foreach (var summary in summaries)
        {
            var gpuOk = !hasGpuData
                || (summary.GpuUtil.HasValue && summary.GpuUtil >= threshold);
            if (!gpuOk)
            {
                continue;
            }

            gpuCandidates.Add(summary.Index);
            if (fpsUpperBound.HasValue
                && summary.Fps.HasValue
                && summary.Fps > fpsUpperBound)
            {
                transitionIndices.Add(summary.Index);
            }
        }

        if (gpuCandidates.Count == 0)
        {
            // No bin meets the GPU filter: there is no active segment to
            // analyze. Returning the whole capture would silently distort
            // every statistic.
            return new FilterProfile(
                null,
                new HashSet<int>(),
                new FilterDiagnostics(
                    TotalBins: summaries.Count,
                    BelowGpuBins: hasGpuData ? summaries.Count : 0,
                    FpsUpperBound: fpsUpperBound));
        }

        var validCandidates = gpuCandidates
            .Where(index => !transitionIndices.Contains(index))
            .ToList();
        var runs = ConsecutiveRuns(validCandidates);
        var sustained = runs.Where(run => run.Count >= AnalysisConstants.MinActiveRunSeconds).ToList();
        var chosenRuns = sustained.Count > 0 ? sustained : runs;
        // Multi-scene benchmarks intentionally contain gaps for loading.
        // Keep the envelope of every sustained scene and exclude only
        // invalid bins.
        var first = chosenRuns[0][0];
        var last = chosenRuns[^1][^1];

        var trim = NormalizeTrimBuffer(trimBufferSeconds);
        var start = first * AnalysisConstants.FpsBinSeconds;
        var end = (last + 1) * AnalysisConstants.FpsBinSeconds;
        if (trim > 0 && end - start > trim * 2 + AnalysisConstants.FpsBinSeconds)
        {
            start += trim;
            end -= trim;
        }

        var visible = new HashSet<int>(validCandidates.Where(index =>
            start <= index * AnalysisConstants.FpsBinSeconds
            && index * AnalysisConstants.FpsBinSeconds < end));

        var inWindow = summaries
            .Where(summary => start <= summary.Start && summary.Start < end)
            .ToList();
        var belowGpu = inWindow.Count(summary =>
            hasGpuData && (!summary.GpuUtil.HasValue || summary.GpuUtil < threshold));
        var outliers = inWindow.Count(summary => transitionIndices.Contains(summary.Index));
        var edgeTrimmed = summaries.Count(summary =>
            summary.Start < start || summary.Start >= end);

        var diagnostics = new FilterDiagnostics(
            TotalBins: summaries.Count,
            VisibleBins: visible.Count,
            BelowGpuBins: belowGpu,
            FpsOutlierBins: outliers,
            EdgeTrimmedBins: edgeTrimmed,
            FpsUpperBound: fpsUpperBound);

        return new FilterProfile(new ActiveWindow(start, end), visible, diagnostics);
    }

    /// <summary>Active window only (used by metadata extraction).</summary>
    public static ActiveWindow? InferActiveWindow(
        ParsedSamples samples,
        double threshold,
        double trimBufferSeconds)
    {
        var summaries = BinBuilder.BuildSummaries(samples);
        return Detect(summaries, threshold, trimBufferSeconds, excludeTransitions: true).Window;
    }

    private static List<List<int>> ConsecutiveRuns(List<int> indices)
    {
        if (indices.Count == 0)
        {
            return [];
        }

        var runs = new List<List<int>> { new List<int> { indices[0] } };
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
