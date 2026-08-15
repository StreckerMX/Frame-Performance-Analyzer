# Performance — Phase 12 baselines and optimization

> Corrected, reproducible measurements. The initial Phase 12 benchmark fixture
> had a unit bug (synthetic frametimes of ~9–11 **seconds** instead of
> milliseconds), which produced ~2,000,000 s captures with one bin per row and
> contaminated every first measurement (e.g. Analyze@200k ~208 ms / ~227 MB).
> Those numbers were **discarded**. Everything below was re-measured with the
> corrected fixture.

## Environment

| Item | Value |
|---|---|
| OS | Windows 11 (10.0.26200.9168/25H2/2025Update) |
| CPU | AMD Ryzen 7 5700X3D, 1 CPU, 16 logical / 8 physical cores |
| .NET SDK / runtime | 10.0.400 / .NET 10.0.11, X64 RyuJIT x86-64-v3 |
| BenchmarkDotNet | 0.15.8, DefaultJob, MemoryDiagnoser, Release |
| Baseline snapshot | `docs/performance-data/phase12-baseline.csv` |

## Workload definitions (deterministic, seed 42 / 7 for the comparison session)

- **CSV parsing** (`CsvParsingBenchmarks`): synthetic FrameView-style CSV
  (`TimeInSeconds,MsBetweenPresents,GPU0Util(%)`, UTF-8 BOM), 25k / 200k rows.
- **Analysis** (`AnalysisBenchmarks`): in-memory `CaptureData` built by the
  same generator — full `Analyze` pipeline, per-metric `ComputeStatistics`
  (FPS values), and `DetectGpuProfile` (bin-level GPU filter detection).
- **Comparison** (`ComparisonBenchmarks`): `ComparisonService.Compare` over two
  sessions from seeds 42 and 7.
- **Decimation** (`DecimationBenchmarks`): ascending x + noisy y series,
  250k / 1M points, budgets 1000 / 4000 (regression guard only — never
  optimized).

Corrected fixture: frametime 9–11 ms (~100 FPS typical), 200k frames span
**1,999.97 s** (~2,000 s, not millions), timestamps strictly ascending. The
fixture is locked by `BenchmarkDataFixtureTests`.

## Results

### Corrected baseline (pre-optimization production code)

| Benchmark | Size | Mean | Allocated |
|---|---:|---:|---:|
| LoadCaptureAsync | 25k | 5.143 ms | 4.97 MB |
| LoadCaptureAsync | 200k | 65.456 ms | 38.26 MB |
| Analyze | 25k | 6.883 ms | 4,812 KB |
| Analyze | 200k | 55.611 ms | 38,601 KB |
| ComputeStatistics | 25k | 2.304 ms | 686 KB |
| ComputeStatistics | 200k | 24.573 ms | 5,472 KB |
| DetectGpuProfile | 25k | 13.91 µs | 40 KB |
| DetectGpuProfile | 200k | 185.76 µs | 326 KB |
| Compare | 25k | 12.97 ms | 13.45 MB |
| Compare | 200k | 104.11 ms | 107.81 MB |

### Optimized

| Benchmark | Size | Mean | Allocated |
|---|---:|---:|---:|
| LoadCaptureAsync | 25k | 5.159 ms | 4.97 MB |
| LoadCaptureAsync | 200k | 64.502 ms | 38.26 MB |
| Analyze | 25k | 5.830 ms | 2,896 KB |
| Analyze | 200k | 50.992 ms | 23,292 KB |
| ComputeStatistics | 25k | 2.644 ms* | 686 KB |
| ComputeStatistics | 200k | 24.951 ms | 5,472 KB |
| DetectGpuProfile | 25k | 14.59 µs | 40 KB |
| DetectGpuProfile | 200k | 251.43 µs* | 326 KB |
| Compare | 25k | 5.838 ms | 98.39 KB |
| Compare | 200k | 47.759 ms | 651.29 KB |

