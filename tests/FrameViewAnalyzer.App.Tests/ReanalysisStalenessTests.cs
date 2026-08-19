using System.IO;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.Analytics.Samples;
using FrameViewAnalyzer.App.Busy;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Stale-result protection for overlapping re-analysis requests: a newer
/// user-requested analysis state must always win, even when an older
/// overlapping request completes afterwards. The first Reanalyze call is
/// blocked deterministically (no timing assumptions) until the newer request
/// has applied its results.
/// </summary>
public class ReanalysisStalenessTests
{
    private sealed class ControllableAnalysisService : ICaptureAnalysisService
    {
        private readonly CaptureAnalysisService _inner = new();
        private readonly TaskCompletionSource _firstStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reanalyzeCount;

        public Task WaitUntilFirstReanalyzeStartsAsync() => _firstStarted.Task;

        public void ReleaseFirstReanalyze() => _releaseFirst.TrySetResult();

        public SessionAnalysis Analyze(CaptureData capture, AnalysisOptions? options = null) =>
            _inner.Analyze(capture, options);

        public SessionAnalysis Reanalyze(SessionAnalysis previous, AnalysisOptions options)
        {
            if (Interlocked.Increment(ref _reanalyzeCount) == 1)
            {
                _firstStarted.TrySetResult();
                // Hold the first request open until the newer one has applied.
                _releaseFirst.Task.GetAwaiter().GetResult();
            }

            return _inner.Reanalyze(previous, options);
        }

        public double ComputeAutoGpuThreshold(ParsedSamples samples) =>
            _inner.ComputeAutoGpuThreshold(samples);
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public SettingsDocument Current { get; set; } = new();

        public SettingsDocument Load() => Current;

        public void Save(SettingsDocument settings) => Current = settings;
    }

    private sealed class FakeThemeService : IThemeService
    {
        public string Current => "dark";

        public event EventHandler? Changed;

        public void Apply(string mode) => Changed?.Invoke(this, EventArgs.Empty);
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

    private static (
        MainWindowViewModel ViewModel,
        ControllableAnalysisService Analysis,
        BusyState Busy,
        string Directory) Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var analysis = new ControllableAnalysisService();
        var reader = new FrameViewCsvReader();
        var viewModel = new MainWindowViewModel(
            new FakeSettingsStore(),
            new FakeThemeService(),
            new ChartViewModel(),
            reader,
            analysis,
            new RangeAnalysisService(),
            new JsonManualMetadataStore(Path.Combine(directory, "metadata.json")),
            new JsonLibraryStore(Path.Combine(directory, "library.json")),
            new CaptureFolderScanner(reader),
            new FakeDialogService(),
            new BusyState());
        return (viewModel, analysis, viewModel.Busy, directory);
    }

    private static string WriteCapture(string directory, string fileName, double frameTime = 10.0)
    {
        var path = Path.Combine(directory, fileName);
        var rows = string.Concat(
            Enumerable.Range(0, 8).SelectMany(second =>
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
            // Best-effort cleanup.
        }
    }

    private static AnalysisOptions Options(double gpuThreshold) =>
        new(
            GpuThreshold: gpuThreshold,
            TrimBufferSeconds: 0.0,
            AutoGpuThreshold: false,
            ExcludeTransitions: false);

    [Fact]
    public async Task Overlapping_pair_reanalysis_never_applies_stale_options()
    {
        var (viewModel, analysis, busy, directory) = Create();
        try
        {
            await viewModel.LoadBaseFromPathAsync(WriteCapture(directory, "FrameView_Test_Log.csv"));

            var older = viewModel.ApplyAnalysisOptionsAsync(Options(20.0));
            await analysis.WaitUntilFirstReanalyzeStartsAsync();

            // The newer request completes and applies while the older one is
            // still computing.
            await viewModel.ApplyAnalysisOptionsAsync(Options(40.0));
            Assert.Equal(40.0, viewModel.BaseSession!.EffectiveOptions.GpuThreshold);

            // The older request finishes afterwards: its result must be
            // discarded instead of overwriting the newer state.
            analysis.ReleaseFirstReanalyze();
            await older;

            Assert.Equal(40.0, viewModel.BaseSession.EffectiveOptions.GpuThreshold);
            Assert.Contains("REANALYZED", viewModel.StatusText);
            Assert.False(busy.IsBusy);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Overlapping_multi_reanalysis_never_applies_stale_options()
    {
        var (viewModel, analysis, busy, directory) = Create();
        try
        {
            await viewModel.LoadMultiBenchmarksAsync(
            [
                WriteCapture(directory, "FrameView_A_Log.csv", 10.0),
                WriteCapture(directory, "FrameView_B_Log.csv", 12.0),
                WriteCapture(directory, "FrameView_C_Log.csv", 15.0),
            ]);
            Assert.True(viewModel.IsMultiMode);

            var older = viewModel.ApplyMultiAnalysisOptionsAsync(Options(30.0));
            await analysis.WaitUntilFirstReanalyzeStartsAsync();

            await viewModel.ApplyMultiAnalysisOptionsAsync(Options(60.0));
            Assert.All(viewModel.MultiSessions, item =>
                Assert.Equal(60.0, item.Session.EffectiveOptions.GpuThreshold));

            analysis.ReleaseFirstReanalyze();
            await older;

            Assert.All(viewModel.MultiSessions, item =>
                Assert.Equal(60.0, item.Session.EffectiveOptions.GpuThreshold));
            Assert.Contains("REANALYZED", viewModel.StatusText);
            Assert.False(busy.IsBusy);
        }
        finally
        {
            Cleanup(directory);
        }
    }
}
