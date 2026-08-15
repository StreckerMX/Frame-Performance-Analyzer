using System.IO;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Tests;

public class MainWindowViewModelTests
{
    private sealed class FakeSettingsStore : ISettingsStore
    {
        public SettingsDocument Current { get; set; } = new();

        public int SaveCount { get; private set; }

        public SettingsDocument Load() => Current;

        public void Save(SettingsDocument settings)
        {
            Current = settings;
            SaveCount++;
        }
    }

    private sealed class FakeThemeService : IThemeService
    {
        public string Current { get; private set; } = "dark";

        public int ApplyCount { get; private set; }

        public event EventHandler? Changed;

        public void Apply(string mode)
        {
            Current = mode;
            ApplyCount++;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public string? NextCsvPath { get; set; }

        public string? LastError { get; private set; }

        public string? PickCsvFile(string? initialDirectory) => NextCsvPath;

        public void ShowError(string title, string message) => LastError = $"{title}: {message}";

        public void ShowInfo(string title, string message)
        {
        }
    }

    private static (
        MainWindowViewModel ViewModel,
        FakeSettingsStore Settings,
        FakeThemeService Themes,
        FakeDialogService Dialogs) Create(
        string appearanceMode = "dark",
        string? nextCsvPath = null)
    {
        var settings = new FakeSettingsStore { Current = new SettingsDocument(AppearanceMode: appearanceMode) };
        var themes = new FakeThemeService();
        var dialogs = new FakeDialogService { NextCsvPath = nextCsvPath };
        var reader = new FrameViewCsvReader();
        var analysis = new CaptureAnalysisService();
        var viewModel = new MainWindowViewModel(
            settings,
            themes,
            new ChartViewModel(analysis),
            reader,
            analysis,
            dialogs);
        return (viewModel, settings, themes, dialogs);
    }

    [Fact]
    public void Constructor_adopts_the_saved_theme()
    {
        var (viewModel, _, _, _) = Create(appearanceMode: "light");

        Assert.Equal("light", viewModel.AppearanceMode);
        Assert.False(viewModel.IsDark);
        Assert.True(viewModel.IsLight);
    }

    [Fact]
    public void ChangeAppearance_applies_theme_and_persists()
    {
        var (viewModel, settings, themes, _) = Create();

        viewModel.ChangeAppearanceCommand.Execute("light");

        Assert.Equal("light", viewModel.AppearanceMode);
        Assert.True(viewModel.IsLight);
        Assert.False(viewModel.IsDark);
        Assert.Equal("light", themes.Current);
        Assert.Equal("light", settings.Current.AppearanceMode);
    }

    [Fact]
    public void Reapplying_the_current_theme_is_a_no_op()
    {
        var (viewModel, settings, themes, _) = Create();

        viewModel.ChangeAppearanceCommand.Execute("dark");

        Assert.Equal(0, settings.SaveCount);
        Assert.Equal(0, themes.ApplyCount);
    }

    [Fact]
    public void Invalid_mode_normalizes_to_dark()
    {
        var (viewModel, settings, _, _) = Create(appearanceMode: "light");

        viewModel.ChangeAppearanceCommand.Execute("sepia");

        Assert.Equal("dark", viewModel.AppearanceMode);
        Assert.Equal("dark", settings.Current.AppearanceMode);
    }

    [Fact]
    public void Theme_radio_state_flows_through_bindings()
    {
        var (viewModel, settings, themes, _) = Create();

        viewModel.IsLight = true;

        Assert.Equal("light", viewModel.AppearanceMode);
        Assert.Equal("light", themes.Current);
        Assert.Equal("light", settings.Current.AppearanceMode);
    }

    [Fact]
    public void Status_text_is_ready_by_default()
    {
        var (viewModel, _, _, _) = Create();

        Assert.Contains("READY", viewModel.StatusText);
        Assert.Equal("FrameView Analyzer v2", viewModel.VersionText);
    }

    [Fact]
    public async Task Load_capture_fills_the_chart_and_status()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var csvPath = Path.Combine(directory, "FrameView_Test_Log.csv");
            var rows = string.Concat(
                Enumerable.Range(0, 6).SelectMany(second =>
                    new[] { 0.0, 0.25, 0.5 }.Select(offset =>
                        $"{second + offset},10,80\n")));
            File.WriteAllText(csvPath, "TimeInSeconds,MsBetweenPresents,GPU0Util(%)\n" + rows);
            var (viewModel, _, _, dialogs) = Create(nextCsvPath: csvPath);

            await viewModel.LoadCaptureCommand.ExecuteAsync(null);

            Assert.Null(dialogs.LastError);
            Assert.True(
                viewModel.Chart.HasData,
                $"status={viewModel.StatusText} window={viewModel.Chart.Session?.Window} "
                + $"samples={viewModel.Chart.SampleCount} metrics={viewModel.Chart.Metrics.Count} "
                + $"seriesLen={viewModel.Chart.Series?.X.Length ?? -1}");
            Assert.Contains("ANALYZED", viewModel.StatusText);
            Assert.Contains("18 samples", viewModel.StatusText);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; transient handles may briefly linger.
            }
        }
    }

    [Fact]
    public void Load_capture_reports_errors_without_crashing()
    {
        var (viewModel, _, _, dialogs) = Create(nextCsvPath: "Z:/missing/file.csv");

        viewModel.LoadCaptureCommand.Execute(null);

        Assert.False(viewModel.Chart.HasData);
        Assert.NotNull(dialogs.LastError);
        Assert.Contains("CSV loading error", dialogs.LastError);
    }
}
