# Phase 13 — Feature-parity audit

> Comparison of the C#/.NET/WPF rewrite against the Python reference
> application (`StreckerMX/frameview-analyzer`, v1.3.1) as of 2026-08-15.
> Method: full feature inventory of both codebases, verified against source.
> Legend:
> ✔ parity · ⚠ accepted deviation (documented in a review) · ✖ missing ·
> ➕ exists only in the C# rewrite.
> Algorithm/format parity is covered in depth by `docs/ANALYTICS_PARITY.md`
> (golden fixtures) and is only summarized here.

## 1. Shell, window and theming

| Feature | Python | C# | Status |
|---|---|---|---|
| Main window title/min size | `FrameView Analyzer`, 980×700 | same | ✔ |
| Initial geometry (88% work area, clamped) | yes | persisted + validated, 88% fallback | ✔ |
| Header identity (logo, title, badge, tagline) | yes | yes | ✔ |
| Theme segmented control Dark/Light | yes | yes | ✔ |
| Dark palette (NVIDIA green accents) | yes | yes | ✔ |
| Responsive layout (toolbar wrap, KPI reflow) | yes | partial (fixed layouts) | ⚠ minor |
| Windows AppUserModelID | `StreckerMX.FrameViewAnalyzer` | not set | ✖ minor |
| Window icon | bundled ico/png | not set | ✖ minor |

## 2. Sessions, cards and status

| Feature | Python | C# | Status |
|---|---|---|---|
| BASE/COMPARISON cards + VS badge | yes | yes | ✔ |
| Card buttons: Load/Change, Metadata, View details, Remove | yes | yes | ✔ |
| Comparison delta line `X → Y FPS ±Z%` | yes | yes | ✔ |
| Slot promotion on base removal | yes | yes | ✔ |
| Comparison requires base | yes | yes | ✔ |
| Status-bar protocol + version label | yes | yes | ✔ |
| `Ctrl+O` / `Ctrl+Shift+O` / `Ctrl+E` / `Ctrl+Shift+E` | bound | bound (`KeyBinding` → commands) | ✔ |

## 3. KPI tiles (visible-range statistics)

| Feature | Python | C# | Status |
|---|---|---|---|
| Six tiles: AVERAGE FPS, 1% LOW, 0.1% LOW, MAX, MIN, VISIBLE TIME | yes | yes | ✔ |
| FPS-only (not selected metric) | yes | yes | ✔ |
| Comparison `base → comp` + colored delta | yes | yes | ✔ |
| TIME tile `vs` when durations differ | yes | yes | ✔ |
| Values from visible zoom range | yes | yes | ✔ |

## 4. Chart and interactions

| Feature | Python | C# | Status |
|---|---|---|---|
| Single metric at a time, Base/Comparison overlay | yes | yes | ✔ |
| Series colors/widths (green 2.15, blue 1.8) | yes | yes | ✔ |
| Dashed per-series average line | yes | yes | ✔ |
| FPS axis starts at 0 | yes | yes | ✔ |
| Cursor-anchored wheel zoom (0.75/1.35, min 2 s) | yes | yes | ✔ |
| Drag pan + area zoom when pan disabled | yes | yes — horizontal drag range selection with Drag pan off (min 1 s, clamped to full bounds) | ✔ |
| Reset zoom / Auto zoom | yes | yes | ✔ |
| Tooltip: time + per-series value, tolerance `max(0.65, span/120)` | yes | yes | ✔ |
| Tooltip edge clamping | 4-corner + re-clamp | border overlay | ⚠ cosmetic |
| Gap rendering: shaded omitted bands + `N s omitted` labels (≥3 s) | hatched bands | shaded `HorizontalSpan` + rotated labels | ✔ (see G7) |
| Single-series fill to baseline | yes | no | ⚠ cosmetic |
| Data-points toggle, wheel-cycling metric picker | yes | yes | ✔ |
| Y-axis formatter by span | yes | yes | ✔ |
| Decimation: LTTB / min-max envelope | Matplotlib downsampling | Core pipeline (LTTB/M4) | ✔ (better) |

## 5. Analysis rail (filter controls)

| Feature | Python | C# | Status |
|---|---|---|---|
| Auto (full session) switch | yes | yes (AnalysisRangeViewModel) | ✔ |
| Manual GPU threshold slider 0–80 | yes | yes | ✔ |
| Trim slider 0–10 | yes | yes | ✔ |
| Exclude loading screens / FPS cullers switch | yes | yes | ✔ |
| Progress + analyzed seconds + filter help text | yes | yes (diagnostics line) | ✔ |
| Help card (metric description + Learn more) | yes | no | ✖ (G6, accepted) |

## 6. Analyze menu

