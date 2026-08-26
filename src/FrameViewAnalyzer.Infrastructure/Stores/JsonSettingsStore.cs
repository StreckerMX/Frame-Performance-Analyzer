using System.Text.Json;

namespace FrameViewAnalyzer.Infrastructure.Stores;

/// <summary>
/// JSON settings store at the v2 data location
/// (%APPDATA%\FrameViewAnalyzer\V2\settings.json). The Python application
/// uses the parent folder; v2 never shares store files with it. Writes are
/// atomic (temp file + move) and unknown format versions fall back to
/// defaults without overwriting anything.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;

    public JsonSettingsStore(string? path = null) => _path = path ?? DefaultSettingsPath();

    public static string DefaultAppDataRoot()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "FrameViewAnalyzer", "V2");
    }

    public static string DefaultSettingsPath() =>
        Path.Combine(DefaultAppDataRoot(), "settings.json");

    public SettingsDocument Load()
    {
        try
        {
            var payload = JsonSerializer.Deserialize<SettingsPayload>(File.ReadAllText(_path));
            if (payload?.FormatVersion != 1)
            {
                return new SettingsDocument();
            }

            var appearance = payload.AppearanceMode is "dark" or "light"
                ? payload.AppearanceMode
                : "dark";
            var captureDirectory = string.IsNullOrWhiteSpace(payload.CaptureDirectory)
                ? null
                : payload.CaptureDirectory;
            var reportMetrics = payload.LastPngReportMetricIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (reportMetrics is { Length: 0 })
            {
                reportMetrics = null;
            }

            return new SettingsDocument(
                FormatVersion: 1,
                CaptureDirectory: captureDirectory,
                AppearanceMode: appearance,
                Window: payload.Window,
                LastPngReportMetricIds: reportMetrics);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SettingsDocument();
        }
    }

    public void Save(SettingsDocument settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new SettingsPayload(
            settings.FormatVersion,
            settings.CaptureDirectory,
            settings.AppearanceMode,
            settings.Window,
            settings.LastPngReportMetricIds);
        var json = JsonSerializer.Serialize(payload, JsonOptions.Indented);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json + Environment.NewLine);
        File.Move(temporary, _path, overwrite: true);
    }

    private sealed record SettingsPayload(
        int FormatVersion,
        string? CaptureDirectory,
        string? AppearanceMode,
        WindowStateDocument? Window,
        IReadOnlyList<string>? LastPngReportMetricIds);

    internal static class JsonOptions
    {
        public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    }
}
