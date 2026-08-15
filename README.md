# FrameView Analyzer (WPF)

Native C#/.NET/WPF rewrite of **FrameView Analyzer** — a Windows desktop tool
that analyzes NVIDIA FrameView capture CSVs (frame times, GPU/CPU telemetry,
latency) with a Base/Comparison workflow.

The original Python application (`StreckerMX/frameview-analyzer`) is the
functional reference; behavior is verified against golden fixtures and ported
test vectors where algorithms must match exactly.

## Status

**Phase 12 of 15 complete — performance baselines and optimization.**

Implemented through Phase 12:

- **Phase 0** — solution skeleton, project layout, strict gates
- **Phase 1** — FrameView CSV loader + domain models (encodings, missing
  values, capture naming, display names)
- **Phase 2** — analytics engine (GPU filtering, loading-screen detection,
  FPS outliers, harmonic FPS bins, 1% / 0.1% lows, min/max)
- **Phase 3** — analytics test parity with Python (golden fixtures)
- **Phase 4** — WPF shell, dark/light themes, window placement, settings
- **Phase 5** — single-session ScottPlot chart with LTTB/MinMax decimation
- **Phase 6** — visible-range statistics, wheel zoom, drag pan, tooltip,
  Reset/Auto zoom
- **Phase 7** — Base/Comparison sessions, session cards, KPI tiles with
  deltas, slot promotion when the Base is removed
- **Phase 8** — Analyze menu: Full capture, Worst performance region (10 s),
  Most stable region (10 s), Largest performance drop, Largest A/B difference
- **Phase 9** — manual benchmark metadata, stable capture identity, detected
  metadata prefill, v2 metadata persistence, Base/Comparison metadata editor
  integration
- **Phase 10** — Benchmark Library (search / filter / sort, Base/Comparison
  loading from the library, A/B selection, recent comparisons, missing-source
  handling, statistics digest) + the legacy one-way importer from the Python
  application
- **Phase 11** — exports: multi-chart PNG report with a compact context
  header, Statistics CSV (invariant, UTF-8 BOM), Benchmark data JSON, and
  portable benchmark package export/import with statistics hydration
- **Phase 12** — BenchmarkDotNet suite (CSV parsing, analytics, comparison,
  decimation) with corrected baselines and measured optimizations: −55% / −99%
  allocations on Compare, −40% allocations on Analyze
  (see `docs/PERFORMANCE.md`)

Remaining future work: **final parity audit** and the **release candidate**
are **not implemented yet**.

## Stack

- C# on **.NET 10**
- **WPF** + **CommunityToolkit.Mvvm** (MVVM)
- **ScottPlot 5** for charting
- **CsvHelper** for CSV input, **System.Text.Json** for settings
- **xUnit** for tests

## Solution structure

```text
src/
  FrameViewAnalyzer.Core/            domain models + pure math (no dependencies)
  FrameViewAnalyzer.Analytics/       analysis engine (→ Core)
  FrameViewAnalyzer.Infrastructure/  CSV IO, stores (→ Core, Analytics)
  FrameViewAnalyzer.App/             WPF composition root + Views/ViewModels
tests/
  FrameViewAnalyzer.Core.Tests/
  FrameViewAnalyzer.Analytics.Tests/
  FrameViewAnalyzer.Infrastructure.Tests/
  FrameViewAnalyzer.App.Tests/
```

See `docs/ARCHITECTURE.md` for the architecture proposal and roadmap.

## Features

- Load NVIDIA FrameView `*_Log.csv` captures (UTF-8 with legacy-encoding
  fallback, decimal-comma tolerant)
- Analysis engine: automatic GPU threshold, edge trimming, transition
  exclusion, harmonic FPS, percentiles
- Chart: any metric, decimation for large captures, wheel zoom (cursor
  anchored), drag pan, hover tooltip with nearest real samples, Reset/Auto
  zoom
- Visible-range KPI strip: average FPS, 1% low, 0.1% low, max, min, visible
  time — recomputed for the zoomed range
- Base/Comparison: load two captures, combined session cards with the FPS
  delta, overlaid series with legend, per-tile comparison values and
  direction-aware deltas, comparison promotion when the Base is removed
- Analyze menu: jump the chart to the full capture, the worst 10-second
  performance region, the most stable 10-second region, the largest
  performance drop, or the region of largest A/B divergence
- Manual benchmark metadata: benchmark name, game/scene, resolution, graphics
  preset, upscaler (+ quality), Frame Generation, Ray Tracing, driver
  version, notes, and tags — persisted per capture identity with detected
  prefill in the metadata editor
- Benchmark Library: persistent index over the capture folder with search,
  game/resolution/GPU/tag filters, date/name sorting, availability tracking
  (missing sources stay listed), Load as Base / Load as Comparison, A/B
  selection, recent comparisons, and an FPS statistics digest per record
- Legacy import: a one-way, user-triggered importer reads the Python
  application's stores — `%APPDATA%\FrameViewAnalyzer\settings.json`,
  `metadata.json`, and `library.json` — into the separate v2 stores without
  modifying the Python files; existing V2 data always wins and repeated
  imports are idempotent
- Exports: multi-chart PNG report (compact context header, all or selected
  session), Statistics CSV (invariant numbers, UTF-8 BOM), Benchmark data
  JSON (sessions + statistics + manual metadata), and portable benchmark
  package export/import (statistics digest hydration, validation, atomic
  writes)

## Tests

349/349 tests pass on the Release build with 0 errors and 0 warnings.

```powershell
dotnet restore FrameViewAnalyzer.sln
dotnet build FrameViewAnalyzer.sln --configuration Release
dotnet test FrameViewAnalyzer.sln --configuration Release
```

Run the app:

```powershell
dotnet run --project src/FrameViewAnalyzer.App --configuration Debug
```

## Layout notes

The window enforces a minimum size (`980 × 700` logical units) so the session
cards, six KPI tiles, toolbar, and Analyze menu stay reachable; WPF is
per-monitor DPI aware. At minimum width, KPI values use a smaller display
size and ellipsize rather than push the layout.