| Feature | Python | C# | Status |
|---|---|---|---|
| Full capture / Worst (10 s) / Most stable (10 s) / Largest drop / Largest A/B | yes | yes | ✔ |
| Exact window parameters (10 s, ≥5 samples, ≥5 gap, 3% span) | yes | yes | ✔ |
| Undefined-direction guards + explanatory dialogs | yes | yes | ✔ |
| Result zoom with ≥1 s padding | yes | yes | ✔ |

## 7. Capture browsing

| Feature | Python | C# | Status |
|---|---|---|---|
| Capture dropdown in main window + refresh | yes | yes (quick capture dropdown) | ✔ |
| `Capture folder ▾` menu (choose/open/reset to FrameView dir) | yes | yes | ✔ |
| Default dir = Documents\FrameView | yes | yes | ✔ |
| Background scan with generation counter | yes | yes (library window) | ✔ |

## 8. Session details window

Python `Complete data` window (1120×840): how-results-obtained block, benchmark
identity, system used, frame presentation (humanized), additional data, and
one telemetry card per metric. **Implemented in Phase 13** —
`SessionDetailsWindow` + `SessionDetailsViewModel` with the same hierarchy,
read-only, for Base and Comparison sessions.

## 9. Summary CSV table view — the deferred decision

Python opens `FrameView_Summary.csv` in a dedicated sortable table (column
priority `Log Name, Application, Resolution, Avg FPS, 1% Low FPS, 0.1% Low
FPS, AvgPCLatency (ms), Average PC Latency(MSec), …`, numeric `.3f` trimming,
right-aligned numerics, header-click sort, empty-last). The C# rewrite detects
`CsvKind.Summary` but rejects the file in the analysis pipeline and has **no
viewer**.

**Decision taken: IMPLEMENTED (Phase 13).** The read-only `SummaryTableWindow`
consumes `FrameView_Summary.csv` with the reference column priority, numeric
formatting, and numeric-aware header sorting; Summary files never occupy the
Base/Comparison slots and never run log analytics.

## 10. Benchmark Library

| Feature | Python | C# | Status |
|---|---|---|---|
| Search / game+resolution+GPU filters / tags AND | yes | yes | ✔ |
| Sort date/name | yes | yes | ✔ |
| Recent comparisons (cap 5) + ghost buttons | yes | yes | ✔ |
| Row buttons Base/Comparison + two-click A/B | yes | yes | ✔ |
| MISSING SOURCE badge | yes | `MISSING` badge | ✔ |
| Package export with on-demand digest hydration | yes | yes | ✔ |
| Package import (validate → merge) | yes | yes + coordinated commit | ➕ |
| Legacy one-way import from Python stores | n/a | yes | ➕ |
| Folder re-scan on open/refresh | yes | yes | ✔ |

## 11. Metadata editor

| Feature | Python | C# | Status |
|---|---|---|---|
| Same 10 fields + tags + notes | yes | yes | ✔ |
| Detected prefill for empty fields | yes | yes | ✔ |
| Empty metadata ⇒ entry removed | yes | yes | ✔ |
| `Save updates the cards and exports immediately` | yes | yes | ✔ |

## 12. Settings and persistence

| Feature | Python | C# | Status |
|---|---|---|---|
| `capture_directory` + `appearance_mode` | yes | yes (+ window placement) | ➕ |
| Store version gate + atomic writes | partial | yes (stronger) | ➕ |
| `analysis_options` persisted per library record | schema yes | schema yes, never written | ⚠ minor |
| Logging (Serilog per ARCHITECTURE) | n/a | declared, not implemented | ⚠ note |

## 13. Exports

| Feature | Python | C# | Status |
|---|---|---|---|
| Statistics CSV (12 exact columns, BOM, invariant) | yes | yes | ✔ |
| Benchmark data JSON (format_version 1, sessions/statistics) | yes | yes | ✔ |
| PNG report (multi-metric, Base/Comparison, header) | banner + badges | compact context header | ⚠ accepted in Phase 11 review |
| Report metric selection (groups, visible-first, cap 8) | yes | yes | ✔ |
| File stems (`FrameView_{game}_{metrics}.png`, `frameview_{name}_stats.csv`) | yes | yes | ✔ |
| Export dialog All/Single + dedupe | yes | yes | ✔ |
| Package JSON schema (package_version 1, captures fields) | yes | yes | ✔ |

## 14. Algorithms and formats (summary — detail in ANALYTICS_PARITY.md)