\* Repeatability re-run: ComputeStatistics@25k → 2.293 ms,
DetectGpuProfile@200k → 182.4 µs — the elevated values in the full run were
run-to-run noise; code was not changed for either benchmark.

### Percentage changes (corrected baseline → optimized)

| Benchmark | Size | Mean Δ | Alloc Δ |
|---|---:|---:|---:|
| LoadCaptureAsync | 25k | +0.3 % | 0 % |
| LoadCaptureAsync | 200k | −1.5 % | 0 % |
| Analyze | 25k | **−15.3 %** | **−39.8 %** |
| Analyze | 200k | **−8.3 %** | **−39.7 %** |
| Compare | 25k | **−55.0 %** | **−99.3 %** |
| Compare | 200k | **−54.1 %** | **−99.4 %** |

### Decimation regression guard (1M points)

| Method | Budget | Baseline | Optimized | Repeat run | Δ |
|---|---:|---:|---:|---:|---:|
| MinMaxEnvelope | 1000 | 1,046.2 µs | 923.3 µs | 977.5 µs | noise band |
| MinMaxEnvelope | 4000 | 1,117.9 µs | 1,099.2 µs | 1,129.2 µs | ~±1–2 % |
| Lttb | 1000 | 1,352.9 µs | 1,459.4 µs | 1,391.6 µs | noise band |
| Lttb | 4000 | 1,665.9 µs | 1,610.5 µs | 1,670.0 µs | ~±3 % |
| Select | 1000 | 1,062.9 µs | 1,043.7 µs | 949.5 µs | noise band |
| Select | 4000 | 1,128.8 µs | 1,097.9 µs | 1,144.1 µs | ~±1–2 % |

Decimation was never modified; the repeat run shows all deltas are within
run-to-run noise. No repeatable regression ≥ 5 %.

## Optimizations kept

1. **`CsvValues.TryParseAnyNumber`** (Core) — tries the invariant numeric
   parse first and only runs the `Trim().ToLowerInvariant()` inf/nan token
   switch for non-numeric cells, eliminating per-cell string allocations in
   the sample-parsing hot loop. Semantics identical (locked by
   `CsvValuesTests`).
2. **`MetricValueResolver.MetricColumns` + `SeriesBuilder`** (Core/Analytics) —
   column indices are resolved once per metric instead of rebuilding a header
   `HashSet` and scanning headers **per row**. This is the dominant Compare
   win (−99 % allocated). `SeriesBuilder.Values` returns Y-only series for
   the comparison/statistics path; per-bin scratch buffer is reused.
   Parity locked by `MetricValueResolverTests` and
   `SeriesBuilderTests.Values_matches_Build_y_for_every_metric`.
3. **`ParsedSampleBuilder`** (Analytics) — capacity-aware collections
   (`capture.RowCount`) and a fast path that skips the stable sort when
   timestamps are already ascending (the identity sort; FrameView logs are
   append-ordered). Unordered input keeps the exact LINQ stable sort.
   Locked by `ParsedSampleBuilderTests` (ascending, unordered-with-ties,
   missing time, empty).

## Optimizations rejected

None. All three changes met the acceptance rule (≥ 20 % allocation reduction
on the 200k hot paths, or a clearly repeatable improvement in both
dimensions) with no material regression anywhere else.

## Regressions investigated

- Full optimized run showed `ComputeStatistics@25k` +14.8 %,
  `DetectGpuProfile@200k` +35 % and `Lttb@1M/1000` +7.9 % — all in **untouched
  code**. A dedicated repeat run measured 2.293 ms / 182.4 µs / 1,391.6 µs,
  i.e. within noise of the baseline. Not real regressions.

## Reproducing

```powershell
dotnet build FrameViewAnalyzer.sln -c Release
dotnet run -c Release --project bench/FrameViewAnalyzer.Benchmarks `
  -- --filter *
```

Do not commit `BenchmarkDotNet.Artifacts/` (gitignored). The corrected
baseline snapshot lives in `docs/performance-data/phase12-baseline.csv`.
