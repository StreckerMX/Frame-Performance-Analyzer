using System.IO;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.App.Busy;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Tests;

public sealed class QuickCaptureMetadataRegressionTests
{
    private sealed class FakeSettingsStore : ISettingsStore
    {
        public SettingsDocument Current { get; set; } = new();

        public SettingsDocument Load() => Current;

        public void Save(SettingsDocument settings) => Current = settings;
    }

    private sealed class FakeThemeService : IThemeService
    {
        public event EventHandler? Changed;

        public void Apply(string mode) => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeDialogService : IDialogService
    {
        public string? PickCsvFile(string? initialDirectory) => null;

        public string? PickSaveFile(string? initialFile, string filter, string defaultExtension) => null;

        public string? PickOpenFile(string filter) => null;

        public string? PickFolder(string? initialDirectory) => null;

        public void ShowError(string title, string message) =>
            throw new InvalidOperationException($"Unexpected dialog error: {title}: {message}");

        public void ShowInfo(string title, string message)
        {
        }
    }

    [Fact]
    public async Task Selected_quick_capture_metadata_refreshes_without_reloading_the_capture()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "fva-quick-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var settings = new FakeSettingsStore
            {
                Current = new SettingsDocument(CaptureDirectory: directory),
            };
            var reader = new FrameViewCsvReader();
            using var busy = new BusyState();
            var viewModel = new MainWindowViewModel(
                settings,
                new FakeThemeService(),
                new ChartViewModel(),
                reader,
                new CaptureAnalysisService(),
                new RangeAnalysisService(),
                new JsonManualMetadataStore(Path.Combine(directory, "metadata.json")),
                new JsonLibraryStore(Path.Combine(directory, "library.json")),
                new CaptureFolderScanner(reader),
                new FakeDialogService(),
                busy);

            var path = WriteCapture(directory, "FrameView_GTA5_Enhanced_Log.csv");
            await viewModel.ReloadCaptureFolderAsync();
            var quickCapture = Assert.Single(viewModel.Captures);

            await viewModel.LoadBaseFromPathAsync(path);
            var originalSession = Assert.IsType<SessionAnalysis>(viewModel.BaseSession);

            // Establish the toolbar selection without intentionally loading the
            // same capture a second time. The regression under test begins only
            // after the selection is already active and the VM is idle.
            using (busy.Begin("Test selection guard"))
            {
                viewModel.SelectedCapture = quickCapture;
            }

            Assert.False(busy.IsBusy);
            Assert.Equal("GTA5_Enhanced", viewModel.SelectedCapture?.Display);

            var busyTransitions = 0;
            busy.BusyChanged += (_, _) => busyTransitions++;

            viewModel.PersistMetadata(
                originalSession,
                new ManualMetadata(BenchmarkName: "Ultra 4K run"));

            Assert.False(busy.IsBusy);
            Assert.Equal(0, busyTransitions);
            Assert.Same(originalSession, viewModel.BaseSession);
            Assert.Equal(
                "Ultra 4K run · GTA5_Enhanced",
                viewModel.SelectedCapture?.Display);
            Assert.Equal(
                "Ultra 4K run · GTA5_Enhanced",
                Assert.Single(viewModel.Captures).Display);

            viewModel.PersistMetadata(originalSession, new ManualMetadata());

            Assert.False(busy.IsBusy);
            Assert.Equal(0, busyTransitions);
            Assert.Same(originalSession, viewModel.BaseSession);
            Assert.Equal("GTA5_Enhanced", viewModel.SelectedCapture?.Display);
            Assert.Equal("GTA5_Enhanced", Assert.Single(viewModel.Captures).Display);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string WriteCapture(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        var rows = string.Concat(
            Enumerable.Range(0, 6).SelectMany(second =>
                new[] { 0.0, 0.25, 0.5 }.Select(offset =>
                    $"{second + offset},10,80\n")));
        File.WriteAllText(
            path,
            "TimeInSeconds,MsBetweenPresents,GPU0Util(%)\n" + rows);
        return path;
    }
}
