using BenchmarkDotNet.Attributes;
using FrameViewAnalyzer.Core.Charting;

namespace FrameViewAnalyzer.Benchmarks;

[MemoryDiagnoser]
public class DecimationBenchmarks
{
    private double[]? _xs;
    private double[]? _ys;

    [Params(250_000, 1_000_000)]
    public int Points { get; set; }

    [Params(1_000, 4_000)]
    public int Budget { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _xs = BenchmarkData.AscendingXs(Points);
        _ys = BenchmarkData.NoisySeries(
            new Random(42),
            Points,
            baseline: 100.0,
            noise: 12.0,
            spikeChance: 0.002);
    }

    [Benchmark]
    public (double[] Xs, double[] Ys) MinMaxEnvelope() =>
        Decimation.MinMaxEnvelope(_xs!, _ys!, Math.Max(1, Budget / 2));

    [Benchmark]
    public (double[] Xs, double[] Ys) Lttb() =>
        Decimation.Lttb(_xs!, _ys!, Budget);

    [Benchmark]
    public (double[] Xs, double[] Ys) Select() =>
        Decimation.Select(_xs!, _ys!, Budget);
}
