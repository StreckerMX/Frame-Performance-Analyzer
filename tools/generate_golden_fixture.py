"""Generates the Phase 3 golden fixture set using the Python application
as the certified behavioral reference (read-only: the Python repository is
never modified).

Usage:
    python tools/generate_golden_fixture.py <path-to-python-repo>

Writes:
    tests/FrameViewAnalyzer.Analytics.Tests/Fixtures/golden_base.csv
    tests/FrameViewAnalyzer.Analytics.Tests/Fixtures/golden_comparison.csv
    tests/FrameViewAnalyzer.Analytics.Tests/Fixtures/golden.json

The fixture uses only core catalog columns so metric IDs are identical in
both applications (dynamic-column IDs intentionally differ: the Python
reference uses BLAKE2s-4 while the C# version uses FNV-1a).
"""

from __future__ import annotations

import csv
import json
import sys
from pathlib import Path

if len(sys.argv) != 2:
    raise SystemExit("usage: python tools/generate_golden_fixture.py <python-repo-path>")

python_repo = Path(sys.argv[1])
sys.path.insert(0, str(python_repo))

from frameview_analyzer.analytics import (  # noqa: E402
    AnalysisOptions,
    analyze_session,
    get_stats_from_series,
    get_trimmed_series,
)
from frameview_analyzer.csv_loader import load_csv  # noqa: E402
from frameview_analyzer.metrics import improvement_kind  # noqa: E402
from frameview_analyzer.reporting import build_statistics_rows  # noqa: E402

FIXTURES = (
    Path(__file__).resolve().parents[1]
    / "tests"
    / "FrameViewAnalyzer.Analytics.Tests"
    / "Fixtures"
)
FIXTURES.mkdir(parents=True, exist_ok=True)

HEADERS = [
    "TimeInSeconds",
    "MsBetweenPresents",
    "GPU0Util(%)",
    "GPU0Temp(C)",
    "CPUUtil(%)",
    "MsPCLatency",
    "NV Pwr(W) (API)",
    "Application",
    "Resolution",
    "GPU",
    "CPU",
    "Runtime",
]


def build(seconds: int, frame_time: float, temp_base: float, power_base: float) -> list[dict]:
    rows: list[dict] = []
    for second in range(seconds):
        for offset in (0.0, 0.25, 0.5):
            in_loading = 7 <= second <= 9
            util = 30.0 if in_loading else 80.0
            rows.append(
                {
                    "TimeInSeconds": f"{second + offset:.3f}",
                    "MsBetweenPresents": f"{frame_time + 0.1 * second:.3f}",
                    "GPU0Util(%)": f"{util:.1f}",
                    "GPU0Temp(C)": f"{temp_base + second:.1f}",
                    "CPUUtil(%)": f"{40 + second % 7:.1f}",
                    "MsPCLatency": f"{20 + 0.5 * second:.2f}",
                    "NV Pwr(W) (API)": f"{power_base + 2 * second:.1f}",
                    "Application": "GoldenGame.exe",
                    "Resolution": "3840x2160",
                    "GPU": "NVIDIA GeForce RTX 5070 Ti",
                    "CPU": "AMD Ryzen 7 5700X3D 8-Core Processor",
                    "Runtime": "DXGI",
                }
            )
    return rows


def write_csv(path: Path, rows: list[dict]) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=HEADERS)
        writer.writeheader()
        writer.writerows(rows)


base_rows = build(20, 10.0, 50.0, 200.0)
comparison_rows = build(20, 8.0, 55.0, 190.0)
write_csv(FIXTURES / "golden_base.csv", base_rows)
write_csv(FIXTURES / "golden_comparison.csv", comparison_rows)

options = AnalysisOptions(
    gpu_threshold=10.0,
    trim_buffer_seconds=1.0,
    auto_gpu_threshold=True,
    exclude_transitions=True,
)
base = analyze_session(load_csv(FIXTURES / "golden_base.csv"), options)
comparison = analyze_session(load_csv(FIXTURES / "golden_comparison.csv"), options)


def session_payload(session) -> dict:
    metrics = []
    for metric in session.catalog:
        points = get_trimmed_series(session, metric.metric_id)
        stats = get_stats_from_series(metric.metric_id, [point.y for point in points])
        metrics.append(
            {
                "id": metric.metric_id,
                "label": metric.label,
                "unit": metric.unit,
                "higher_is_better": metric.higher_is_better,
                "points": [[point.x, point.y] for point in points],
                "stats": {key: value for key, value in stats.items()},
            }
        )
    meta = session.metadata
    return {
        "display_name": session.loaded.display_name,
        "window": session.active_window,
        "valid_bins": sorted(session.valid_bin_indices),
        "diagnostics": {
            "total_bins": session.filter_diagnostics.total_bins,
            "visible_bins": session.filter_diagnostics.visible_bins,
            "below_gpu_bins": session.filter_diagnostics.below_gpu_bins,
            "fps_outlier_bins": session.filter_diagnostics.fps_outlier_bins,
            "edge_trimmed_bins": session.filter_diagnostics.edge_trimmed_bins,
            "fps_upper_bound": session.filter_diagnostics.fps_upper_bound,
        },
        "metadata": {
            "application": meta.application if meta else None,
            "resolution": meta.resolution if meta else None,
            "gpu": meta.gpu if meta else None,
            "cpu": meta.cpu if meta else None,
            "runtime": meta.runtime if meta else None,
            "duration": meta.duration if meta else None,
            "capture_duration": meta.capture_duration if meta else None,
            "frame_count": meta.frame_count if meta else None,
            "metric_count": meta.metric_count if meta else None,
        },
        "effective_gpu_threshold": session.options.gpu_threshold,
        "metrics": metrics,
    }


comparison_payload: list[dict] = []
for row in build_statistics_rows(base, comparison):
    metric = next(
        (item for item in base.catalog if item.metric_id == row["metric_id"]),
        None,
    )
    if metric is None:
        metric = next(
            (item for item in comparison.catalog if item.metric_id == row["metric_id"]),
            None,
        )
    comparison_payload.append(
        {
            "metric_id": row["metric_id"],
            "statistic_key": row["statistic_key"],
            "base_value": row["base_value"],
            "comparison_value": row["comparison_value"],
            "delta": row["delta"],
            "delta_percent": row["delta_percent"],
            "kind": improvement_kind(
                metric.higher_is_better if metric else None,
                row["base_value"],
                row["comparison_value"],
            ),
        }
    )

payload = {
    "format_version": 1,
    "options": {
        "gpu_threshold": 10.0,
        "trim_buffer_seconds": 1.0,
        "auto_gpu_threshold": True,
        "exclude_transitions": True,
    },
    "base": session_payload(base),
    "comparison": session_payload(comparison),
    "comparison_rows": comparison_payload,
}
golden_path = FIXTURES / "golden.json"
golden_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
print(f"golden fixture written: {FIXTURES}")
