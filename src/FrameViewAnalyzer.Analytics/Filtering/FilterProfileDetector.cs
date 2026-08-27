using FrameViewAnalyzer.Analytics.Bins;
using FrameViewAnalyzer.Analytics.Samples;
using FrameViewAnalyzer.Core.Math;

namespace FrameViewAnalyzer.Analytics.Filtering;

/// <summary>
/// Detects the analyzable segment of a capture: GPU-utilization filtering,
/// abnormal-FPS culling, smart transition-edge cleanup, sustained-run grouping,
/// and user-controlled outer-edge trimming. FrameView keeps the established
/// one-second analysis model while avoiding fixed extra cuts around every load.
/// </summary>
public static class FilterProfileDetector
{
    private const int TransitionProbeDepth = 2;
    private const int TransitionBaselineBins = 3;
    private const double TransitionFpsRatio = 1.25;
    private const double TransitionFpsDelta = 30.0;
    private const double TransitionGpuRatio = 0.85;
    private const double TransitionGpuDelta = 10.0;

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
    /// Robust upper fence for bin FPS, used to cull obvious transition frames:
    /// min(5000, max(q3 + 3·IQR, median·1.75, median + 30)) over bins with
    /// enough usable samples and sufficient GPU utilization.
    /// </summary>
    public static double? ComputeFpsUpperBound(
        IReadOnlyList<BinSummary> summaries,
        double gpuThreshold,
        int minimumSamplesPerBin = AnalysisConstants.MinFramesPerBin)
    {
        minimumSamplesPerBin = Math.Max(1, minimumSamplesPerBin);
        var candidates = summaries
            .Where(summary =>
                summary.Fps.HasValue
                && summary.FrameCount >= minimumSamplesPerBin
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
        var robustLimit = Math.Max(
            q3.Value + 3.0 * iqr,
            Math.Max(median.Value * 1.75, median.Value + 30.0));
        return Math.Min(AnalysisConstants.FpsChartCap, robustLimit);
    }

    public static FilterProfile Detect(
        IReadOnlyList<BinSummary> summaries,
        double threshold,
        double trimBufferSeconds,
        bool excludeTransitions,
        int minimumSamplesPerBin = AnalysisConstants.MinFramesPerBin)
    {
        if (summaries.Count == 0)
        {
            return new FilterProfile(null, new HashSet<int>(), new FilterDiagnostics());
        }

        minimumSamplesPerBin = Math.Max(1, minimumSamplesPerBin);
        var hasGpuData = summaries.Any(summary => summary.GpuUtil.HasValue);
        var fpsUpperBound = excludeTransitions
            ? ComputeFpsUpperBound(summaries, threshold, minimumSamplesPerBin)
            : null;

        var belowGpuIndices = new HashSet<int>();
        var fpsOutlierIndices = new HashSet<int>();
        var candidates = new List<int>(summaries.Count);

        foreach (var summary in summaries)
        {
            if (!excludeTransitions)
            {
                candidates.Add(summary.Index);
                continue;
            }

            var gpuOk = !hasGpuData
                || (summary.GpuUtil.HasValue && summary.GpuUtil >= threshold);
            if (!gpuOk)
            {
                belowGpuIndices.Add(summary.Index);
                continue;
            }

            if (fpsUpperBound.HasValue
                && summary.Fps.HasValue
                && summary.FrameCount >= minimumSamplesPerBin
                && summary.Fps > fpsUpperBound)
            {
                fpsOutlierIndices.Add(summary.Index);
                continue;
            }

            candidates.Add(summary.Index);
        }

        if (candidates.Count == 0)
        {
            return new FilterProfile(
                null,
                new HashSet<int>(),
                new FilterDiagnostics(
                    TotalBins: summaries.Count,
                    BelowGpuBins: excludeTransitions && hasGpuData ? summaries.Count : 0,
                    FpsUpperBound: fpsUpperBound));
        }

        var candidateRuns = ConsecutiveRuns(candidates);
        var sustained = candidateRuns
            .Where(run => run.Count >= AnalysisConstants.MinActiveRunSeconds)
            .ToList();
        var chosenRuns = sustained.Count > 0 ? sustained : candidateRuns;

        // Loading screens can leave one or two high-FPS/low-GPU seconds on the
        // gameplay side of a gap. Probe only the immediate internal edges and
        // compare them to their local scene baseline. This avoids arbitrary
        // fixed trimming around every transition while removing the distinctive
        // pre/post-load spike pattern seen in real FrameView captures.
        var transitionEdgeIndices = excludeTransitions
            ? FindTransitionEdgeSpikes(summaries, chosenRuns)
            : new HashSet<int>();
        if (transitionEdgeIndices.Count > 0)
        {
            candidates = candidates
                .Where(index => !transitionEdgeIndices.Contains(index))
                .ToList();
        }

        if (candidates.Count == 0)
        {
            return new FilterProfile(
                null,
                new HashSet<int>(),
                new FilterDiagnostics(
                    TotalBins: summaries.Count,
                    BelowGpuBins: belowGpuIndices.Count,
                    FpsOutlierBins: fpsOutlierIndices.Count,
                    TransitionEdgeBins: transitionEdgeIndices.Count,
                    FpsUpperBound: fpsUpperBound));
        }

        var survivingRuns = ConsecutiveRuns(candidates);
        var survivingSustained = survivingRuns
            .Where(run => run.Count >= AnalysisConstants.MinActiveRunSeconds)
            .ToList();
        var windowRuns = survivingSustained.Count > 0 ? survivingSustained : survivingRuns;
        var first = windowRuns[0][0];
        var last = windowRuns[^1][^1];

        var trim = NormalizeTrimBuffer(trimBufferSeconds);
        var start = first * AnalysisConstants.FpsBinSeconds;
        var end = (last + 1) * AnalysisConstants.FpsBinSeconds;
        if (trim > 0 && end - start > trim * 2 + AnalysisConstants.FpsBinSeconds)
        {
            start += trim;
            end -= trim;
        }

        var visible = new HashSet<int>(candidates.Where(index =>
            start <= index * AnalysisConstants.FpsBinSeconds
            && index * AnalysisConstants.FpsBinSeconds < end));

        var inWindow = summaries
            .Where(summary => start <= summary.Start && summary.Start < end)
            .ToList();
        var belowGpu = excludeTransitions
            ? inWindow.Count(summary => belowGpuIndices.Contains(summary.Index))
            : 0;
        var outliers = excludeTransitions
            ? inWindow.Count(summary => fpsOutlierIndices.Contains(summary.Index))
            : 0;
        var transitionEdges = excludeTransitions
            ? inWindow.Count(summary => transitionEdgeIndices.Contains(summary.Index))
            : 0;
        var edgeTrimmed = summaries.Count(summary =>
            summary.Start < start || summary.Start >= end);

        var diagnostics = new FilterDiagnostics(
            TotalBins: summaries.Count,
            VisibleBins: visible.Count,
            BelowGpuBins: belowGpu,
            FpsOutlierBins: outliers,
            TransitionEdgeBins: transitionEdges,
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

    private static HashSet<int> FindTransitionEdgeSpikes(
        IReadOnlyList<BinSummary> summaries,
        IReadOnlyList<List<int>> runs)
    {
        var byIndex = summaries.ToDictionary(summary => summary.Index);
        var excluded = new HashSet<int>();

        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];
            if (run.Count < TransitionBaselineBins + 1)
            {
                continue;
            }

            // The first and final outer capture edges are already controlled by
            // Trim. Smart stabilization is reserved for internal loading gaps.
            if (runIndex > 0)
            {
                ProbeStart(run, byIndex, excluded);
            }

            if (runIndex < runs.Count - 1)
            {
                ProbeEnd(run, byIndex, excluded);
            }
        }

        return excluded;
    }

    private static void ProbeStart(
        IReadOnlyList<int> run,
        IReadOnlyDictionary<int, BinSummary> byIndex,
        ISet<int> excluded)
    {
        for (var depth = 0; depth < TransitionProbeDepth; depth++)
        {
            var candidatePosition = depth;
            var baselineStart = candidatePosition + 1;
            var baselineEnd = Math.Min(run.Count, baselineStart + TransitionBaselineBins);
            if (baselineEnd - baselineStart < 2)
            {
                break;
            }

            var candidate = byIndex[run[candidatePosition]];
            var baseline = new List<BinSummary>(baselineEnd - baselineStart);
            for (var i = baselineStart; i < baselineEnd; i++)
            {
                baseline.Add(byIndex[run[i]]);
            }

            if (!IsTransitionEdgeSpike(candidate, baseline))
            {
                break;
            }

            excluded.Add(candidate.Index);
        }
    }

    private static void ProbeEnd(
        IReadOnlyList<int> run,
        IReadOnlyDictionary<int, BinSummary> byIndex,
        ISet<int> excluded)
    {
        for (var depth = 0; depth < TransitionProbeDepth; depth++)
        {
            var candidatePosition = run.Count - 1 - depth;
            var baselineEnd = candidatePosition;
            var baselineStart = Math.Max(0, baselineEnd - TransitionBaselineBins);
            if (baselineEnd - baselineStart < 2)
            {
                break;
            }

            var candidate = byIndex[run[candidatePosition]];
            var baseline = new List<BinSummary>(baselineEnd - baselineStart);
            for (var i = baselineStart; i < baselineEnd; i++)
            {
                baseline.Add(byIndex[run[i]]);
            }

            if (!IsTransitionEdgeSpike(candidate, baseline))
            {
                break;
            }

            excluded.Add(candidate.Index);
        }
    }

    private static bool IsTransitionEdgeSpike(
        BinSummary candidate,
        IReadOnlyList<BinSummary> baseline)
    {
        if (candidate.Fps is null || candidate.GpuUtil is null)
        {
            return false;
        }

        var fpsValues = baseline
            .Where(summary => summary.Fps.HasValue)
            .Select(summary => summary.Fps!.Value)
            .OrderBy(value => value)
            .ToList();
        var gpuValues = baseline
            .Where(summary => summary.GpuUtil.HasValue)
            .Select(summary => summary.GpuUtil!.Value)
            .OrderBy(value => value)
            .ToList();
        if (fpsValues.Count < 2 || gpuValues.Count < 2)
        {
            return false;
        }

        var medianFps = FrameViewAnalyzer.Core.Math.Statistics.Percentile(fpsValues, 0.50);
        var medianGpu = FrameViewAnalyzer.Core.Math.Statistics.Percentile(gpuValues, 0.50);
        if (medianFps is null || medianGpu is null)
        {
            return false;
        }

        var fpsFloor = Math.Max(
            medianFps.Value * TransitionFpsRatio,
            medianFps.Value + TransitionFpsDelta);
        var gpuCeiling = Math.Min(
            medianGpu.Value * TransitionGpuRatio,
            medianGpu.Value - TransitionGpuDelta);

        return candidate.Fps.Value >= fpsFloor
            && candidate.GpuUtil.Value <= gpuCeiling;
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
