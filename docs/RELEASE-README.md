# Frame Performance Analyzer 3.2.3

**Release:** Precision Timeline maintenance update  
**Version:** 3.2.3  
**Windows package version:** 3.2.3.0

Frame Performance Analyzer 3.2.3 is a maintenance update for the
Precision Timeline release. It preserves the 3.2.0 feature set while
addressing issues discovered during post-release validation.

## Fixes in 3.2.3

- **Contextual benchmark browser scoping:** Pair Base, Pair Comparison, and
  Multi selectors now show only captures belonging to the currently active
  Source folder. The global Benchmark Library remains historical and is not
  cleared when the Source folder changes.
- **Precision Frame points:** enabling Frame points no longer reintroduces
  abnormal raw FPS spikes while Precision filtering is active. Raw mode still
  preserves the original frame-level values.
- **Benchmark capture duration:** FrameView capture duration is now calculated
  from the actual recorded timestamp span rather than treating the final
  absolute `TimeInSeconds` value as the capture length. NVIDIA App elapsed-time
  logs retain their existing semantics.

## Precision Timeline highlights

- **Precision filtering** is one explicit binary mode. Off keeps the complete
  raw capture. On applies automatic GPU gating, FPS outlier handling, fixed
  outer-edge trimming, and conservative multivariable loading/transition
  validation.
- The transition detector combines frame and presentation cadence with the
  available GPU, CPU, render-queue, latency, dropped-frame, and Frame
  Generation telemetry. An isolated real performance drop is not removed just
  because GPU utilization changed.
- **Analyzed time** is compressed. Excluded loading and transition regions no
  longer leave artificial empty gaps in the chart timeline.
- **Frame points** replace the one-second summary curve with true per-frame
  values. Visual decimation only controls drawing density; zoom reveals more of
  the original data and FPS KPIs use every analyzed frame.
- **Pair** continues to compare a Base and Comparison with role-aware colors,
  visible-range statistics, and direction-aware deltas.
- **Multi** compares 2–8 captures as equal peers with stable colors shared by
  charts, KPI rows, cursor values, selection, and exports.
- Pair, Comparison, Multi, and Library use one **unified benchmark browser**
  with search, filters, full-card selection, source-folder control, and
  protection against choosing the current Base as its own Comparison.
- Metadata **Benchmark name** values appear together with the detected capture
  name in the quick selector, so repeated captures remain distinguishable.
- The interface uses themed scrollbars, exact tick-aligned grid lines, a fixed
  cursor-value readout, and distinct **Reset view** and **Fit visible Y**
  actions.

## Supported input

- **NVIDIA FrameView detailed logs** (`*_Log.csv`) with per-frame timing and
  available hardware/presentation telemetry.
- **NVIDIA FrameView summary CSVs** (`FrameView_Summary.csv`), opened as a
  read-only table.
- **NVIDIA App performance logs** (`NVIDIA_App_Performance_Log_*.csv`) with
  sampled FPS and available GPU/CPU telemetry.
- **Frame Performance Analyzer portable analyzed-data exports** in CSV or JSON.
  Files produced by earlier application versions remain import-compatible.

NVIDIA App logs have lower temporal precision than detailed FrameView logs.
Frame Performance Analyzer does not synthesize frame-level precision absent
from the source file.

## Pair and Multi

Use **Pair** for a Base versus Comparison workflow. Use **Multi** when two to
eight runs should be treated as equal peers. Both modes support the same metric
selection, Precision filtering, visible-range behavior, frame-point timeline,
zoom/pan controls, cursor values, and export system.

## Precision Timeline controls

- **Precision filtering off:** recorded data remains raw; no loading,
  transition, FPS-outlier, GPU-gate, or edge-trim exclusions are applied.
- **Precision filtering on:** the complete automatic pipeline is applied.
- **Frame points off:** the chart uses the normal one-second summary.
- **Frame points on:** real per-frame values and frame-level FPS statistics
  replace the summary representation.
- **Reset view:** restores the complete analyzed timeline.
- **Fit visible Y:** keeps the current X interval and fits Y to its visible
  values.

## Library and metadata

The unified browser combines saved Library records with captures discovered in
the active source folder. It supports search, filters, sorting, availability,
recent Pair comparisons, 2–8 benchmark Multi selection, package import/export,
and non-destructive Library removal.

Metadata can record a custom benchmark name, game or scene, resolution,
graphics preset, upscaler, Frame Generation, Ray Tracing, driver, notes, and
tags. Metadata is stored by the application; source CSV files are never
modified.

## Exports

- Professional PNG reports for selected Pair or Multi benchmarks and metrics.
- Statistics CSV export.
- Benchmark JSON/package export.
- Portable analyzed-data CSV/JSON export and import.

Exports honor the current visible range where applicable. The portable format
version is unchanged by the product rebranding, preserving compatibility with
existing files.

## GitHub ZIP

1. Unzip `FramePerformanceAnalyzer-v3.2.3-win-x64.zip`.
2. Double-click `FramePerformanceAnalyzer.exe`.

The build is self-contained for Windows x64; a separate .NET runtime is not
required. A matching `.sha256` file is published for ZIP verification.

Windows SmartScreen may show an unknown-publisher warning while the SignPath
Foundation application and signing integration remain pending. Run a binary
only when it came from the official repository release.

## Microsoft Store package

The Store artifact is
`FramePerformanceAnalyzer-Store-3.2.3.0-x64.msix`. Microsoft Store signs the
package after certification. The physical filename and public DisplayName use
the new brand, while the Partner Center package identity intentionally remains
`Strecker.FrameViewAnalyzer`.

## Local data and compatibility

The public rebranding does not migrate or duplicate existing data:

| Data | Stable location |
|---|---|
| Settings, metadata, and Library | `%APPDATA%\FrameViewAnalyzer\V2\` |
| Diagnostic logs | `%LOCALAPPDATA%\FrameViewAnalyzer\logs\` |

The solution/projects (`FrameViewAnalyzer.*`), AppUserModelId
(`StreckerMX.FrameViewAnalyzer`), repository URL, portable format version, and
Store identity remain unchanged intentionally.

## Privacy and independence

Frame Performance Analyzer is local-first, contains no telemetry, and makes no
network calls during normal analysis. It is an independent project and is not
affiliated with or endorsed by NVIDIA Corporation.

See [`PRIVACY.md`](../PRIVACY.md),
[`CODE_SIGNING_POLICY.md`](../CODE_SIGNING_POLICY.md), and
[`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md) for details.
