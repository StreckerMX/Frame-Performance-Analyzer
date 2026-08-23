# FrameView Analyzer 2.2.0

**Version:** 2.2.0 (stable release)

FrameView Analyzer is a native Windows desktop application for analyzing NVIDIA
FrameView captures and NVIDIA App performance-overlay logs. Version 2.2.0
expands the original Base vs. Comparison workflow into a complete Pair / Multi
benchmark workspace while keeping Pair fast and familiar.

## What's new in 2.2.0

- Adds a **Pair | Multi** workspace switch. Pair keeps the familiar Base vs. Comparison workflow; Multi compares **2–8 captures as equal peers** with no reference benchmark.
- Adds a stable eight-color Multi palette used consistently by charts, KPI rows, the export selector, and exported PNG reports.
- Generalizes chart state and metric union from two sessions to N sessions.
- Makes the visible-range KPI strip follow the selected metric and current zoom range.
- Shows `AVERAGE`, `Max`, `Min`, and `VISIBLE TIME` for every metric, with `1% LOW` and `0.1% LOW` added only for FPS.
- Marks the winning Multi statistic with its signed percentage advantage over the runner-up, respecting whether higher or lower values are better for the metric.
- Enables **Analysis Range** in Multi. GPU threshold, edge trim, and loading-screen / FPS-culler exclusion are applied to every selected benchmark with transactional rollback if any re-analysis fails.
- Adds Library row checkboxes for selecting **2–8 benchmarks** and a `Compare selected` action that opens the same Multi workspace used by the folder selector.
- Adds non-destructive Library removal. Removing a record hides it persistently without deleting the source CSV and clears stale recent-comparison references.
- Replaces the old PNG All/Single selector with explicit **benchmark and metric checklists**. FPS is selected by default and up to eight metrics can be included.
- Adds an editable **REPORT TITLE** field before PNG export. Pair defaults to `BENCHMARK COMPARISON`; Multi defaults to `MULTI BENCHMARK COMPARISON`.
- Adds timestamped suggested PNG filenames so repeated exports do not reuse the same name. Multi filenames use a neutral `MULTI_BENCHMARK_COMPARISON_...` identity instead of borrowing the first benchmark name.
- Preserves the current chart time/zoom bounds when switching metrics.
- Retains NVIDIA App performance-log support introduced in 2.1.0, including sampled FPS and dynamic telemetry metrics.

## Pair and Multi workflows

### Pair

Use Pair for the normal one-vs-one workflow. Load a **Base** capture and,
optionally, a **Comparison** capture. KPI tiles show A/B values and the metric's
improvement or regression using the correct performance direction.

### Multi

Use Multi when comparing more than two configurations or when you want two
captures treated as equal peers instead of Base/Comparison roles. Select 2–8
captures from the chosen capture folder or directly from Benchmark Library.
Every selected benchmark receives a stable color and appears in each applicable
visible-range KPI tile.

## PNG report workflow

The PNG report dialog lets you:

1. Select exactly which loaded benchmarks to include.
2. Select FPS and any additional available metrics, up to eight charts.
3. Edit the report title before rendering.
4. Export a Pair or Multi report with the same benchmark identity and colors used in the application.

Multi reports use neutral `Benchmark:` header lines and do not present the first
capture's hardware/configuration as if it described every selected peer.

## System requirements

- **Windows 11 x64 recommended.**
- **Windows 10 x64** support is limited to editions/releases currently supported by .NET 10, such as applicable Enterprise/LTSC versions.
- **No .NET installation is required.** The release build is self-contained.
- No installer: unzip and run.

## How to launch

1. Unzip `FrameViewAnalyzer-v2.2.0-win-x64.zip`.
2. Double-click `FrameViewAnalyzer.exe`.

Windows SmartScreen may warn about an unknown publisher because the executable
is not code-signed. Choose **More info > Run anyway** only if you trust the
source of the download.

## Code signing policy

FrameView Analyzer is applying for the SignPath Foundation Open Source Code Signing program. If approved, official releases will use **Free code signing provided by SignPath.io, certificate by SignPath Foundation**.

The complete signing scope, maintainer roles, privacy statement, build provenance, and verification process are documented in [`CODE_SIGNING_POLICY.md`](../CODE_SIGNING_POLICY.md).

## Supported files

- **FrameView Log CSVs** (`*_Log.csv`) - analyzed from per-frame timing data and available in Pair or Multi.
- **NVIDIA App Performance Logs** (`NVIDIA_App_Performance_Log_*.csv`) - analyzed as low-rate sampled telemetry and available in Pair or Multi.
- **FrameView Summary CSVs** (`FrameView_Summary.csv`) - opened as a read-only table and never occupy analysis slots.

NVIDIA App performance logs have lower temporal precision than FrameView logs.
Their FPS values are sampled telemetry rather than frame-by-frame timing, so
FrameView Analyzer does not synthesize precision absent from the source file.

## Benchmark Library

The Library supports search, filters, sorting, recent Pair comparisons, direct
`Base` / `Comparison` loading, Multi row selection, and non-destructive record
removal. Removing a Library record never deletes its original CSV file.

## Where data is stored

| What | Location |
|---|---|
| Settings, metadata, library | `%APPDATA%\FrameViewAnalyzer\V2\` |
| Log files | `%LOCALAPPDATA%\FrameViewAnalyzer\logs\` |

The portable ZIP does **not** store settings beside the executable. Logs are
kept locally, roll daily, and are retained for 7 days. The application has
**no telemetry** and makes no network calls.

## Validation

2.2.0 was validated with the Windows/.NET 10 automated test suite and Release
build, plus manual UI testing of Pair, Multi, Multi Analysis Range, Library
multi-selection/removal, FrameView and NVIDIA App metrics, editable PNG report
titles, benchmark/metric report selection, and exported Multi charts.

Each GitHub release includes the self-contained Windows x64 ZIP and a matching
`.sha256` checksum file.

## How to uninstall

1. Delete the extracted application folder and downloaded ZIP.
2. Optionally delete the data folders listed above to remove local settings, metadata, Library records, and logs.

## Known deviations

- No Help / Learn-more card yet.
- No single-series fill-under-curve shading (cosmetic only).
- Dynamic metric IDs use FNV-1a for display keys and are not persisted as user data.
- UI is English-only; string centralization remains a future refactor.

Time-range drag selection remains available in Pair: turn **Drag pan** off and
drag horizontally on the chart (minimum span 1 second).

See `README.md` for screenshots and the repository `docs/` folder for the
architecture, parity, and performance documentation.
