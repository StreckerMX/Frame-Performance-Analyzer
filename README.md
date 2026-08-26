<div align="center">
  <img src="docs/assets/readme-hero.svg" width="100%" alt="FrameView Analyzer animated hero" />
</div>

# FrameView Analyzer

A native Windows desktop application for analyzing and comparing **NVIDIA FrameView** captures and **NVIDIA App performance-overlay logs**.

[![Release](https://img.shields.io/github/v/release/StreckerMX/FrameView-Analyzer?label=release)](https://github.com/StreckerMX/FrameView-Analyzer/releases/latest)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4)](https://github.com/StreckerMX/FrameView-Analyzer/releases/latest)
[![License](https://img.shields.io/github/license/StreckerMX/FrameView-Analyzer)](LICENSE)

FrameView Analyzer turns NVIDIA performance CSV captures into an interactive benchmark workspace. Inspect frame-rate and telemetry data over time, compare two runs with the familiar **Pair** workflow, compare **2–8 benchmarks as equal peers** in **Multi**, isolate noisy or loading-screen regions, organize captures in a local Library, and export presentation-ready reports and portable analyzed data.

## Screenshots

### Pair comparison

<p align="center">
  <img src="docs/screenshots/analysis-dark.png" alt="FrameView Analyzer Pair comparison in dark theme" width="100%">
</p>

<table>
  <tr>
    <td width="50%" align="center">
      <strong>Light theme</strong><br><br>
      <img src="docs/screenshots/analysis-light.png" alt="FrameView Analyzer Pair comparison in light theme">
    </td>
    <td width="50%" align="center">
      <strong>Multi benchmark workspace</strong><br><br>
      <img src="docs/screenshots/multi-workspace.png" alt="FrameView Analyzer Multi benchmark workspace">
    </td>
  </tr>
</table>

### Library and export workflow

<table>
  <tr>
    <td width="50%" align="center">
      <strong>Benchmark Library</strong><br><br>
      <img src="docs/screenshots/benchmark-library.png" alt="FrameView Analyzer Benchmark Library with multi-selection">
    </td>
    <td width="50%" align="center">
      <strong>PNG report selection</strong><br><br>
      <img src="docs/screenshots/export-dialog.png" alt="FrameView Analyzer PNG report export dialog">
    </td>
  </tr>
</table>

<details>
<summary><strong>Exported multi-benchmark report</strong></summary>
<br>
<p align="center">
  <img src="docs/screenshots/export-report.png" alt="FrameView Analyzer exported multi-benchmark PNG report" width="55%">
</p>
</details>

## Download

Download the latest stable build from **[GitHub Releases](https://github.com/StreckerMX/FrameView-Analyzer/releases/latest)**.

For the current stable release:

1. Download `FrameViewAnalyzer-v3.1.4-win-x64.zip`.
2. Extract the archive.
3. Run `FrameViewAnalyzer.exe`.

The application is distributed as a **self-contained Windows x64 build**, so installing the .NET runtime separately is not required.

> Windows SmartScreen may show an unknown-publisher warning because the executable is not code-signed.

## Code signing policy

FrameView Analyzer is applying for the SignPath Foundation Open Source Code Signing program. If approved, official release binaries will use **Free code signing provided by SignPath.io, certificate by SignPath Foundation**.

The current application has no telemetry and makes no network calls during normal operation. Signing scope, project roles, privacy guarantees, build provenance, and verification details are documented in the **[Code signing policy](CODE_SIGNING_POLICY.md)**.

## Highlights in 3.1.4

- **Adaptive FPS scaling.** FPS charts no longer force a zero baseline. The vertical axis adapts to the visible data while keeping a minimum span so small differences remain readable without becoming visually exaggerated.
- **Persistent visible time range.** A manually selected or zoomed time window stays active when switching metrics in the same workspace, while the Y-axis is recalculated for the newly selected metric.
- **Range-aware exports.** PNG reports plus analyzed CSV and JSON exports use the current visible time range, so exported results match the section being inspected on screen.
- **Portable analyzed-data import.** CSV/JSON analyzed-data exports can be imported back into FrameView Analyzer, restoring chart-ready metric series without requiring the original raw capture rows.
- **Clean export round trips.** Imported benchmark names preserve their original labels instead of accumulating Pair role prefixes after repeated export/import cycles.
- **Correct comparison arrows.** Pair KPI arrows now reflect the actual value movement. Lower-is-better improvements such as frame time or latency use a green down arrow instead of a green up arrow.
- **Pair and Multi parity.** Adaptive chart behavior, visible-range handling, export scoping, and comparison presentation are covered across both Pair and Multi workflows.

## Features

- **Interactive performance charts** with metric switching, hover inspection, cursor-anchored zoom, drag pan, range selection, automatic zoom, and adaptive FPS Y-axis scaling.
- **Pair comparison** with Base / Comparison KPI deltas, direction-aware arrows, and quick loading from the Benchmark Library.
- **Multi comparison** for 2–8 equal peers with stable colors, shared metric selection, per-benchmark KPI rows, and N-series chart overlays.
- **Visible-range statistics** that recalculate from the current chart window and preserve the visible time range when switching metrics.
- **Automatic analysis tools** for full capture, worst-performance region, most stable region, largest performance drop, and Pair A/B difference analysis.
- **Capture filtering** with GPU-active range detection, edge trimming, transition/loading-screen exclusion, and source-aware FPS outlier handling.
- **NVIDIA App performance-log support** with sampled FPS, NVIDIA-provided 1% Low, GPU/CPU utilization, latency, clocks, temperatures, power, voltage, fan telemetry, and other numeric metrics when present.
- **Benchmark metadata** for game, scene, resolution, graphics preset, upscaler, Frame Generation, Ray Tracing, driver version, notes, and tags.
- **Benchmark Library** with search, filters, sorting, availability tracking, recent Pair comparisons, direct Base/Comparison loading, Multi checkboxes, and non-destructive removal.
- **PNG report export** with benchmark and metric checklists, editable report title, Pair/Multi-aware headers, stable Multi colors, current-range rendering, and timestamped suggested filenames.
- **Portable analyzed-data export/import** for current-range CSV and JSON snapshots that can be reopened later in FrameView Analyzer.
- **Dark and light themes** with native Windows title-bar integration and a responsive full-width dashboard.

## Supported input

FrameView Analyzer supports:

- **NVIDIA FrameView detailed logs**, including standard `*_Log.csv` session files. FrameView FPS is calculated from per-frame timing data.
- **NVIDIA FrameView summary CSVs**, opened as a read-only table.
- **NVIDIA App performance-overlay logs**, including `NVIDIA_App_Performance_Log_*.csv`. These are low-rate telemetry samples rather than per-frame captures, so FPS is aggregated from NVIDIA's sampled FPS values and the exported `FPS 1(%) Low` column is exposed as its own metric.
- **FrameView Analyzer portable analyzed-data exports**, allowing previously exported CSV/JSON analysis snapshots to be imported without the original raw capture.

CSV loading includes tolerant handling for common encoding and numeric-format variations found in real-world captures.

## Typical workflow

1. Select a capture folder or open a supported CSV, or import a FrameView Analyzer analyzed-data export.
2. Stay in **Pair** to load a Base run and, optionally, a Comparison run.
3. Or switch to **Multi** and select 2–8 captures from the folder or Benchmark Library.
4. Choose a metric, inspect the chart, zoom into a time range, and review the visible-range KPIs.
5. Switch metrics without losing the selected time window when investigating the same event across FPS, frame time, latency, utilization, or other telemetry.
6. Adjust **Analysis Range** when GPU activity, edge trimming, or loading-screen exclusion needs refinement.
7. Add metadata when you want clearer benchmark names, configuration context, notes, and tags in the Library.
8. Export a PNG report or portable CSV/JSON analyzed-data snapshot. Exports use the current visible time range.

## Requirements

- **Windows 11 x64 recommended**
- Self-contained release build: **no separate .NET installation required**
- NVIDIA FrameView or NVIDIA App performance CSV captures

## Building from source

The project targets **.NET 10** and uses WPF.

```powershell
git clone https://github.com/StreckerMX/FrameView-Analyzer.git
cd FrameView-Analyzer

dotnet restore FrameViewAnalyzer.sln
dotnet build FrameViewAnalyzer.sln --configuration Release
dotnet test FrameViewAnalyzer.sln --configuration Release
```

Run the application from source:

```powershell
dotnet run --project src/FrameViewAnalyzer.App --configuration Debug
```

## Technology

- C# / .NET 10
- WPF
- CommunityToolkit.Mvvm
- ScottPlot 5
- CsvHelper
- System.Text.Json
- Serilog
- xUnit

## Project structure

```text
src/
  FrameViewAnalyzer.Core/            Domain models and pure math
  FrameViewAnalyzer.Analytics/       Performance analysis engine
  FrameViewAnalyzer.Infrastructure/  CSV I/O and persistent stores
  FrameViewAnalyzer.App/             WPF application and presentation layer

tests/
  FrameViewAnalyzer.Core.Tests/
  FrameViewAnalyzer.Analytics.Tests/
  FrameViewAnalyzer.Infrastructure.Tests/
  FrameViewAnalyzer.App.Tests/
```

More technical documentation is available in [`docs/`](docs/), including architecture, parity, performance, and release documentation.

## Verification

Version **3.1.4** is covered by the Windows/.NET 10 automated test suite and Release build, plus manual validation of Pair, Multi, adaptive FPS scaling, visible-range persistence, range-aware PNG/CSV/JSON exports, portable analyzed-data import, comparison-arrow direction, Library multi-selection, and FrameView/NVIDIA App metrics.

Each GitHub release also includes a `.sha256` file for verifying the downloadable ZIP.

## License

FrameView Analyzer is released under the [MIT License](LICENSE).

## Acknowledgements

FrameView Analyzer uses NVIDIA FrameView and NVIDIA App performance-export data together with open-source libraries including ScottPlot, CsvHelper, CommunityToolkit.Mvvm, and Serilog.

This project is independent and is not affiliated with or endorsed by NVIDIA Corporation.
