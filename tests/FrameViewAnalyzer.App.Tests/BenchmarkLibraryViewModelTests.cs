using System.IO;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Tests;

public class BenchmarkLibraryViewModelTests
{
    private static (BenchmarkLibraryViewModel ViewModel, string Directory) Create(
        string? captureDirectory = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new JsonLibraryStore(Path.Combine(directory, "library.json"));
        var manualStore = new JsonManualMetadataStore(Path.Combine(directory, "metadata.json"));
        var viewModel = new BenchmarkLibraryViewModel(
            store,
            manualStore,
            new CaptureFolderScanner(new FrameViewCsvReader()),
            captureDirectory);
        return (viewModel, directory);
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

    private static void Seed(
        string storePath,
        IReadOnlyDictionary<string, ManualMetadata> manual,
        params (string Id, string Game, string Resolution, string Gpu)[] records)
    {
        var store = new JsonLibraryStore(storePath);
        var library = new LibraryModel();
        var counter = records.Length;
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
                $"2026-01-0{counter}T00:00:00Z");
            counter--;
        }

        library.RecentComparisons.Add(("a", "b"));
        store.Save(library);
        if (manual.Count > 0)
        {
            var manualStore = new JsonManualMetadataStore(
                Path.Combine(Path.GetDirectoryName(storePath)!, "metadata.json"));
            foreach (var (identity, metadata) in manual)
            {
                manualStore.Set(identity, metadata);
            }
        }
    }

    [Fact]
    public async Task Refresh_populates_rows_in_date_order()
    {
        var (viewModel, directory) = Create();
        try
        {
            Seed(
                Path.Combine(directory, "library.json"),
                new Dictionary<string, ManualMetadata>(),
                ("a", "GTA5", "1920x1080", "RTX 4090"),
                ("b", "Cyber", "3840x2160", "RTX 5090"));

            await viewModel.RefreshAsync();

            Assert.Equal(2, viewModel.Rows.Count);
            Assert.Equal("GTA5", viewModel.Rows[0].Title);
            Assert.Equal("2 record(s)", viewModel.CountText);
            Assert.Single(viewModel.RecentPairs);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Search_filters_the_rows()
    {
        var (viewModel, directory) = Create();
        try
        {
            Seed(
                Path.Combine(directory, "library.json"),
                new Dictionary<string, ManualMetadata>(),
                ("a", "GTA5", "1920x1080", "RTX 4090"),
                ("b", "Cyber", "3840x2160", "RTX 5090"));

            await viewModel.RefreshAsync();
            viewModel.SearchText = "cyber";

            Assert.Single(viewModel.Rows);
            Assert.Equal("Cyber", viewModel.Rows[0].Title);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Option_combos_reflect_the_indexed_records()
    {
        var (viewModel, directory) = Create();
        try
        {
            Seed(
                Path.Combine(directory, "library.json"),
                new Dictionary<string, ManualMetadata>(),
                ("a", "GTA5", "1920x1080", "RTX 4090"),
                ("b", "Cyber", "3840x2160", "RTX 5090"));

            await viewModel.RefreshAsync();

            Assert.Equal(["All", "Cyber", "GTA5"], viewModel.GameOptions);
            Assert.Equal(["All", "1920x1080", "3840x2160"], viewModel.ResolutionOptions);
            Assert.Equal(["All", "RTX 4090", "RTX 5090"], viewModel.GpuOptions);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Ab_selection_compares_two_captures_and_clears()
    {
        var (viewModel, directory) = Create();
        try
        {
            Seed(
                Path.Combine(directory, "library.json"),
                new Dictionary<string, ManualMetadata>(),
                ("a", "GTA5", "1920x1080", "RTX 4090"),
                ("b", "Cyber", "3840x2160", "RTX 5090"));
            await viewModel.RefreshAsync();
            (string First, string Second)? compare = null;
            viewModel.CompareRequested += (first, second) => compare = (first, second);

            viewModel.ToggleAbCommand.Execute(viewModel.Rows[0]);
            Assert.True(viewModel.HasAbSelection);

            viewModel.ToggleAbCommand.Execute(viewModel.Rows[1]);

            Assert.NotNull(compare);
            Assert.EndsWith("a.csv", compare!.Value.First);
            Assert.EndsWith("b.csv", compare.Value.Second);
            Assert.False(viewModel.HasAbSelection);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Load_commands_forward_available_paths_only()
    {
        var (viewModel, directory) = Create();
        try
        {
            Seed(
                Path.Combine(directory, "library.json"),
                new Dictionary<string, ManualMetadata>(),
                ("a", "GTA5", "1920x1080", "RTX 4090"));
            await viewModel.RefreshAsync();
            string? basePath = null;
            string? comparisonPath = null;
            viewModel.LoadBaseRequested += path => basePath = path;
            viewModel.LoadComparisonRequested += path => comparisonPath = path;

            viewModel.LoadBaseCommand.Execute(viewModel.Rows[0]);
            viewModel.LoadComparisonCommand.Execute(viewModel.Rows[0]);

            Assert.NotNull(basePath);
            Assert.NotNull(comparisonPath);
        }
        finally
        {
            Cleanup(directory);
        }
    }
}
