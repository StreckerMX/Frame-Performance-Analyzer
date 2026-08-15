# Analytics parity with the Python reference

Phase 3 goal: certify that the C# analytics engine reproduces the Python
application's numeric behavior exactly. The Python application remains the
authoritative reference; the fixture below is its certified output.

## Golden fixture

| File | Content |
|---|---|
| `tests/FrameViewAnalyzer.Analytics.Tests/Fixtures/golden_base.csv` | Synthetic 20-second capture (3 frames/s), loading seconds 7–9 at 30% GPU, metadata columns |
| `tests/FrameViewAnalyzer.Analytics.Tests/Fixtures/golden_comparison.csv` | Same shape, different frame times/temps/power |
| `tests/FrameViewAnalyzer.Analytics.Tests/Fixtures/golden.json` | Certified output: catalog, active window, valid bins, diagnostics, metadata, per-metric series + statistics, and all 29 Base/Comparison rows (values, deltas, improvement kinds) |

Generation (read-only use of the Python repo):

```powershell
python tools/generate_golden_fixture.py C:\Users\strec\GitHub\frameview-analyzer
```

The fixture uses only core catalog columns on purpose: metric IDs match
between both apps. Dynamic-column IDs intentionally differ (Python uses
BLAKE2s-4, C# uses FNV-1a — see `MetricCatalogBuilder`).

`GoldenFixtureTests` asserts, with a 1e-9 tolerance:

- catalog identity, order, labels, units, directions
- active window start/end and the exact valid-bin set
- all five filter-diagnostic counters + FPS upper bound
- effective (auto) GPU threshold
- detected metadata strings and counts
- every metric's full series (x and y) and its statistics
- every comparison row (base/comparison values, delta, delta %, kind)

## Python test → C# test mapping

| Python file | C# coverage |
|---|---|
| `test_analytics.py` (AnalyticsTests, FormatDurationHumanTests) | `AnalyticsEngineTests`, `DisplayTextTests` |
| `test_metrics.py` (MetricCatalogTests, ImprovementKindTests) | `MetricCatalogTests` (Core) |
| `test_csv_loader.py` (numeric/NA semantics) | `CsvValuesTests`, `ColumnInspectorTests` |
| `test_capture_library.py` | `CaptureFolderScannerTests`, `CaptureLabelBuilderTests` |
| reporting delta math | `ComparisonServiceTests` |
| `test_session_state.py`, `test_settings.py` | later phases (UI/library stores) |

## Certification status

- Python reference output regenerated and committed: `golden.json` (format v1).
- C# `GoldenFixtureTests` passes against it (see phase report).
- Remaining Python suites (range analysis, reporting exporters, benchmark
  summary, library, metadata) are scheduled for their roadmap phases.
