# FrameView Analyzer — Release Candidate (Phase 14)

**Version:** 2.0.0-rc.1 (release candidate — not the final stable release)

FrameView Analyzer is a Windows desktop application for analyzing NVIDIA
FrameView captures: load one or two FrameView CSVs, filter GPU-active seconds,
exclude loading transitions, and compare captures with percentile lows,
harmonic FPS bins, and per-metric charts.

## System requirements

- **Windows 11 x64 recommended.**
- **Windows 10 x64** support is limited to editions/releases currently
  supported by .NET 10, such as applicable Enterprise/LTSC versions. Ordinary
  end-of-service Windows 10 editions are not supported.
- **No .NET installation is required.** The application is self-contained.
- No installer: unzip and run.

## How to launch

1. Unzip `FrameViewAnalyzer-v2.0.0-rc.1-win-x64.zip`.
2. Double-click `FrameViewAnalyzer.exe`.

Windows SmartScreen may warn about an unknown publisher for the release
candidate. Choose **More info → Run anyway** if you trust the source of the
download.

## Supported files

- **FrameView Log CSVs** (`FrameView_*.csv`) — opened as the Base or
  Comparison capture and analyzed normally.
- **FrameView Summary CSVs** (`FrameView_Summary.csv`) — opened as a
  read-only table view; they never occupy the analysis slots.

## Where data is stored

| What | Location |
|---|---|
| Settings, metadata, library | `%APPDATA%\FrameViewAnalyzer\V2\` |
| Log files | `%LOCALAPPDATA%\FrameViewAnalyzer\logs\` |

The portable ZIP does **not** store settings beside the executable. Logs are
kept locally, roll daily, and are retained for 7 days. The application has
**no telemetry** and makes no network calls.

## How to uninstall

1. Delete the extracted folder (and the ZIP).
2. Optionally delete the data folders listed above to remove all traces.

## Known first-release deviations

- No Help / Learn-more card yet.
- No single-series fill-under-curve shading (cosmetic only).
- Dynamic metric IDs use FNV-1a (display keys only; never persisted).
- UI is English-only; string centralization is a post-release refactor.

Time-range drag selection is available: turn **Drag pan** off and drag
horizontally on the chart (minimum span 1 second).

See the repository `docs/PARITY.md` for the full parity audit.
