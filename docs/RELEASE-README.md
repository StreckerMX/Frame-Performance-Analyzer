# FrameView Analyzer 2.1.0

**Version:** 2.1.0 (stable release)

FrameView Analyzer is a Windows desktop application for analyzing NVIDIA
performance captures from FrameView and the NVIDIA App performance overlay.
It can load one or two captures, filter GPU-active seconds, exclude loading
transitions, compare sessions, and chart the metrics available in each source.

## What's new in 2.1.0

- Adds support for `NVIDIA_App_Performance_Log_*.csv` files exported by the NVIDIA App performance overlay.
- Treats NVIDIA App CSV rows as low-rate telemetry samples instead of pretending they are rendered frames.
- Charts NVIDIA sampled FPS, exported 1% Low, latency, GPU/CPU utilization, clocks, temperatures, power, voltage, fan speed, and other numeric telemetry when present.
- Keeps the existing FrameView per-frame harmonic-FPS analysis unchanged.
- Applies source-aware loading/transition filtering and minimum-sample rules.
- Discovers NVIDIA App performance logs in the selected capture folder.
- Includes regression coverage for source detection, folder discovery, duration parsing, sampled FPS binning, and telemetry series.

## System requirements

- **Windows 11 x64 recommended.**
- **Windows 10 x64** support is limited to editions/releases currently
  supported by .NET 10, such as applicable Enterprise/LTSC versions. Ordinary
  end-of-service Windows 10 editions are not supported.
- **No .NET installation is required.** The application is self-contained.
- No installer: unzip and run.

## How to launch

1. Unzip `FrameViewAnalyzer-v2.1.0-win-x64.zip`.
2. Double-click `FrameViewAnalyzer.exe`.

Windows SmartScreen may warn about an unknown publisher. Choose
**More info > Run anyway** if you trust the source of the download.

## Supported files

- **FrameView Log CSVs** (`*_Log.csv`) - opened as the Base or Comparison capture and analyzed from per-frame timing data.
- **NVIDIA App Performance Logs** (`NVIDIA_App_Performance_Log_*.csv`) - opened as the Base or Comparison capture and analyzed as sampled telemetry.
- **FrameView Summary CSVs** (`FrameView_Summary.csv`) - opened as a read-only table view; they never occupy the analysis slots.

NVIDIA App performance logs have lower temporal precision than FrameView logs.
Their exported FPS values are sampled telemetry, not frame-by-frame data, so
FrameView Analyzer does not synthesize per-frame precision that is absent from
the source file.

## Where data is stored

| What | Location |
|---|---|
| Settings, metadata, library | `%APPDATA%\FrameViewAnalyzer\V2\` |
| Log files | `%LOCALAPPDATA%\FrameViewAnalyzer\logs\` |

The portable ZIP does **not** store settings beside the executable. Logs are
kept locally, roll daily, and are retained for 7 days. The application has
**no telemetry** and makes no network calls.

## How to uninstall

1. Delete the extracted folder and the ZIP.
2. Optionally delete the data folders listed above to remove all traces.

## Known deviations

- No Help / Learn-more card yet.
- No single-series fill-under-curve shading (cosmetic only).
- Dynamic metric IDs use FNV-1a (display keys only; never persisted).
- UI is English-only; string centralization is a post-release refactor.

Time-range drag selection is available: turn **Drag pan** off and drag
horizontally on the chart (minimum span 1 second).

See the repository `docs/PARITY.md` for the full parity audit.
