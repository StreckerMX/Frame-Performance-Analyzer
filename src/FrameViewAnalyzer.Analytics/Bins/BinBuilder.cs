using FrameViewAnalyzer.Analytics.Samples;

namespace FrameViewAnalyzer.Analytics.Bins;

/// <summary>Groups parsed samples into one-second bins and summary rows.</summary>
public static class BinBuilder
{
    /// <summary>
    /// Builds one-second summaries. FrameView data uses the harmonic FPS
    /// calculation from per-frame times. NVIDIA App logs already contain
    /// sampled FPS values, so those samples are averaged directly instead of
    /// pretending each telemetry row represents one rendered frame.
    /// </summary>
    public static IReadOnlyList<BinSummary> BuildSummaries(
        ParsedSamples samples,
        bool sampledFps = false)
    {
        var utilSums = new Dictionary<int, double>();
        var utilCounts = new Dictionary<int, int>();
        var fpsTotalMs = new Dictionary<int, double>();
        var fpsSampleSums = new Dictionary<int, double>();
        var frameCounts = new Dictionary<int, int>();

        for (var i = 0; i < samples.Count; i++)
        {
            var index = (int)Math.Floor(samples.TimeSeconds[i]);
            Accumulate(utilSums, utilCounts, index, samples.GpuUtilPercent[i], finiteOnly: true);

            if (sampledFps)
            {
                var fps = samples.Fps[i];
                if (double.IsFinite(fps) && fps > 0)
                {
                    Accumulate(fpsSampleSums, null, index, fps, finiteOnly: false);
                    frameCounts[index] = frameCounts.GetValueOrDefault(index) + 1;
                }

                continue;
            }

            var frameTime = samples.FrametimeMs[i];
            if (double.IsFinite(frameTime) && frameTime > 0)
            {
                Accumulate(fpsTotalMs, null, index, frameTime, finiteOnly: false);
                frameCounts[index] = frameCounts.GetValueOrDefault(index) + 1;
            }
        }

        // A bin exists for every sample with a time value, even when it has
        // no usable FPS samples (fps null, frame_count 0).
        var allIndices = new SortedSet<int>();
        foreach (var index in utilSums.Keys)
        {
            allIndices.Add(index);
        }

        foreach (var index in (sampledFps ? fpsSampleSums.Keys : fpsTotalMs.Keys))
        {
            allIndices.Add(index);
        }

        var summaries = new List<BinSummary>(allIndices.Count);
        foreach (var index in allIndices)
        {
            var utilCount = utilCounts.GetValueOrDefault(index);
            var frameCount = frameCounts.GetValueOrDefault(index);
            double? fps = null;
            if (frameCount > 0)
            {
                if (sampledFps)
                {
                    fps = fpsSampleSums.GetValueOrDefault(index) / frameCount;
                }
                else
                {
                    var totalMs = fpsTotalMs.GetValueOrDefault(index);
                    fps = totalMs > 0 ? 1000.0 * frameCount / totalMs : null;
                }
            }

            summaries.Add(new BinSummary(
                Index: index,
                Start: index * AnalysisConstants.FpsBinSeconds,
                GpuUtil: utilCount > 0 ? utilSums[index] / utilCount : null,
                Fps: fps,
                FrameCount: frameCount));
        }

        return summaries;
    }

    /// <summary>Groups sample indices by bin, sorted ascending by bin index.</summary>
    public static IReadOnlyDictionary<int, int[]> BuildRowsByBin(ParsedSamples samples)
    {
        var rows = new Dictionary<int, List<int>>();
        for (var i = 0; i < samples.Count; i++)
        {
            var index = (int)Math.Floor(samples.TimeSeconds[i]);
            if (!rows.TryGetValue(index, out var list))
            {
                list = [];
                rows[index] = list;
            }

            list.Add(i);
        }

        return rows.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private static void Accumulate(
        Dictionary<int, double> sums,
        Dictionary<int, int>? counts,
        int index,
        double value,
        bool finiteOnly)
    {
        if (finiteOnly && !double.IsFinite(value))
        {
            return;
        }

        sums[index] = sums.GetValueOrDefault(index) + value;
        if (counts is not null)
        {
            counts[index] = counts.GetValueOrDefault(index) + 1;
        }
    }
}
