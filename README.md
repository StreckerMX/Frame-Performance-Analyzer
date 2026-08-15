# FrameView Analyzer (WPF)

Native C#/.NET/WPF rewrite of **FrameView Analyzer** — a Windows desktop tool
that analyzes NVIDIA FrameView capture CSVs (frame times, GPU/CPU telemetry,
latency) with an A/B comparison workflow.

The original Python application (`StreckerMX/frameview-analyzer`) is the
functional reference for this rewrite; this repository is a clean redesign
and does not port implementation details blindly.

## Status

**Phase 0 — solution skeleton.** Architecture approved; repository created.
No functionality implemented yet.

## Stack

- C# on **.NET 10 (LTS)**
- **WPF** + **CommunityToolkit.Mvvm** (MVVM)
- **ScottPlot 5** for charting
- **System.Text.Json**, **CsvHelper**, **xUnit**, **Serilog**, **BenchmarkDotNet** (later)

## Solution structure

```text
src/
  FrameViewAnalyzer.Core/            domain models + pure math (no dependencies)
  FrameViewAnalyzer.Analytics/       analysis engine (→ Core)
  FrameViewAnalyzer.Infrastructure/  CSV IO, stores, exports (→ Core, Analytics)
  FrameViewAnalyzer.App/             WPF composition root + Views/ViewModels
tests/
  FrameViewAnalyzer.Core.Tests/
  FrameViewAnalyzer.Analytics.Tests/
  FrameViewAnalyzer.Infrastructure.Tests/
  FrameViewAnalyzer.App.Tests/
bench/
  FrameViewAnalyzer.Benchmarks/      BenchmarkDotNet scenarios (Phase 12)
```

See `docs/ARCHITECTURE.md` for the full architecture proposal and migration
roadmap.

## Build & test

```powershell
dotnet build
dotnet test
```
