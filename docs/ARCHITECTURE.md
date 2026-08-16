# FrameView Analyzer v2 — C#/.NET/WPF Architecture

> Status: **APPROVED** (2026-08-14). The Python application
> (`StreckerMX/frameview-analyzer`) is the behavioral reference. This
> repository is a clean rewrite; implementation details are not ported
> blindly. Neither repository modifies the other.

## 1. Technology stack

| Concern | Choice |
|---|---|
| Runtime/SDK | .NET 10 (LTS) — .NET 8 only if a blocking tooling issue is discovered and reported |
| UI | WPF (`net10.0-windows`) |
| MVVM | CommunityToolkit.Mvvm 8.x |
| DI | Microsoft.Extensions.DependencyInjection (light composition root) |
| JSON | System.Text.Json with source-generated contexts |
| CSV | CsvHelper (read hot path + statistics write) |
| Charting | ScottPlot 5 |
| Tests | xUnit only (no FluentAssertions for now) |
| Benchmarks | BenchmarkDotNet (Phase 12) |
| Logging | Serilog → `%LOCALAPPDATA%\FrameViewAnalyzer\logs\` |
| Packaging | Windows self-contained single-file, distributed as ZIP; MSIX deferred |

## 2. Solution structure

```text
FrameViewAnalyzer.sln
src/
  FrameViewAnalyzer.Core/            BCL only — models, metric catalog rules, pure math
  FrameViewAnalyzer.Analytics/       → Core — analysis engine, no IO/UI
  FrameViewAnalyzer.Infrastructure/  → Core, Analytics — CSV, stores, exports
  FrameViewAnalyzer.App/             → all — WPF composition root, Views, ViewModels
tests/
  FrameViewAnalyzer.Core.Tests/
  FrameViewAnalyzer.Analytics.Tests/
  FrameViewAnalyzer.Infrastructure.Tests/
  FrameViewAnalyzer.App.Tests/       (net10.0-windows, ViewModel tests)
bench/
  FrameViewAnalyzer.Benchmarks/
