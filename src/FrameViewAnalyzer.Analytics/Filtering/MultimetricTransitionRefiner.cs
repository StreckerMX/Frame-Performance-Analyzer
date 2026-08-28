using FrameViewAnalyzer.Analytics.Bins;
using FrameViewAnalyzer.Analytics.Samples;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Analytics.Filtering;

/// <summary>
/// Conservatively refines only the one/two seconds immediately beside an
/// already-detected internal loading gap. A real FPS regression is never
/// removed just because FPS is low: frame cadence must look transitional and
/// at least two independent FrameView telemetry families must also leave their
/// local gameplay baseline. This lets genuine slow gameplay remain in the
/// benchmark while removing partially contaminated load-transition seconds.
/// </summary>
internal static class MultimetricTransitionRefiner
{
    private const int ProbeDepth = 2;
    private const int BaselineBins = 3;

    public static FilterProfile Refine(
        CaptureData capture,
        ParsedSamples samples,
        IReadOnlyDictionary<int, int[]> rowsByBin,
        IReadOnlyList<BinSummary> summaries,
        FilterProfile profile)
    {
        if (profile.ValidBins.Count == 0 || summaries.Count == 0)
        {
            return profile;
        }

        var runs = ConsecutiveRuns(profile.ValidBins.Order().ToList());
        if (runs.Count < 2)
        {
            return profile;
        }

        var summaryByIndex = summaries.ToDictionary(summary => summary.Index);
        var reader = new TelemetryReader(capture, samples, rowsByBin);
        var excluded = new HashSet<int>();

        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];
            if (run.Count < BaselineBins + 1)
            {
                continue;
            }

            // Outer capture edges remain the user's Trim responsibility. The
            // multimetric classifier only reasons about internal gaps which
            // the first-pass loading/FPS filter already identified.
            if (runIndex > 0)
            {
                ProbeStart(run, summaryByIndex, reader, excluded);
            }

