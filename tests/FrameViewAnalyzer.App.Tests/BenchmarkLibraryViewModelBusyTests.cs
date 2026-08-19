using System.IO;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.App.Busy;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Busy integration of the Benchmark Library view model: the Library owns
/// its own busy state, its initial load runs inside a "Loading benchmark
/// library..." scope that always returns to READY, and its row actions are
/// guarded while busy.
/// </summary>
public class BenchmarkLibraryViewModelBusyTests
{
    private static (BenchmarkLibraryViewModel ViewModel, BusyState Busy, string Directory) Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-libbusy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new JsonLibraryStore(Path.Combine(directory, "library.json"));
        var manualStore = new JsonManualMetadataStore(Path.Combine(directory, "metadata.json"));
        var busy = new BusyState();
        var viewModel = new BenchmarkLibraryViewModel(
            store,
            manualStore,
            new CaptureFolderScanner(new FrameViewCsvReader()),
            captureDirectory: null,
            busy);
        return (viewModel, busy, directory);
    }

    private static void Seed(
        string directory,
        params (string Id, string Game, string Resolution, string Gpu)[] records)
    {
        var store = new JsonLibraryStore(Path.Combine(directory, "library.json"));
        var library = new LibraryModel();
        foreach (var (id, game, resolution, gpu) in records)
        {
            library.Records[id] = new LibraryRecord(
                id,
                $"C:/captures/{id}.csv",
                "FrameView_2026_01_02T033633_Log.csv",
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
    public async Task Refresh_wraps_the_load_and_returns_to_ready()
    {
        var (viewModel, busy, directory) = Create();
        try
        {
            Seed(directory, ("a", "GTA5", "1920x1080", "RTX 4090"));
            var busyTransitions = new List<bool>();
            var operations = new List<string?>();
            busy.BusyChanged += (_, _) =>
            {
                busyTransitions.Add(busy.IsBusy);
                operations.Add(busy.OperationText);
            };

            await viewModel.RefreshAsync();

            Assert.False(busy.IsBusy);
            Assert.False(busy.IsBusyVisible);
            Assert.Equal([true, false], busyTransitions);
            Assert.Contains("Loading benchmark library", operations[0]);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Row_commands_cannot_execute_while_busy()
    {
        var (viewModel, busy, directory) = Create();
        try
        {
            Seed(
                directory,
                ("a", "GTA5", "1920x1080", "RTX 4090"),
                ("b", "Cyber", "3840x2160", "RTX 5090"));
            await viewModel.RefreshAsync();
            viewModel.ToggleSelectedCommand.Execute(viewModel.Rows[0]);
            viewModel.ToggleSelectedCommand.Execute(viewModel.Rows[1]);
            Assert.True(viewModel.CanCompareSelectedNow);

            using (busy.Begin("Importing benchmark package..."))
            {
                Assert.False(viewModel.LoadBaseCommand.CanExecute(viewModel.Rows[0]));
                Assert.False(viewModel.LoadComparisonCommand.CanExecute(viewModel.Rows[0]));
                Assert.False(viewModel.ComparePairCommand.CanExecute(null));
                Assert.False(viewModel.CompareSelectedCommand.CanExecute(null));
                Assert.False(viewModel.CanCompareSelectedNow);
                Assert.True(viewModel.IsBusy);
            }

            Assert.True(viewModel.LoadBaseCommand.CanExecute(viewModel.Rows[0]));
            Assert.True(viewModel.CanCompareSelectedNow);
            Assert.False(viewModel.IsBusy);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Nested_refresh_inside_import_keeps_the_library_busy()
    {
        var (viewModel, busy, directory) = Create();
        try
        {
            Seed(directory, ("a", "GTA5", "1920x1080", "RTX 4090"));

            // Mimics ImportPackage_Click: an outer import scope whose inner
            // refresh must not return the window to READY early.
            using (busy.Begin("Importing benchmark package..."))
            {
                Assert.True(busy.IsBusy);
                await viewModel.RefreshAsync();
                Assert.True(busy.IsBusy);
                Assert.Equal("Importing benchmark package...", busy.OperationText);
            }

            Assert.False(busy.IsBusy);
        }
        finally
        {
            Cleanup(directory);
        }
    }
}
