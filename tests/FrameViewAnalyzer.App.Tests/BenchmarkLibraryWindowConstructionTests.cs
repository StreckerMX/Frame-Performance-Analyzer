using System.IO;
using System.Windows;
using System.Windows.Threading;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Exports;
using FrameViewAnalyzer.Infrastructure.Legacy;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage that actually CONSTRUCTS the real Benchmark Library
/// window (InitializeComponent included) and drives the real
/// Loaded → RefreshAsync → ObservableCollection rebuild pipeline. The Library
/// button crash escaped helper-level tests because they never bound the view
/// model collections to WPF controls and never exercised the asynchronous
/// capture-folder scan continuation on the UI thread.
/// All scenarios run on the shared STA test host so the test Application, its
/// theme resources, and every window share one dispatcher thread safely.
/// </summary>
public class BenchmarkLibraryWindowConstructionTests
{
    private sealed record Fixture(string Root, string LibraryPath, string MetadataPath, string SettingsPath);

    private static Fixture CreateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "fva-libwin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new Fixture(
            root,
            Path.Combine(root, "library.json"),
            Path.Combine(root, "metadata.json"),
            Path.Combine(root, "settings.json"));
    }

    private static void SeedLibrary(
        string libraryPath,
        params (string Id, string Game, string Resolution, string Gpu)[] records)
    {
        var store = new JsonLibraryStore(libraryPath);
        var library = new LibraryModel();
        foreach (var (id, game, resolution, gpu) in records)
        {
            library.Records[id] = new LibraryRecord(
                id,
                $"C:/captures/{id}.csv",
                $"FrameView_{game}_2026_01_01T000000_Log.csv",
                game,
                resolution,
                gpu,
                "Ryzen 7",
                60.0,
                "2026-01-01T00:00:00Z",
                "2026-01-02T00:00:00Z");
        }

        store.Save(library);
    }

    /// <summary>
    /// Writes one syntactically valid FrameView capture into a subfolder and
    /// returns the folder, so the Library's capture-folder scan has real
    /// asynchronous file work to await (the crash continuation path).
    /// </summary>
    private static string WriteCapture(string fixtureRoot)
    {
        var folder = Path.Combine(fixtureRoot, "captures");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "FrameView_TestGame.exe_2026_08_15T120000_Log.csv");
        File.WriteAllText(
            path,
            "TimeInSeconds,MsBetweenPresents,Application,Resolution,GPU,CPU\n"
            + "0.0,16.6,TestGame,1920x1080,RTX 4090,Ryzen 7\n"
            + "0.5,16.6,TestGame,1920x1080,RTX 4090,Ryzen 7\n"
            + "1.0,16.6,TestGame,1920x1080,RTX 4090,Ryzen 7\n");
        return folder;
    }

    private static BenchmarkLibraryWindow CreateWindow(Fixture fixture, string? captureDirectory)
    {
        var libraryStore = new JsonLibraryStore(fixture.LibraryPath);
        var manualStore = new JsonManualMetadataStore(fixture.MetadataPath);
        return new BenchmarkLibraryWindow(
            libraryStore,
            manualStore,
            new CaptureFolderScanner(new FrameViewCsvReader()),
            new LegacyDataImporter(
                new JsonSettingsStore(fixture.SettingsPath),
                manualStore,
                libraryStore,
                fixture.Root,
                fixture.SettingsPath),
            new ExportService(),
            new DialogService(),
            new FrameViewCsvReader(),
            new CaptureAnalysisService(),
            captureDirectory);
    }

    private static BenchmarkLibraryViewModel ViewModelOf(BenchmarkLibraryWindow window) =>
        (BenchmarkLibraryViewModel)window.DataContext;

    /// <summary>Pumps the dispatcher until the condition holds or the timeout expires.</summary>
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

    private static void Cleanup(Fixture fixture)
    {
        try
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void Library_window_constructs_and_refreshes_with_a_populated_library() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var fixture = CreateFixture();
            try
            {
                SeedLibrary(
                    fixture.LibraryPath,
                    ("a", "GTA5", "1920x1080", "RTX 4090"),
                    ("b", "Cyber", "3840x2160", "RTX 5090"));
                var captureDirectory = WriteCapture(fixture.Root);
                var window = CreateWindow(fixture, captureDirectory);
                try
                {
                    Assert.Equal("Benchmark Library", window.Title);
                    window.Show();
                    Assert.True(window.IsVisible, "Library window must open.");

                    var viewModel = ViewModelOf(window);
                    PumpUntil(
                        () => viewModel.CountText == "3 record(s)",
                        TimeSpan.FromSeconds(20));

                    // Two seeded records plus the capture discovered by the
                    // asynchronous folder scan — the exact continuation path
                    // that crashed before the thread-affinity fix.
                    Assert.Equal("3 record(s)", viewModel.CountText);
                    Assert.Equal(3, viewModel.Rows.Count);
                    Assert.Equal(4, viewModel.GameOptions.Count);
                    Assert.Equal(BenchmarkLibraryViewModel.AllValue, viewModel.GameOptions[0]);
                    Assert.Contains(viewModel.GameOptions, game => game == "GTA5");
                    Assert.Contains(viewModel.GameOptions, game => game == "TestGame");
                    Assert.Equal(BenchmarkLibraryViewModel.AllValue, viewModel.SelectedGame);
                    Assert.Equal(BenchmarkLibraryViewModel.AllValue, viewModel.SelectedResolution);
                    Assert.Equal(BenchmarkLibraryViewModel.AllValue, viewModel.SelectedGpu);
                }
                finally
                {
                    window.Close();
                    Assert.False(window.IsVisible, "Library window must close cleanly.");
                }
            }
            finally
            {
                Cleanup(fixture);
            }
        });

    [Fact]
    public void Library_window_opens_with_a_completely_empty_library() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var fixture = CreateFixture();
            try
            {
                // No persistence files at all; the capture scan still finds
                // the one capture in the folder.
                var captureDirectory = WriteCapture(fixture.Root);
                var window = CreateWindow(fixture, captureDirectory);
                try
                {
                    window.Show();
                    var viewModel = ViewModelOf(window);
                    PumpUntil(
                        () => viewModel.CountText == "1 record(s)",
                        TimeSpan.FromSeconds(20));

                    Assert.Equal("1 record(s)", viewModel.CountText);
                    Assert.Single(viewModel.Rows);
                    Assert.Equal(2, viewModel.GameOptions.Count);
                    Assert.Contains(viewModel.GameOptions, game => game == "TestGame");
                    Assert.Equal(BenchmarkLibraryViewModel.AllValue, viewModel.SelectedGame);
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                Cleanup(fixture);
            }
        });

    [Fact]
    public void Library_window_opens_when_the_persistence_file_does_not_exist_yet() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var fixture = CreateFixture();
            try
            {
                // First run: no library.json, no capture directory, no loaded
                // sessions anywhere in the app.
                Assert.False(File.Exists(fixture.LibraryPath));
                var window = CreateWindow(fixture, captureDirectory: null);
                try
                {
                    window.Show();
                    var viewModel = ViewModelOf(window);
                    PumpUntil(
                        () => viewModel.CountText == "0 record(s)",
                        TimeSpan.FromSeconds(20));

                    Assert.Equal("0 record(s)", viewModel.CountText);
                    Assert.Empty(viewModel.Rows);
                    Assert.Single(viewModel.GameOptions);
                    Assert.Equal(BenchmarkLibraryViewModel.AllValue, viewModel.SelectedGame);
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                Cleanup(fixture);
            }
        });

    [Fact]
    public void Library_window_opens_with_a_zero_entry_persistence_file() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var fixture = CreateFixture();
            try
            {
                SeedLibrary(fixture.LibraryPath); // zero records persisted
                Assert.True(File.Exists(fixture.LibraryPath));
                var window = CreateWindow(fixture, captureDirectory: null);
                try
                {
                    window.Show();
                    var viewModel = ViewModelOf(window);
                    PumpUntil(
                        () => viewModel.CountText == "0 record(s)",
                        TimeSpan.FromSeconds(20));

                    Assert.Equal("0 record(s)", viewModel.CountText);
                    Assert.Empty(viewModel.Rows);
                    Assert.Empty(viewModel.RecentPairs);
                    Assert.Equal(BenchmarkLibraryViewModel.AllValue, viewModel.SelectedGame);
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                Cleanup(fixture);
            }
        });
}