            if (runIndex < runs.Count - 1)
            {
                ProbeEnd(run, summaryByIndex, reader, excluded);
            }
        }

        if (excluded.Count == 0)
        {
            return profile;
        }

        var visible = new HashSet<int>(profile.ValidBins.Where(index => !excluded.Contains(index)));
        var diagnostics = profile.Diagnostics with
        {
            VisibleBins = visible.Count,
            TransitionEdgeBins = profile.Diagnostics.TransitionEdgeBins + excluded.Count,
        };

        return profile with { ValidBins = visible, Diagnostics = diagnostics };
    }

    private static void ProbeStart(
        IReadOnlyList<int> run,
        IReadOnlyDictionary<int, BinSummary> summaries,
        TelemetryReader reader,
        ISet<int> excluded)
    {
        for (var depth = 0; depth < ProbeDepth; depth++)
        {
            var candidatePosition = depth;
            var baselineStart = candidatePosition + 1;
            var baselineEnd = Math.Min(run.Count, baselineStart + BaselineBins);
            if (baselineEnd - baselineStart < 2)
            {
                break;
            }

            var baseline = new List<int>(baselineEnd - baselineStart);
            for (var index = baselineStart; index < baselineEnd; index++)
            {
                baseline.Add(run[index]);
            }

            var candidate = run[candidatePosition];
            if (!IsContaminatedTransition(candidate, baseline, summaries, reader))
            {
                break;
            }

            excluded.Add(candidate);
        }
    }

    private static void ProbeEnd(
        IReadOnlyList<int> run,
        IReadOnlyDictionary<int, BinSummary> summaries,
        TelemetryReader reader,
        ISet<int> excluded)
    {
        for (var depth = 0; depth < ProbeDepth; depth++)
        {
            var candidatePosition = run.Count - 1 - depth;
            var baselineEnd = candidatePosition;
            var baselineStart = Math.Max(0, baselineEnd - BaselineBins);
            if (baselineEnd - baselineStart < 2)
            {
                break;
            }

            var baseline = new List<int>(baselineEnd - baselineStart);
            for (var index = baselineStart; index < baselineEnd; index++)
            {
                baseline.Add(run[index]);
            }

            var candidate = run[candidatePosition];
            if (!IsContaminatedTransition(candidate, baseline, summaries, reader))
            {
                break;
            }

            excluded.Add(candidate);
        }
    }

    private static bool IsContaminatedTransition(
        int candidateIndex,
        IReadOnlyList<int> baselineIndices,
        IReadOnlyDictionary<int, BinSummary> summaries,
        TelemetryReader reader)
    {
        if (!summaries.TryGetValue(candidateIndex, out var candidateSummary))
        {
            return false;
        }

        var baselineSummaries = baselineIndices
            .Where(summaries.ContainsKey)
            .Select(index => summaries[index])
            .ToList();
        if (baselineSummaries.Count < 2)
        {
            return false;
        }

        var candidate = reader.Read(candidateIndex);
        var baseline = TelemetrySnapshot.Median(baselineIndices.Select(reader.Read));

        var cadenceEvidence = 0;
        if (StrongFpsDeviation(candidateSummary.Fps, Median(baselineSummaries.Select(item => item.Fps))))
        {
            cadenceEvidence++;
        }

        cadenceEvidence += StrongRatioDeviation(candidate.FrameTimeMs, baseline.FrameTimeMs, 0.68, 1.45) ? 1 : 0;
        cadenceEvidence += StrongRatioDeviation(candidate.SimulationIntervalMs, baseline.SimulationIntervalMs, 0.68, 1.45) ? 1 : 0;
        cadenceEvidence += StrongRatioDeviation(candidate.DisplayIntervalMs, baseline.DisplayIntervalMs, 0.68, 1.45) ? 1 : 0;
        var cadenceStrong = cadenceEvidence >= 2;
        if (!cadenceStrong)
        {
            // This is the key guardrail for genuine performance events. A low
            // FPS second by itself is benchmark data, not a loading transition.
            return false;
        }

        var gpuEvidence = 0;
        gpuEvidence += StrongLow(candidateSummary.GpuUtil, Median(baselineSummaries.Select(item => item.GpuUtil)), 0.86, 8.0) ? 1 : 0;
        gpuEvidence += StrongLow(candidate.GpuPowerW, baseline.GpuPowerW, 0.78, 12.0) ? 1 : 0;
        gpuEvidence += StrongLow(candidate.GpuClockMhz, baseline.GpuClockMhz, 0.90, 100.0) ? 1 : 0;
        gpuEvidence += StrongLow(candidate.GpuMemoryClockMhz, baseline.GpuMemoryClockMhz, 0.90, 150.0) ? 1 : 0;
        var gpuStateChanged = gpuEvidence >= 2;

        var pipelineEvidence = 0;
        pipelineEvidence += StrongRatioDeviation(candidate.RenderQueueDepth, baseline.RenderQueueDepth, 0.35, 2.50) ? 1 : 0;
        pipelineEvidence += StrongRatioDeviation(candidate.PcLatencyMs, baseline.PcLatencyMs, 0.55, 1.70) ? 1 : 0;
        pipelineEvidence += StrongRatioDeviation(candidate.RenderPresentLatencyMs, baseline.RenderPresentLatencyMs, 0.55, 1.70) ? 1 : 0;
        pipelineEvidence += StrongRatioDeviation(candidate.UntilDisplayedMs, baseline.UntilDisplayedMs, 0.55, 1.70) ? 1 : 0;
        pipelineEvidence += StrongRatioDeviation(candidate.PresentApiMs, baseline.PresentApiMs, 0.55, 1.70) ? 1 : 0;
        pipelineEvidence += StrongRatioDeviation(candidate.FlipDelayMs, baseline.FlipDelayMs, 0.55, 1.70) ? 1 : 0;
        var pipelineStateChanged = pipelineEvidence >= 2;

        var cpuEvidence = 0;
        cpuEvidence += StrongRatioDeviation(candidate.CpuUtilPercent, baseline.CpuUtilPercent, 0.65, 1.55, 8.0) ? 1 : 0;
        cpuEvidence += StrongRatioDeviation(candidate.CpuPowerW, baseline.CpuPowerW, 0.75, 1.40, 5.0) ? 1 : 0;
        cpuEvidence += StrongRatioDeviation(candidate.CpuClockMhz, baseline.CpuClockMhz, 0.85, 1.15, 150.0) ? 1 : 0;
        var cpuStateChanged = cpuEvidence >= 2;

        var presentationStateChanged =
            DiscreteChanged(candidate.FrameGenMultiplier, baseline.FrameGenMultiplier)
            || DiscreteChanged(candidate.DroppedRate, baseline.DroppedRate, tolerance: 0.20);

        // Require agreement from at least two non-FPS telemetry families. This
        // is deliberately conservative: a real GPU-heavy stutter, CPU spike,
        // or isolated FPS collapse remains visible unless the rest of FrameView
        // simultaneously says the application is changing operating state.
        var independentStateFamilies =
            (gpuStateChanged ? 1 : 0)
            + (pipelineStateChanged ? 1 : 0)
            + (cpuStateChanged ? 1 : 0)
            + (presentationStateChanged ? 1 : 0);

        return independentStateFamilies >= 2;
    }

    private static bool StrongFpsDeviation(double? candidate, double? baseline)
    {
        if (candidate is null || baseline is null || baseline <= 0)
        {
            return false;
        }

        var ratio = candidate.Value / baseline.Value;
        return Math.Abs(candidate.Value - baseline.Value) >= 15.0
            && (ratio <= 0.72 || ratio >= 1.35);
    }

    private static bool StrongLow(
        double? candidate,
        double? baseline,
        double ratioLimit,
        double minimumDelta)
    {
        if (candidate is null || baseline is null || baseline <= 0)
        {
            return false;
        }

        return baseline.Value - candidate.Value >= minimumDelta
            && candidate.Value / baseline.Value <= ratioLimit;
    }

    private static bool StrongRatioDeviation(
        double? candidate,
        double? baseline,
        double lowRatio,
        double highRatio,
        double minimumDelta = 0.0)
    {
        if (candidate is null || baseline is null || !double.IsFinite(candidate.Value)
            || !double.IsFinite(baseline.Value))
        {
            return false;
        }

        if (Math.Abs(candidate.Value - baseline.Value) < minimumDelta)
        {
            return false;
        }

        if (Math.Abs(baseline.Value) < 1e-9)
        {
            return Math.Abs(candidate.Value) > Math.Max(minimumDelta, 0.25);
        }

        var ratio = candidate.Value / baseline.Value;
        return ratio <= lowRatio || ratio >= highRatio;
    }

    private static bool DiscreteChanged(double? candidate, double? baseline, double tolerance = 0.25)
    {
        if (candidate is null || baseline is null)
        {
            return false;
        }

        return Math.Abs(candidate.Value - baseline.Value) > tolerance;
    }

    private static double? Median(IEnumerable<double?> values)
    {
        var finite = values
            .Where(value => value.HasValue && double.IsFinite(value.Value))
            .Select(value => value!.Value)
            .OrderBy(value => value)
            .ToArray();
        if (finite.Length == 0)
        {
            return null;
        }

        var middle = finite.Length / 2;
        return finite.Length % 2 == 0
            ? (finite[middle - 1] + finite[middle]) / 2.0
            : finite[middle];
    }

    private static List<List<int>> ConsecutiveRuns(List<int> indices)
    {
        if (indices.Count == 0)
        {
            return [];
        }

        var runs = new List<List<int>> { new() { indices[0] } };
        for (var index = 1; index < indices.Count; index++)
        {
            if (indices[index] == runs[^1][^1] + 1)
            {
                runs[^1].Add(indices[index]);
            }
            else
            {
                runs.Add(new List<int> { indices[index] });
            }
        }

        return runs;
    }

    /// <summary>
    /// Lazily reads only bins inspected at internal boundaries. This keeps the
    /// normal loading path fast even for captures with hundreds of thousands of
    /// frames and many telemetry columns.
    /// </summary>
    private sealed class TelemetryReader
    {
        private readonly CaptureData _capture;
        private readonly ParsedSamples _samples;
        private readonly IReadOnlyDictionary<int, int[]> _rowsByBin;
        private readonly Dictionary<int, TelemetrySnapshot> _cache = [];

        private readonly int _simulation;
        private readonly int _display;
        private readonly int _gpuPower;
        private readonly int _gpuClock;
        private readonly int _gpuMemoryClock;
        private readonly int _cpuUtil;
        private readonly int _cpuPower;
        private readonly int _cpuClock;
        private readonly int _renderQueue;
        private readonly int _pcLatency;
        private readonly int _renderPresentLatency;
        private readonly int _untilDisplayed;
        private readonly int _presentApi;
        private readonly int _flipDelay;
        private readonly int _frameGen;
        private readonly int _dropped;

        public TelemetryReader(
            CaptureData capture,
            ParsedSamples samples,
            IReadOnlyDictionary<int, int[]> rowsByBin)
        {
            _capture = capture;
            _samples = samples;
            _rowsByBin = rowsByBin;
            _simulation = Resolve("MsBetweenSimulationStart");
            _display = Resolve("MsBetweenDisplayChange");
            _gpuPower = Resolve("NV Pwr(W) (API)", "GPUOnlyPwr(W) (API)", "PCAT Power Total(W)", "GPU NV Power (Watts) (API)");
            _gpuClock = Resolve("GPU0Clk(MHz)");
            _gpuMemoryClock = Resolve("GPU0MemClk(MHz)");
            _cpuUtil = Resolve("CPUUtil(%)", "CPU Util %", "CPU Utilization(%)");
            _cpuPower = Resolve("CPU Package Power(W)", "CPU Package Power(Watts)");
            _cpuClock = Resolve("CPUClk(MHz)");
            _renderQueue = Resolve("Render Queue Depth");
            _pcLatency = Resolve("MsPCLatency", "Average PC Latency(MSec)", "AvgPCLatency (ms)");
            _renderPresentLatency = Resolve("MsRenderPresentLatency", "RenderPresentLatency (ms)");
            _untilDisplayed = Resolve("MsUntilDisplayed");
            _presentApi = Resolve("MsInPresentAPI");
            _flipDelay = Resolve("MsFlipDelay");
            _frameGen = Resolve("Frame Gen Multiplier");
            _dropped = Resolve("Dropped");
        }

        public TelemetrySnapshot Read(int bin)
        {
            if (_cache.TryGetValue(bin, out var cached))
            {
                return cached;
            }

            if (!_rowsByBin.TryGetValue(bin, out var sampleIndices) || sampleIndices.Length == 0)
            {
                return _cache[bin] = new TelemetrySnapshot();
            }

            var frameTimes = sampleIndices
                .Select(index => _samples.FrametimeMs[index])
                .Where(value => double.IsFinite(value) && value > 0)
                .OrderBy(value => value)
                .ToArray();

            var result = new TelemetrySnapshot(
                FrameTimeMs: MedianFinite(frameTimes),
                SimulationIntervalMs: MedianColumn(_simulation, sampleIndices),
                DisplayIntervalMs: MedianColumn(_display, sampleIndices),
                GpuPowerW: MedianColumn(_gpuPower, sampleIndices),
                GpuClockMhz: MedianColumn(_gpuClock, sampleIndices),
                GpuMemoryClockMhz: MedianColumn(_gpuMemoryClock, sampleIndices),
                CpuUtilPercent: MedianColumn(_cpuUtil, sampleIndices),
                CpuPowerW: MedianColumn(_cpuPower, sampleIndices),
                CpuClockMhz: MedianColumn(_cpuClock, sampleIndices),
                RenderQueueDepth: MedianColumn(_renderQueue, sampleIndices),
                PcLatencyMs: MedianColumn(_pcLatency, sampleIndices),
                RenderPresentLatencyMs: MedianColumn(_renderPresentLatency, sampleIndices),
                UntilDisplayedMs: MedianColumn(_untilDisplayed, sampleIndices),
                PresentApiMs: MedianColumn(_presentApi, sampleIndices),
                FlipDelayMs: MedianColumn(_flipDelay, sampleIndices),
                FrameGenMultiplier: MedianColumn(_frameGen, sampleIndices),
                DroppedRate: MeanColumn(_dropped, sampleIndices));
            _cache[bin] = result;
            return result;
        }

        private int Resolve(params string[] headers)
        {
            foreach (var header in headers)
            {
                var index = _capture.IndexOfHeader(header);
                if (index >= 0)
                {
                    return index;
                }
            }

            return -1;
        }

        private double? MedianColumn(int column, IReadOnlyList<int> sampleIndices)
        {
            if (column < 0)
            {
                return null;
            }

            var values = Values(column, sampleIndices).OrderBy(value => value).ToArray();
            return MedianFinite(values);
        }

        private double? MeanColumn(int column, IReadOnlyList<int> sampleIndices)
        {
            if (column < 0)
            {
                return null;
            }

            var values = Values(column, sampleIndices).ToArray();
            return values.Length == 0 ? null : values.Average();
        }

        private IEnumerable<double> Values(int column, IReadOnlyList<int> sampleIndices)
        {
            foreach (var sampleIndex in sampleIndices)
            {
                var row = _samples.RowIndex[sampleIndex];
                if (CsvValues.TryParseNumber(_capture.Cell(column, row), out var value)
                    && double.IsFinite(value))
                {
                    yield return value;
                }
            }
        }

        private static double? MedianFinite(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return null;
            }

            var middle = values.Count / 2;
            return values.Count % 2 == 0
                ? (values[middle - 1] + values[middle]) / 2.0
                : values[middle];
        }
    }

    private sealed record TelemetrySnapshot(
        double? FrameTimeMs = null,
        double? SimulationIntervalMs = null,
        double? DisplayIntervalMs = null,
        double? GpuPowerW = null,
        double? GpuClockMhz = null,
        double? GpuMemoryClockMhz = null,
        double? CpuUtilPercent = null,
        double? CpuPowerW = null,
        double? CpuClockMhz = null,
        double? RenderQueueDepth = null,
        double? PcLatencyMs = null,
        double? RenderPresentLatencyMs = null,
        double? UntilDisplayedMs = null,
        double? PresentApiMs = null,
        double? FlipDelayMs = null,
        double? FrameGenMultiplier = null,
        double? DroppedRate = null)
    {
        public static TelemetrySnapshot Median(IEnumerable<TelemetrySnapshot> snapshots)
        {
            var list = snapshots.ToList();
            return new TelemetrySnapshot(
                FrameTimeMs: MultimetricTransitionRefiner.Median(list.Select(item => item.FrameTimeMs)),
                SimulationIntervalMs: MultimetricTransitionRefiner.Median(list.Select(item => item.SimulationIntervalMs)),
                DisplayIntervalMs: MultimetricTransitionRefiner.Median(list.Select(item => item.DisplayIntervalMs)),
                GpuPowerW: MultimetricTransitionRefiner.Median(list.Select(item => item.GpuPowerW)),
                GpuClockMhz: MultimetricTransitionRefiner.Median(list.Select(item => item.GpuClockMhz)),
                GpuMemoryClockMhz: MultimetricTransitionRefiner.Median(list.Select(item => item.GpuMemoryClockMhz)),
                CpuUtilPercent: MultimetricTransitionRefiner.Median(list.Select(item => item.CpuUtilPercent)),
                CpuPowerW: MultimetricTransitionRefiner.Median(list.Select(item => item.CpuPowerW)),
                CpuClockMhz: MultimetricTransitionRefiner.Median(list.Select(item => item.CpuClockMhz)),
                RenderQueueDepth: MultimetricTransitionRefiner.Median(list.Select(item => item.RenderQueueDepth)),
                PcLatencyMs: MultimetricTransitionRefiner.Median(list.Select(item => item.PcLatencyMs)),
                RenderPresentLatencyMs: MultimetricTransitionRefiner.Median(list.Select(item => item.RenderPresentLatencyMs)),
                UntilDisplayedMs: MultimetricTransitionRefiner.Median(list.Select(item => item.UntilDisplayedMs)),
                PresentApiMs: MultimetricTransitionRefiner.Median(list.Select(item => item.PresentApiMs)),
                FlipDelayMs: MultimetricTransitionRefiner.Median(list.Select(item => item.FlipDelayMs)),
                FrameGenMultiplier: MultimetricTransitionRefiner.Median(list.Select(item => item.FrameGenMultiplier)),
                DroppedRate: MultimetricTransitionRefiner.Median(list.Select(item => item.DroppedRate)));
        }
    }
}
