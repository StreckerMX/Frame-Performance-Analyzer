# Phase 12 — corrected baseline snapshot

This directory preserves the **official corrected Phase 12 baseline**, captured
2026-08-15 after the synthetic-fixture unit bug (frametimes in seconds instead
of milliseconds) was fixed.

- Production code: **pre-optimization** (optimization changes stashed) — the
  Release assemblies used by this run contain no optimization markers
  (`MetricColumns` / `FrametimeKeyIndices` absent).
- Fixture: corrected — frametime 9–11 ms, 200k frames ≈ 1,999.97 s, typical
  ~100 FPS, deterministic seed 42.
- Machine: Windows 11 (10.0.26200), AMD Ryzen 7 5700X3D, .NET 10.0.11,
  BenchmarkDotNet 0.15.8, Release, DefaultJob, MemoryDiagnoser.
- Raw BenchmarkDotNet artifacts are NOT committed; see
  `../PERFORMANCE.md` for the full before/after analysis.

The original contaminated measurements (~208 ms / ~227 MB Analyze@200k,
~125 ms / ~151 MB Compare@200k) were discarded and must never be used for
percentage calculations.
