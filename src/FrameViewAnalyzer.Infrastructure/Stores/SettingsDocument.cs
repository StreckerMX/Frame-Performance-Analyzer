namespace FrameViewAnalyzer.Infrastructure.Stores;

/// <summary>Persisted main-window placement (restore-time validated).</summary>
public sealed record WindowStateDocument(
    double Left,
    double Top,
    double Width,
    double Height,
    bool Maximized);

/// <summary>
/// Application preferences DTO. Versioned; the loader is tolerant and
/// unknown versions fall back to defaults.
/// </summary>
public sealed record SettingsDocument(
    int FormatVersion = 1,
    string? CaptureDirectory = null,
    string AppearanceMode = "dark",
    WindowStateDocument? Window = null,
    IReadOnlyList<string>? LastPngReportMetricIds = null);
