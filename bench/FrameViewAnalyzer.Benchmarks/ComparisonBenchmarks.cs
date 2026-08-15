using BenchmarkDotNet.Attributes;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Comparison;

namespace FrameViewAnalyzer.Benchmarks;

[MemoryDiagnoser]
public class ComparisonBenchmarks
{
    private SessionAnalysis? _base;
    private SessionAnalysis? _comparison;

    [Params(25_000, 200_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var service = new CaptureAnalysisService();
        _base = service.Analyze(BenchmarkData.CreateCapture(Rows, seed: 42));
        _comparison = service.Analyze(BenchmarkData.CreateCapture(Rows, seed: 7));
    }

    [Benchmark]
    public IReadOnlyList<ComparisonRow> Compare() =>
        new ComparisonService().Compare(_base!, _comparison);
}
