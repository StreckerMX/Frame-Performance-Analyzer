using System.IO;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.Analytics.Samples;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Tests;

public class MultiAnalysisRangeTests
{
    private sealed class FakeSettingsStore : ISettingsStore
    {
        public SettingsDocument Current { get; set; } = new();

        public SettingsDocument Load() => Current;

        public void Save(SettingsDocument settings) => Current = settings;
    }

    private sealed class FakeThemeService : IThemeService
    {
        public string Current { get; private set; } = "dark";

        public event EventHandler? Changed;

        public void Apply(string mode)
        {
            Current = mode;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public string? LastError { get; private set; }

        public string? PickCsvFile(string? initialDirectory) => null;

        public string? PickSaveFile(string? initialFile, string filter, string defaultExtension) => null;

        public string? PickOpenFile(string filter) => null;

        public string? PickFolder(string? initialDirectory) => null;

        public void ShowError(string title, string message) => LastError = $"{title}: {message}";

        public void ShowInfo(string title, string message)
        {
        }
    }

    private sealed class ThrowingAnalysisService : ICaptureAnalysisService
    {
        private readonly CaptureAnalysisService _inner = new();

        public int ReanalyzeCalls { get; private set; }

        public int ThrowOnReanalyzeCall { get; set; }

        public SessionAnalysis Analyze(CaptureData capture, AnalysisOptions? options = null) =>
            _inner.Analyze(capture, options);

        public SessionAnalysis Reanalyze(SessionAnalysis previous, AnalysisOptions options)
        {
            ReanalyzeCalls++;
            if (ThrowOnReanalyzeCall > 0 && ReanalyzeCalls == ThrowOnReanalyzeCall)
            {
                throw new InvalidOperationException("Synthetic Multi reanalysis failure.");
            }

            return _inner.Reanalyze(previous, options);
        }

        public double ComputeAutoGpuThreshold(ParsedSamples samples) =>
            _inner.ComputeAutoGpuThreshold(samples);
    }

    [Fact]
    public async Task Multi_range_controls_reanalyze_every_peer_with_one_shared_snapshot()
    {
        var setup = Create();
        try
        {
            var paths = new[]
            {
                WriteCapture(setup.Directory, "FrameView_A_Log.csv", 10.0),
                WriteCapture(setup.Directory, "FrameView_B_Log.csv", 12.0),
                WriteCapture(setup.Directory, "FrameView_C_Log.csv", 15.0),
            };

            await setup.ViewModel.LoadMultiBenchmarksAsync(paths);

            Assert.True(setup.ViewModel.IsMultiMode);
            Assert.True(setup.ViewModel.AnalysisRange.IsEnabled);
            Assert.Contains("Applied to all 3 benchmarks", setup.ViewModel.AnalysisRange.AnalysisSummaryText);

            setup.ViewModel.AnalysisRange.AutoGpuThresholdEnabled = false;
            setup.ViewModel.AnalysisRange.GpuThreshold = 35.0;
            setup.ViewModel.AnalysisRange.TrimBufferSeconds = 0.0;
            setup.ViewModel.AnalysisRange.ExcludeTransitionsEnabled = false;
            setup.ViewModel.AnalysisRange.ApplyNow();

            Assert.Equal(3, setup.Analysis.ReanalyzeCalls);
            Assert.Equal(3, setup.ViewModel.MultiSessions.Count);
            Assert.Equal(3, setup.ViewModel.Chart.SeriesList.Count);
            Assert.All(setup.ViewModel.MultiSessions, item =>
            {
                Assert.False(item.Session.EffectiveOptions.AutoGpuThreshold);
                Assert.Equal(35.0, item.Session.EffectiveOptions.GpuThreshold);
                Assert.Equal(0.0, item.Session.EffectiveOptions.TrimBufferSeconds);
                Assert.False(item.Session.EffectiveOptions.ExcludeTransitions);
            });
            Assert.Contains("REANALYZED", setup.ViewModel.StatusText);
            Assert.Contains("Applied to all 3 benchmarks", setup.ViewModel.AnalysisRange.AnalysisSummaryText);
        }
        finally
        {
            Cleanup(setup.Directory);
        }
    }

    [Fact]
    public async Task Failed_multi_reanalysis_keeps_every_previous_session_transactionally()
    {
        var setup = Create();
        try
        {
            var paths = new[]
            {
                WriteCapture(setup.Directory, "FrameView_A_Log.csv", 10.0),
                WriteCapture(setup.Directory, "FrameView_B_Log.csv", 12.0),
                WriteCapture(setup.Directory, "FrameView_C_Log.csv", 15.0),
            };

            await setup.ViewModel.LoadMultiBenchmarksAsync(paths);
            var previous = setup.ViewModel.MultiSessions.Select(item => item.Session).ToArray();
            var previousThreshold = previous[0].EffectiveOptions.GpuThreshold;
            setup.Analysis.ThrowOnReanalyzeCall = 2;

            setup.ViewModel.AnalysisRange.AutoGpuThresholdEnabled = false;
            setup.ViewModel.AnalysisRange.GpuThreshold = 47.0;
            setup.ViewModel.AnalysisRange.ApplyNow();

            Assert.Equal(2, setup.Analysis.ReanalyzeCalls);
            Assert.Equal(previous.Length, setup.ViewModel.MultiSessions.Count);
            for (var index = 0; index < previous.Length; index++)
            {
                Assert.Same(previous[index], setup.ViewModel.MultiSessions[index].Session);
            }

            Assert.Equal(previousThreshold, setup.ViewModel.AnalysisRange.GpuThreshold);
            Assert.Contains("Previous workspace kept", setup.ViewModel.StatusText);
            Assert.NotNull(setup.Dialogs.LastError);
            Assert.Contains("Multi analysis error", setup.Dialogs.LastError);
        }
        finally
        {
            Cleanup(setup.Directory);
        }
    }

    private static TestSetup Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-multi-range-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settings = new FakeSettingsStore
        {
            Current = new SettingsDocument(CaptureDirectory: directory, AppearanceMode: "dark"),
        };
        var dialogs = new FakeDialogService();
        var reader = new FrameViewCsvReader();
        var analysis = new ThrowingAnalysisService();
        var viewModel = new MainWindowViewModel(
            settings,
            new FakeThemeService(),
            new ChartViewModel(),
            reader,
            analysis,
            new RangeAnalysisService(),
            new JsonManualMetadataStore(Path.Combine(directory, "metadata.json")),
            new JsonLibraryStore(Path.Combine(directory, "library.json")),
            new CaptureFolderScanner(reader),
            dialogs);
        return new TestSetup(viewModel, analysis, dialogs, directory);
    }

    private static string WriteCapture(string directory, string fileName, double frameTime)
    {
        var path = Path.Combine(directory, fileName);
        var rows = string.Concat(
            Enumerable.Range(0, 10).SelectMany(second =>
                new[] { 0.0, 0.25, 0.5 }.Select(offset =>
                    $"{second + offset},{frameTime},80\n")));
        File.WriteAllText(path, "TimeInSeconds,MsBetweenPresents,GPU0Util(%)\n" + rows);
        return path;
    }

    private static void Cleanup(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed record TestSetup(
        MainWindowViewModel ViewModel,
        ThrowingAnalysisService Analysis,
        FakeDialogService Dialogs,
        string Directory);
}