```

Dependency rule: nothing above may depend on UI; `Core` has zero external
dependencies. Persistence is behind store interfaces so SQLite could replace
JSON later without architectural change.

## 3. Domain model (summary)

- Immutable records for all analytics: `AnalysisOptions`, `BinSummary`,
  `ActiveWindow`, `FilterDiagnostics`, `MetricStatistics`, `VisibleRange`,
  `ComparisonRow`, `BenchmarkSummary`.
- `CaptureData`: struct-of-arrays (`double[] TimeSeconds/FrametimeMs/
  GpuUtilPercent` + per-metric `double[]` columns). No dict-of-dicts.
- `ManualMetadata`: immutable Core domain record (fields + tags + config
  line); the editor binds through the mutable `MetadataEditorViewModel`
  `ObservableObject` instead.
- Separate persistence DTOs (`SettingsDocument`, `MetadataStoreDocument`,
  `LibraryStoreDocument`, `BenchmarkPackageDocument`) with tolerant converters.
- `MetricDirection` enum drives tail selection (1% Low vs 1% High) and
  improvement semantics exactly as the Python reference.

## 4. Services

- `IFrameViewCsvReader` — streaming parse, kind detection, encodings,
  `CaptureInfo` light scan.
- `ICaptureAnalysisService` — `Analyze` / `Reanalyze` / auto-threshold.
- `IComparisonService`, `IRangeAnalysisService` — pure orchestration.
- `ISettingsStore`, `IManualMetadataStore`, `ILibraryStore` — v2 JSON stores.
- `ILegacyDataImporter` — one-way, read-only import from the Python app's
  `%APPDATA%\FrameViewAnalyzer\settings.json|metadata.json|library.json`.
- `IBenchmarkLibraryService`, `IBenchmarkPackageService`, `IExportService`.
- App-level: `IDialogService`, `IThemeService`, `IWindowPlacementService`.

## 5. Chart architecture (ScottPlot 5)

Per-metric plot-type selection, not one-Scatter-fits-all:

| Data shape | Plot type |
|---|---|
| FPS / per-bin series, large and ~uniformly spaced | `SignalXY` |
| Irregular or gap-heavy series | `Scatter` over decimated points |
| Zoomed-in windows | `Scatter` with full-resolution raw points |
| Comparison overlays | second plot, independent decimation |
| Averages / excluded-gap markers | `HorizontalLine` / shaded spans |

Decimation pipeline (in `Core`): binary-search visible index range →
M4 min/max envelope (zoomed out) or LTTB (mid zoom) → raw points (close
zoom). Analytics always consume full-resolution arrays. Tooltip = crosshair
+ custom data tip. Cursor-anchored wheel zoom is verified early (Phase 6);
custom handler if ScottPlot defaults to center-anchored. PNG report renders
an off-screen ScottPlot figure — no state-swap hack.

## 6. Persistence

| Store | Location |
|---|---|
| Settings | `%APPDATA%\FrameViewAnalyzer\V2\settings.json` |
| Manual metadata | `%APPDATA%\FrameViewAnalyzer\V2\metadata.json` |
| Library index | `%APPDATA%\FrameViewAnalyzer\V2\library.json` |
| Logs | `%LOCALAPPDATA%\FrameViewAnalyzer\logs\` |

- Python and C# apps never share store files (safe coexistence).
- One-way legacy import only; legacy files are never written; no
  bidirectional synchronization.
- JSON only; atomic writes (tmp + move); version-gated loaders; unknown
  versions left untouched. SQLite later only if size/query needs justify it.
- Main-window state (size, position, maximized) persisted from Phase 4;
  restored coordinates validated against `SystemParameters.VirtualScreen*`
  with centered fallback.

## 7. MVVM

`MainWindowViewModel` (sessions, KPI tiles, status) owns
`ChartViewModel` (metric selection, series, view range, toggles) and
`AnalysisRangeViewModel` (thresholds/trim). Separate window VMs:
`BenchmarkLibraryViewModel`, `MetadataEditorViewModel`,
`SessionDetailsViewModel` (read-only model), `ExportDialogViewModel`.
Code-behind limited to presentation-only glue (e.g., wheel-cycling over the
closed metric selector). English-only UI; strings centralized in
`Resources/StringResources` so localization could be added later without a
framework.

Summary CSV table view: **deferred** (parity review in Phase 13).
Statistics CSV export is kept.

## 8. Testing

xUnit. Ported Python behaviors: parsing/encodings, harmonic FPS binning,
auto-threshold bounds, IQR transition fence, active-window envelope +
no-valid-bin rule, percentile interpolation (exact parity), direction
semantics, range analysis (4 algorithms + degeneracies), delta math, store
roundtrips/malformed/unknown-version, identity/move tolerance, package
validation/import, golden-file statistics certified against the Python app.

## 9. Migration roadmap

| Phase | Deliverable |
|---|---|
| 0 | .NET 10 SDK + repository + solution skeleton |
| 1 | CSV reader + domain models + capture scanner |
| 2 | Analytics engine |
| 3 | Analytics test parity + golden fixture vs Python output |
| 4 | WPF shell + themes + main-window state persistence |
| 5 | Single-session chart (ScottPlot adapter + decimation) |
| 6 | Visible-range statistics + interactions |
| 7 | Base/Comparison + KPI delta mode |
| 8 | Analyze menu (full / worst / stable / largest drop / A-B) |
| 9 | Metadata editor + v2 store + detected prefill |
| 10 | Benchmark Library + legacy one-way importer |
| 11 | Exports (PNG report, Statistics CSV, Benchmark JSON) |
| 12 | BenchmarkDotNet + optimization vs baselines |
| 13 | Feature-parity audit (incl. deferred summary-table decision) |
| 14 | Release candidate (single-file ZIP) |
| 15 | Main-window UI polish + stable release preparation |

### Development gates (every phase)

1. Implement one phase only. 2. Run tests. 3. Report. 4. STOP for manual
review. 5. Fix reported issues. 6. Only explicit user approval marks the
phase complete. 7. User decides commit/push (feature branch → squash PR into
`main`; never commit directly to `main`). 8. Only then the next phase begins.
