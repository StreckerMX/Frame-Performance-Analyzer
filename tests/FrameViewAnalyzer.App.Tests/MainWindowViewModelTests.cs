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

        public string? LastInfo { get; private set; }

        public string? PickCsvFile(string? initialDirectory) => NextCsvPath;

        public void ShowError(string title, string message) => LastError = $"{title}: {message}";

        public void ShowInfo(string title, string message) => LastInfo = $"{title}: {message}";
    }

    private static (
        MainWindowViewModel ViewModel,
        FakeSettingsStore Settings,
        FakeThemeService Themes,
        FakeDialogService Dialogs,
        string Directory) Create(string appearanceMode = "dark")
    {
        var settings = new FakeSettingsStore { Current = new SettingsDocument(AppearanceMode: appearanceMode) };
        var themes = new FakeThemeService();
        var dialogs = new FakeDialogService();
        var reader = new FrameViewCsvReader();
        var analysis = new CaptureAnalysisService();
        var directory = Path.Combine(Path.GetTempPath(), "fva-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var viewModel = new MainWindowViewModel(
            settings,
            themes,
            new ChartViewModel(),
            reader,
            analysis,
            dialogs);
        return (viewModel, settings, themes, dialogs, directory);
    }

    private static string WriteCapture(string directory, string fileName, double frameTime = 10.0)
    {
        var csvPath = Path.Combine(directory, fileName);
        var rows = string.Concat(
            Enumerable.Range(0, 6).SelectMany(second =>
                new[] { 0.0, 0.25, 0.5 }.Select(offset =>
                    $"{second + offset},{frameTime},80\n")));
        File.WriteAllText(csvPath, "TimeInSeconds,MsBetweenPresents,GPU0Util(%)\n" + rows);
        return csvPath;
    }

    private static void Cleanup(string directory)
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

    [Fact]
    public void Constructor_adopts_the_saved_theme()
    {
        var (viewModel, _, _, _, directory) = Create(appearanceMode: "light");
        Cleanup(directory);

        Assert.Equal("light", viewModel.AppearanceMode);
        Assert.False(viewModel.IsDark);
        Assert.True(viewModel.IsLight);
    }

    [Fact]
    public void ChangeAppearance_applies_theme_and_persists()
    {
        var (viewModel, settings, themes, _, directory) = Create();
        Cleanup(directory);

        viewModel.ChangeAppearanceCommand.Execute("light");

        Assert.Equal("light", viewModel.AppearanceMode);
        Assert.Equal("light", themes.Current);
        Assert.Equal("light", settings.Current.AppearanceMode);
    }

    [Fact]
    public async Task Load_base_fills_the_cards_chart_and_status()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv");

            await viewModel.LoadBaseCommand.ExecuteAsync(null);

            Assert.Null(dialogs.LastError);
            Assert.True(viewModel.Chart.HasData);
            Assert.Equal("Test", viewModel.BaseSessionName);
            Assert.Contains("CAPTURE OPENED", viewModel.StatusText);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Load_comparison_requires_a_base_session()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv");

            await viewModel.LoadComparisonCommand.ExecuteAsync(null);

            Assert.NotNull(dialogs.LastInfo);
            Assert.Contains("base session", dialogs.LastInfo, StringComparison.OrdinalIgnoreCase);
            Assert.Null(viewModel.BaseSession);
            Assert.Null(viewModel.ComparisonSession);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Comparison_load_updates_cards_and_delta_line()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Base_Log.csv", frameTime: 10.0);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Comp_Log.csv", frameTime: 20.0);

            await viewModel.LoadComparisonCommand.ExecuteAsync(null);

            Assert.NotNull(viewModel.ComparisonSession);
            Assert.Equal(2, viewModel.Chart.SeriesList.Count);
            Assert.Contains("→", viewModel.ComparisonDeltaLine);
            Assert.Contains("-50.0%", viewModel.ComparisonDeltaLine);
            Assert.Contains("COMPARISON OPENED", viewModel.StatusText);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Remove_comparison_keeps_the_base()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Base_Log.csv", frameTime: 10.0);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Comp_Log.csv", frameTime: 20.0);
            await viewModel.LoadComparisonCommand.ExecuteAsync(null);

            viewModel.RemoveComparisonCommand.Execute(null);

            Assert.NotNull(viewModel.BaseSession);
            Assert.Null(viewModel.ComparisonSession);
            Assert.Single(viewModel.Chart.SeriesList);
            Assert.Contains("COMPARISON SESSION REMOVED", viewModel.StatusText);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Remove_base_promotes_the_comparison()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Base_Log.csv", frameTime: 10.0);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Comp_Log.csv", frameTime: 20.0);
            await viewModel.LoadComparisonCommand.ExecuteAsync(null);
            var comparisonSession = viewModel.ComparisonSession;

            viewModel.RemoveBaseCommand.Execute(null);

            Assert.Equal(comparisonSession, viewModel.BaseSession);
            Assert.Null(viewModel.ComparisonSession);
            Assert.Single(viewModel.Chart.SeriesList);
            Assert.Contains("now the base session", viewModel.StatusText);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Load_base_reports_errors_without_crashing()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = "Z:/missing/file.csv";

            await viewModel.LoadBaseCommand.ExecuteAsync(null);

            Assert.False(viewModel.Chart.HasData);
            Assert.NotNull(dialogs.LastError);
            Assert.Contains("CSV loading error", dialogs.LastError);
        }
        finally
        {
            Cleanup(directory);
        }
    }
}
