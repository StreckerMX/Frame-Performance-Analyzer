using FrameViewAnalyzer.Analytics.Bins;
using FrameViewAnalyzer.Analytics.Filtering;
using FrameViewAnalyzer.Analytics.Samples;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Analytics;

public sealed class CaptureAnalysisService : ICaptureAnalysisService
{
    public SessionAnalysis Analyze(CaptureData capture, AnalysisOptions? options = null)
    {
        if (capture.Kind != CsvKind.Log)
        {
            throw new ArgumentException(
                "Session analysis requires a detailed FrameView log (*_Log.csv).");
        }

        options ??= new AnalysisOptions();
        var samples = ParsedSampleBuilder.Build(capture);
        var catalog = MetricCatalogBuilder.Build(capture);
        var bins = BinBuilder.BuildSummaries(samples);
        var rowsByBin = BinBuilder.BuildRowsByBin(samples);
        return Assemble(capture, catalog, samples, bins, rowsByBin, options);
    }

    public SessionAnalysis Reanalyze(SessionAnalysis previous, AnalysisOptions options) =>
        Assemble(
            previous.Capture,
            previous.Catalog,
            previous.Samples,
            previous.Bins,
            previous.RowsByBin,
            options);

    public double ComputeAutoGpuThreshold(ParsedSamples samples) =>
        FilterProfileDetector.ComputeAutoGpuThreshold(samples);

    private static SessionAnalysis Assemble(
        CaptureData capture,
        IReadOnlyList<MetricDefinition> catalog,
        ParsedSamples samples,
        IReadOnlyList<BinSummary> bins,
        IReadOnlyDictionary<int, int[]> rowsByBin,
        AnalysisOptions options)
    {
        var threshold = options.GpuThreshold;
        if (options.AutoGpuThreshold)
        {
            threshold = FilterProfileDetector.ComputeAutoGpuThreshold(samples);
        }

        var profile = FilterProfileDetector.Detect(
            bins,
            threshold,
            options.TrimBufferSeconds,
            options.ExcludeTransitions);

        var metadata = ExtractMetadata(
            capture,
            samples,
            threshold,
            options.TrimBufferSeconds,
            catalog.Count,
            profile.ValidBins.Count * AnalysisConstants.FpsBinSeconds);

        return new SessionAnalysis
        {
            Capture = capture,
            Catalog = catalog,
            Samples = samples,
            EffectiveOptions = options with { GpuThreshold = threshold },
            Bins = bins,
            RowsByBin = rowsByBin,
            Window = profile.Window,
            ValidBins = profile.ValidBins,
            Diagnostics = profile.Diagnostics,
            Metadata = metadata,
        };
    }

    /// <summary>
    /// Detected metadata from the capture's constant columns plus durations
    /// derived from the active window. Mirrors the Python reference.
    /// </summary>
    public static SessionMetadata? ExtractMetadata(
        CaptureData capture,
        ParsedSamples samples,
        double threshold,
        double trimBuffer,
        int metricCount,
        double? activeDurationSeconds = null)
    {
        if (capture.RowCount == 0 || samples.Count == 0)
        {
            return null;
        }

        double durationSeconds;
        if (activeDurationSeconds is not null)
        {
            durationSeconds = Math.Max(0.0, activeDurationSeconds.Value);
        }
        else
        {
            var active = FilterProfileDetector.InferActiveWindow(samples, threshold, trimBuffer);
            durationSeconds = active is not null ? Math.Max(0.0, active.End - active.Start) : 0.0;
        }

        var captureDuration = Math.Max(
            0.0,
            samples.TimeSeconds[^1] - samples.TimeSeconds[0]);

        return new SessionMetadata(
            Application: FirstRowString(capture, "Application") ?? "--",
            Resolution: FirstRowString(capture, "Resolution") ?? "--",
            Gpu: FirstRowString(capture, "GPU") ?? FirstRowString(capture, "GPU0") ?? "--",
            Cpu: FirstRowString(capture, "CPU") ?? "--",
            Runtime: FirstRowString(capture, "Runtime") ?? "--",
            Duration: DisplayText.FormatDuration(durationSeconds),
            CaptureDuration: DisplayText.FormatDuration(captureDuration),
            FrameCount: samples.Count,
            MetricCount: metricCount);
    }

    private static string? FirstRowString(CaptureData capture, string header)
    {
        var index = capture.IndexOfHeader(header);
        if (index < 0)
        {
            return null;
        }

        for (var row = 0; row < capture.RowCount; row++)
        {
            var value = capture.Cell(index, row).Trim();
            if (!CsvValues.IsNa(value))
            {
                return value;
            }
        }

        return null;
    }
}