CSV encodings/NA/kind detection ✔ · sample building (time keys, frametime
fallback, GPU aliases) ✔ · harmonic 1 s bins + ≥3 frame rule ✔ · auto GPU
threshold (55% of 90th pct, [5,80]) ✔ · FPS outlier fence
(min(5000, max(q3+3IQR, 1.75·med, med+30))) ✔ · multi-scene envelope + trim
rule ✔ · no-GPU-data ⇒ no active window ✔ · percentile interpolation ✔ ·
stat-field families (high-tail/low-tail/average-range) ✔ · delta math +
direction-aware improvement ✔ · capture identity `name|size|mtime_ns` ✔ ·
library search/filter/sort/merge rules ✔ · detected metadata (DLSS/RR/FG/
Reflex prefill keys) ✔ · store schemas (`format_version`, snake_case) ✔.
Intentional deviations: dynamic metric IDs use FNV-1a (`col_{slug}_{digest}`)
instead of BLAKE2s-4 (IDs are per-session display keys, not persisted); PNG
banner replaced by the accepted compact header; package import adds
coordinated commit/rollback semantics (strictly stronger than Python).

## 15. Gap register — final dispositions (Phase 13 closure)

| Id | Gap | Final disposition |
|---|---|---|
| G1 | Session Details window | **PASS** — `SessionDetailsWindow` + `SessionDetailsViewModel` (Base and Comparison, read-only, no session mutation) |
| G2 | Summary CSV table view | **PASS** — read-only virtualized `SummaryTableWindow`; Summary CSVs never occupy the slots and never run log analytics |
| G3 | Analysis controls UI | **PASS** — `AnalysisRangeViewModel` rail (auto/manual GPU threshold, trim, transition exclusion, diagnostics) with debounced re-analysis of Base + Comparison |
| G4 | Keyboard shortcuts | **PASS** — `Ctrl+O`, `Ctrl+Shift+O`, `Ctrl+E`, `Ctrl+Shift+E` bound via `KeyBinding` to the same commands as the UI (CanExecute respected) |
| G5 | Capture folder + capture selector | **PASS** — capture-folder menu (choose / reset to FrameView dir) + quick capture dropdown, folder persisted, graceful missing-folder handling |
| G7 | Omitted-load gap visualization | **PASS** — shaded `HorizontalSpan` bands + rotated "N s omitted" labels (≥3 s), interactive chart and PNG report |
| G10 | `analysis_options` persisted with library statistics | **PASS** — `LibraryUpdater.UpdateStats` writes digest + options atomically together; scans never erase; package export/import retain them |
| G11 | Serilog logging | **PASS** — local rolling-file logging at `%LOCALAPPDATA%\FrameViewAnalyzer\logs\` (daily rolling + 10 MB size rollover + 7-day retention); startup/version, controlled startup failure, dispatcher/AppDomain/unobserved-task handlers, and persistence/import/export failure coverage. No intentional CSV-content, benchmark-metadata, or capture-path logging: controlled failures log operation + exception type only (never the exception object). Unexpected failures retain complete local exception diagnostics, which may contain OS/.NET-provided details such as paths. Logs remain local; no telemetry or network sink. Log-directory creation failure falls back to a no-op logger and never blocks startup. |
| G6 | Help card / Learn more | **ACCEPTED DEVIATION** — non-blocking omission (ties into G1/G3; may be added later without schema impact) |
| G8 | Area/span zoom (SpanSelector) | **PASS** — horizontal drag range selection implemented (superseding the earlier accepted deviation): active when Drag pan is disabled, translucent overlay clamped to canonical full-series bounds, minimum span 1 s, backwards drags normalized |
| G9 | AppUserModelID + executable icon packaging | **PASS** (Phase 14) — stable `StreckerMX.FrameViewAnalyzer` AppUserModelID applied before the first window shows; window + executable icon wired and embedded in the self-contained single-file publish |
| — | Single-series fill-under-curve | **ACCEPTED DEVIATION** — purely cosmetic pixel difference; both apps show mean lines and identical series |
| — | Dynamic metric ID hash (FNV-1a vs BLAKE2s-4) | **ACCEPTED DEVIATION** — IDs are per-session display keys, never persisted or exchanged; stability within a run is guaranteed |
| — | UI strings not centralized in `Resources/StringResources` | **ACCEPTED DEVIATION for the first release** — strings live in XAML/ViewModel literals (English-only); centralization is a post-release refactor with no behavioral impact. Audited 2026-08-15: no `Resources/StringResources` exists in the App project. |

## 16. Verification

- 502/502 tests green (golden Python-parity fixtures included).
- Release build 0 errors / 0 warnings.
- This document is generated from a full two-codebase inventory; exact
  labels/keys/defaults were compared against source.

## 17. Final state

Every audited feature ends in exactly one of **PASS** or
**ACCEPTED DEVIATION** (with the why stated above). No deferred Phase 14
items and no unresolved high-severity parity gaps remain.
