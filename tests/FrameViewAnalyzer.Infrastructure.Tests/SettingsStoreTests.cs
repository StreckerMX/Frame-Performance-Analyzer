using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class SettingsStoreTests
{
    private static string TempSettingsPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }

    [Fact]
    public void Save_and_load_roundtrip_preserves_all_fields()
    {
        var path = TempSettingsPath();
        var store = new JsonSettingsStore(path);
        var expected = new SettingsDocument(
            FormatVersion: 1,
            CaptureDirectory: @"C:\Captures",
            AppearanceMode: "light",
            Window: new WindowStateDocument(100, 80, 1200, 800, Maximized: true));

        store.Save(expected);

        var loaded = store.Load();
        Assert.Equal(expected, loaded);
    }

    [Fact]
    public void Png_report_metric_selection_roundtrips_and_is_cleaned()
    {
        var path = TempSettingsPath();
        var store = new JsonSettingsStore(path);
        store.Save(new SettingsDocument(
            LastPngReportMetricIds: ["fps", "frametime", "fps", " gpu0_util "]));

        var loaded = store.Load();

        Assert.Equal(["fps", "frametime", "gpu0_util"], loaded.LastPngReportMetricIds);
    }

    [Fact]
    public void Missing_file_returns_defaults()
    {
        var path = TempSettingsPath();

        var loaded = new JsonSettingsStore(path).Load();

        Assert.Equal(1, loaded.FormatVersion);
        Assert.Null(loaded.CaptureDirectory);
        Assert.Equal("dark", loaded.AppearanceMode);
        Assert.Null(loaded.Window);
        Assert.Null(loaded.LastPngReportMetricIds);
    }

    [Fact]
    public void Malformed_json_returns_defaults()
    {
        var path = TempSettingsPath();
        File.WriteAllText(path, "{ not valid json ");

        var loaded = new JsonSettingsStore(path).Load();

        Assert.Equal("dark", loaded.AppearanceMode);
        Assert.Null(loaded.Window);
    }

    [Fact]
    public void Unknown_format_version_returns_defaults_without_overwriting()
    {
        var path = TempSettingsPath();
        File.WriteAllText(path, """{ "format_version": 99, "appearance_mode": "light" }""");

        var loaded = new JsonSettingsStore(path).Load();

        Assert.Equal("dark", loaded.AppearanceMode);
    }

    [Fact]
    public void Invalid_appearance_mode_falls_back_to_dark()
    {
        var path = TempSettingsPath();
        File.WriteAllText(path, """{ "format_version": 1, "appearance_mode": "blue" }""");

        var loaded = new JsonSettingsStore(path).Load();

        Assert.Equal("dark", loaded.AppearanceMode);
    }

    [Fact]
    public void Blank_capture_directory_becomes_null()
    {
        var path = TempSettingsPath();
        File.WriteAllText(path, """{ "format_version": 1, "capture_directory": "   " }""");

        var loaded = new JsonSettingsStore(path).Load();

        Assert.Null(loaded.CaptureDirectory);
    }

    [Fact]
    public void Default_path_lives_in_the_v2_data_location()
    {
        var path = JsonSettingsStore.DefaultSettingsPath();

        Assert.EndsWith(
            Path.Combine("FrameViewAnalyzer", "V2", "settings.json"),
            path);
    }

    [Fact]
    public void Save_overwrites_previous_content()
    {
        var path = TempSettingsPath();
        var store = new JsonSettingsStore(path);
        store.Save(new SettingsDocument(AppearanceMode: "dark"));

        store.Save(new SettingsDocument(AppearanceMode: "light"));

        Assert.Equal("light", store.Load().AppearanceMode);
    }
}
