using System.IO;
using System.Windows;
using System.Windows.Threading;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.App.Busy;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Exports;
using FrameViewAnalyzer.Infrastructure.Legacy;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Busy integration of the main-window view model: commands respect the busy
/// state, failed and successful loads always restore READY, and the View
/// Details preparation owns the busy state only until the child window is
/// built (MainWindow then returns to READY before the dialog opens).
/// </summary>
public class MainWindowViewModelBusyTests
{
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

    private sealed class FakePlacement : IWindowPlacementService
    {
        public void Restore(Window window)
        {
        }

        public void Save(Window window)
        {
        }
    }

    private sealed class ThrowingReader : IFrameViewCsvReader
    {
        public CsvKind DetectKind(IReadOnlyList<string> headers, string fileName) => CsvKind.Unknown;

        public Task<CaptureData> LoadCaptureAsync(string path, CancellationToken cancellationToken = default) =>
            throw new IOException("Synthetic read failure.");

        public Task<CaptureInfo?> ReadCaptureInfoAsync(string path, CancellationToken cancellationToken = default) =>
            throw new IOException("Synthetic read failure.");
    }

    private static (MainWindowViewModel ViewModel, FakeDialogService Dialogs, BusyState Busy, string Directory)
        Create(IFrameViewCsvReader? reader = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-busy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var busy = new BusyState();
        var dialogs = new FakeDialogService();
        var resolvedReader = reader ?? new FrameViewCsvReader();
        var viewModel = new MainWindowViewModel(
            new FakeSettingsStore(),
            new FakeThemeService(),
            new ChartViewModel(),
            resolvedReader,
            new CaptureAnalysisService(),
            new RangeAnalysisService(),
            new JsonManualMetadataStore(Path.Combine(directory, "metadata.json")),
            new JsonLibraryStore(Path.Combine(directory, "library.json")),
            new CaptureFolderScanner(resolvedReader),
            dialogs,
            busy);
        return (viewModel, dialogs, busy, directory);
    }

    private static string WriteCapture(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        var rows = string.Concat(
            Enumerable.Range(0, 6).SelectMany(second =>
                new[] { 0.0, 0.25, 0.5 }.Select(offset =>
                    $"{second + offset},10.0,80\n")));
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

    [Fact]
    public void Load_commands_cannot_execute_while_busy()
    {
        var (viewModel, _, busy, directory) = Create();
        Cleanup(directory);

        using (busy.Begin("Loading benchmark library"))
        {
            Assert.False(viewModel.LoadBaseCommand.CanExecute(null));
            Assert.False(viewModel.LoadComparisonCommand.CanExecute(null));
            Assert.False(viewModel.RefreshCapturesCommand.CanExecute(null));
        }

        Assert.True(viewModel.LoadBaseCommand.CanExecute(null));
        Assert.True(viewModel.LoadComparisonCommand.CanExecute(null));
        Assert.True(viewModel.RefreshCapturesCommand.CanExecute(null));
    }

    [Fact]
    public async Task Failed_load_restores_ready()
    {
        var (viewModel, dialogs, busy, directory) = Create(new ThrowingReader());
        try
        {
            await viewModel.LoadBaseFromPathAsync("Z:/missing/file.csv");

            Assert.False(busy.IsBusy);
            Assert.False(busy.IsBusyVisible);
            Assert.Null(busy.OperationText);
            Assert.NotNull(dialogs.LastError);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Successful_load_restores_ready_with_the_usual_status()
    {
        var (viewModel, dialogs, busy, directory) = Create();
        try
        {
            var path = WriteCapture(directory, "FrameView_Test_Log.csv");

            await viewModel.LoadBaseFromPathAsync(path);

            Assert.False(busy.IsBusy);
            Assert.False(busy.IsBusyVisible);
            Assert.Null(busy.OperationText);
            Assert.Null(dialogs.LastError);
            Assert.Contains("CAPTURE OPENED", viewModel.StatusText);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void View_details_preparation_busies_then_returns_to_ready() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var (viewModel, _, busy, directory) = Create();
            try
            {
                var capturePath = WriteCapture(directory, "FrameView_Test_Log.csv");
                var session = new CaptureAnalysisService().Analyze(
                    new FrameViewCsvReader().LoadCaptureAsync(capturePath).GetAwaiter().GetResult());
                var dialogs = new FakeDialogService();
                var themes = new FakeThemeService();
                var window = new FrameViewAnalyzer.App.MainWindow(
                    viewModel,
                    new FakePlacement(),
                    themes,
                    new JsonLibraryStore(Path.Combine(directory, "library.json")),
                    new JsonManualMetadataStore(Path.Combine(directory, "metadata.json")),
                    new CaptureFolderScanner(new FrameViewCsvReader()),
                    new FakeSettingsStore(),
                    new LegacyDataImporter(
                        new JsonSettingsStore(Path.Combine(directory, "settings.json")),
                        new JsonManualMetadataStore(Path.Combine(directory, "metadata.json")),
                        new JsonLibraryStore(Path.Combine(directory, "library.json")),
                        directory,
                        Path.Combine(directory, "settings.json")),
                    new ExportService(),
                    dialogs,
                    new FrameViewCsvReader(),
                    new CaptureAnalysisService(),
                    busy);
                try
                {
                    // Preparation starts immediately: MainWindow owns the busy
                    // state while the details view model is being built.
                    var task = window.PrepareDetailsWindowAsync(session);
                    Assert.True(busy.IsBusy);

                    PumpUntil(() => task.IsCompleted, TimeSpan.FromSeconds(20));

                    // As soon as the child window is ready to open, MainWindow
                    // returns to READY — ownership transfers with the window.
                    var details = task.Result;
                    Assert.False(busy.IsBusy);
                    Assert.False(busy.IsBusyVisible);
                    var detailsViewModel = Assert.IsType<SessionDetailsViewModel>(details.DataContext);
                    Assert.NotEmpty(detailsViewModel.Sections);
                    details.Close();
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                Cleanup(directory);
            }
        });

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var deadline = DateTime.UtcNow + timeout;
        var frame = new DispatcherFrame();
        void Tick()
        {
            if (condition() || DateTime.UtcNow > deadline)
            {
                frame.Continue = false;
                return;
            }

            dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)Tick);
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)Tick);
        Dispatcher.PushFrame(frame);
    }
}
