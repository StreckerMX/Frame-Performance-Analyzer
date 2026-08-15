using System.IO;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;
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

        public string? NextSavePath { get; set; }

        public string? NextOpenPath { get; set; }

        public string? LastError { get; private set; }

        public string? LastInfo { get; private set; }

        public string? PickCsvFile(string? initialDirectory) => NextCsvPath;

        public string? PickSaveFile(string? initialFile, string filter, string defaultExtension) => NextSavePath;

        public string? PickOpenFile(string filter) => NextOpenPath;

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
            new RangeAnalysisService(),
            new JsonManualMetadataStore(Path.Combine(directory, "metadata.json")),
            new JsonLibraryStore(Path.Combine(directory, "library.json")),
            dialogs);
        return (viewModel, settings, themes, dialogs, directory);
    }

    private static string WriteCapture(
        string directory,
        string fileName,
        double frameTime = 10.0,
        int seconds = 6,
        Func<int, double>? frameTimeBySecond = null)
    {
        var csvPath = Path.Combine(directory, fileName);
        var rows = string.Concat(
            Enumerable.Range(0, seconds).SelectMany(second =>
                new[] { 0.0, 0.25, 0.5 }.Select(offset =>
                {
                    var time = frameTimeBySecond?.Invoke(second) ?? frameTime;
                    return $"{second + offset},{time},80\n";
                })));
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

    [Fact]
    public async Task Analyze_full_capture_requests_a_full_zoom()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv");
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            Assert.Null(dialogs.LastError);
            Assert.True(viewModel.Chart.HasData);
            var listener = Listen(viewModel);

            viewModel.AnalyzeFullCaptureCommand.Execute(null);

            Assert.True(listener.Invoked);
            Assert.Null(listener.Requested);
            Assert.Null(dialogs.LastInfo);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Analyze_worst_region_jumps_to_the_worst_ten_seconds()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            // 14 s at a constant 100 FPS; the 1 s edge trim leaves bins 1-12,
            // and the chart X axis is rebased to the window start: range (0, 9).
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv", seconds: 14);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            var listener = Listen(viewModel);

            viewModel.AnalyzeWorstRegionCommand.Execute(null);

            Assert.True(listener.Invoked);
            Assert.Equal(new TimeRange(0.0, 9.0), listener.Requested);
            Assert.Null(dialogs.LastInfo);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Analyze_stable_region_jumps_to_the_stablest_ten_seconds()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv", seconds: 14);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            var listener = Listen(viewModel);

            viewModel.AnalyzeStableRegionCommand.Execute(null);

            Assert.True(listener.Invoked);
            Assert.Equal(new TimeRange(0.0, 9.0), listener.Requested);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Analyze_largest_drop_jumps_to_the_drop_region()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            // 100 FPS for seconds 0-6, then 50 FPS; trim leaves bins 1-12 and
            // the drop lands at bin 7 (X = 6 on the rebased axis): range (0, 6).
            dialogs.NextCsvPath = WriteCapture(
                directory,
                "FrameView_Test_Log.csv",
                seconds: 14,
                frameTimeBySecond: second => second < 7 ? 10.0 : 20.0);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            var listener = Listen(viewModel);

            viewModel.AnalyzeLargestDropCommand.Execute(null);

            Assert.True(listener.Invoked);
            Assert.Equal(new TimeRange(0.0, 6.0), listener.Requested);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Analyze_largest_drop_without_a_drop_shows_info()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv", seconds: 14);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            var listener = Listen(viewModel);

            viewModel.AnalyzeLargestDropCommand.Execute(null);

            Assert.False(listener.Invoked);
            Assert.NotNull(dialogs.LastInfo);
            Assert.Contains("No meaningful performance drop", dialogs.LastInfo);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Analyze_without_direction_shows_info()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv", seconds: 14);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            viewModel.Chart.SelectedMetric = viewModel.Chart.Metrics.Single(
                metric => metric.Id == "gpu0_util");
            var listener = Listen(viewModel);

            viewModel.AnalyzeWorstRegionCommand.Execute(null);

            Assert.False(listener.Invoked);
            Assert.NotNull(dialogs.LastInfo);
            Assert.Contains("no defined performance direction", dialogs.LastInfo);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Analyze_ab_difference_requires_a_comparison()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv", seconds: 14);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            var listener = Listen(viewModel);

            viewModel.AnalyzeLargestAbDifferenceCommand.Execute(null);

            Assert.False(listener.Invoked);
            Assert.NotNull(dialogs.LastInfo);
            Assert.Contains("Load a comparison session", dialogs.LastInfo);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Analyze_ab_difference_jumps_to_the_diverging_region()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Base_Log.csv", seconds: 14);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            dialogs.NextCsvPath = WriteCapture(
                directory, "FrameView_Comp_Log.csv", frameTime: 20.0, seconds: 14);
            await viewModel.LoadComparisonCommand.ExecuteAsync(null);
            var listener = Listen(viewModel);

            viewModel.AnalyzeLargestAbDifferenceCommand.Execute(null);

            Assert.True(listener.Invoked);
            Assert.Equal(new TimeRange(0.0, 9.0), listener.Requested);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Analyze_without_data_is_a_no_op()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        Cleanup(directory);
        var listener = Listen(viewModel);

        viewModel.AnalyzeFullCaptureCommand.Execute(null);
        viewModel.AnalyzeWorstRegionCommand.Execute(null);
        viewModel.AnalyzeLargestAbDifferenceCommand.Execute(null);

        Assert.False(listener.Invoked);
        Assert.Null(dialogs.LastInfo);
    }

    [Fact]
    public async Task Has_comparison_follows_the_comparison_session()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            Assert.False(viewModel.HasComparison);

            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Base_Log.csv");
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            Assert.False(viewModel.HasComparison);

            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Comp_Log.csv", frameTime: 20.0);
            await viewModel.LoadComparisonCommand.ExecuteAsync(null);
            Assert.True(viewModel.HasComparison);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Analyze_worst_region_uses_base_points_when_comparing()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            // Base: constant 100 FPS (worst region (0, 9) on the rebased axis).
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Base_Log.csv", seconds: 14);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            // Comparison: 100 FPS for 0-6 s then 50 FPS; its own worst region
            // would be (2, 11), so this proves the command uses Base points.
            dialogs.NextCsvPath = WriteCapture(
                directory,
                "FrameView_Comp_Log.csv",
                seconds: 14,
                frameTimeBySecond: second => second < 7 ? 10.0 : 20.0);
            await viewModel.LoadComparisonCommand.ExecuteAsync(null);
            var listener = Listen(viewModel);

            viewModel.AnalyzeWorstRegionCommand.Execute(null);

            Assert.True(listener.Invoked);
            Assert.Equal(new TimeRange(0.0, 9.0), listener.Requested);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Analyze_still_works_after_removing_a_session()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Base_Log.csv", seconds: 14);
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Comp_Log.csv", seconds: 14);
            await viewModel.LoadComparisonCommand.ExecuteAsync(null);
            viewModel.RemoveComparisonCommand.Execute(null);
            var listener = Listen(viewModel);

            viewModel.AnalyzeStableRegionCommand.Execute(null);

            Assert.True(listener.Invoked);
            Assert.Equal(new TimeRange(0.0, 9.0), listener.Requested);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private sealed class AnalyzeListener
    {
        public bool Invoked { get; set; }

        public TimeRange? Requested { get; set; }
    }

    private static AnalyzeListener Listen(MainWindowViewModel viewModel)
    {
        var listener = new AnalyzeListener();
        viewModel.AnalyzeRangeRequested += (_, range) =>
        {
            listener.Invoked = true;
            listener.Requested = range;
        };
        return listener;
    }

    [Fact]
    public async Task Edit_metadata_requests_the_editor_with_stored_values()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv");
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            var session = viewModel.BaseSession!;
            viewModel.PersistMetadata(session, new ManualMetadata(Game: "Stored Run"));
            MainWindowViewModel.MetadataEditorRequest? request = null;
            viewModel.MetadataEditorRequested += (_, value) => request = value;

            viewModel.EditBaseMetadataCommand.Execute(null);

            Assert.NotNull(request);
            Assert.Equal(session, request!.Session);
            Assert.Equal("Stored Run", request.Current.Game);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Edit_metadata_without_a_session_is_a_no_op()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        Cleanup(directory);
        var requested = false;
        viewModel.MetadataEditorRequested += (_, _) => requested = true;

        viewModel.EditBaseMetadataCommand.Execute(null);
        viewModel.EditComparisonMetadataCommand.Execute(null);

        Assert.False(requested);
        Assert.Null(dialogs.LastInfo);
    }

    [Fact]
    public async Task Persisting_metadata_updates_the_card_name_and_config_line()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv");
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            var session = viewModel.BaseSession!;

            viewModel.PersistMetadata(
                session,
                new ManualMetadata(
                    BenchmarkName: "RTX Run",
                    Resolution: "4K",
                    GraphicsPreset: "Ultra",
                    Tags: ["gpu"]));

            Assert.Equal("RTX Run", viewModel.BaseSessionName);
            Assert.Equal("4K · Ultra", viewModel.BaseMetaLine);
            Assert.Contains("METADATA SAVED", viewModel.StatusText);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Empty_metadata_removes_the_entry_and_restores_detected_lines()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv");
            await viewModel.LoadBaseCommand.ExecuteAsync(null);
            var session = viewModel.BaseSession!;
            viewModel.PersistMetadata(session, new ManualMetadata(BenchmarkName: "Temporary"));

            viewModel.PersistMetadata(session, new ManualMetadata());

            Assert.Equal("Test", viewModel.BaseSessionName);
            Assert.DoesNotContain("Temporary", viewModel.BaseMetaLine);
            Assert.Contains("METADATA SAVED", viewModel.StatusText);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Has_base_session_follows_the_base_session()
    {
        var (viewModel, _, _, dialogs, directory) = Create();
        try
        {
            Assert.False(viewModel.HasBaseSession);

            dialogs.NextCsvPath = WriteCapture(directory, "FrameView_Test_Log.csv");
            await viewModel.LoadBaseCommand.ExecuteAsync(null);

            Assert.True(viewModel.HasBaseSession);

            viewModel.RemoveBaseCommand.Execute(null);

            Assert.False(viewModel.HasBaseSession);
        }
        finally
        {
            Cleanup(directory);
        }
    }
}
