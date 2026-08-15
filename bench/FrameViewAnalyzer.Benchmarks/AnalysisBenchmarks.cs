using BenchmarkDotNet.Attributes;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Bins;
using FrameViewAnalyzer.Analytics.Filtering;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Benchmarks;

[MemoryDiagnoser]
public class AnalysisBenchmarks
{
    private CaptureData? _capture;
    private AnalysisOptions? _options;
    private double[]? _fpsValues;
    private MetricDefinition? _fpsMetric;
    private IReadOnlyList<BinSummary>? _bins;
    private double _gpuThreshold;

    [Params(25_000, 200_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _capture = BenchmarkData.CreateCapture(Rows, seed: 42);
        var session = new CaptureAnalysisService().Analyze(_capture);
        _options = session.EffectiveOptions;
        _fpsValues = session.Samples.Fps;
        _fpsMetric = CoreMetricCatalog.CoreById["fps"];
        _bins = session.Bins;
        _gpuThreshold = session.EffectiveOptions.GpuThreshold;
    }

    /// <summary>Full pipeline: samples → bins → filter profile → assembled session.</summary>
    [Benchmark]
    public SessionAnalysis Analyze() =>
        new CaptureAnalysisService().Analyze(_capture!, _options);

    [Benchmark]
    public MetricStatistics ComputeStatistics() =>
        StatisticsCalculator.Compute(_fpsMetric!, _fpsValues!);

    [Benchmark]
    public FilterProfile DetectGpuProfile() =>
        FilterProfileDetector.Detect(_bins!, _gpuThreshold, trimBufferSeconds: 1.0, excludeTransitions: true);
}
