using BenchmarkDotNet.Attributes;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.Benchmarks;

[MemoryDiagnoser]
public class CsvParsingBenchmarks
{
    private readonly FrameViewCsvReader _reader = new();
    private string? _path;
    private string? _directory;

    [Params(25_000, 200_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void GlobalSetup() => (_path, _directory) = BenchmarkData.CreateCsvFile(Rows, seed: 42);

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        try
        {
            if (_directory is not null)
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    [Benchmark]
    public Task<CaptureData> LoadCaptureAsync() => _reader.LoadCaptureAsync(_path!);
}
