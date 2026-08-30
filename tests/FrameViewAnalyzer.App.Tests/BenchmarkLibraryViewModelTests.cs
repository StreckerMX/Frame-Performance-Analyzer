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
        string? captureDirectory = null,
        BenchmarkBrowserMode mode = BenchmarkBrowserMode.Library,
        IReadOnlyList<string>? initiallySelectedPaths = null,
        string? excludedSelectionPath = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new JsonLibraryStore(Path.Combine(directory, "library.json"));
        var manualStore = new JsonManualMetadataStore(Path.Combine(directory, "metadata.json"));
        var viewModel = new BenchmarkLibraryViewModel(
            store,
            manualStore,
            new CaptureFolderScanner(new FrameViewCsvReader()),
            captureDirectory,
            mode: mode,
            initiallySelectedPaths: initiallySelectedPaths,
            excludedSelectionPath: excludedSelectionPath);
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
                $"2026-01-0{Math.Min(counter, 9)}T00:00:00Z");
            counter--;
        }

        if (library.Records.ContainsKey("a") && library.Records.ContainsKey("b"))
        {
            library.RecentComparisons.Add(("a", "b"));
        }
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
    public async Task Multi_selection_forwards_checked_captures_in_selection_order()
    {
        var (viewModel, directory) = Create();
        try
        {
            Seed(
                Path.Combine(directory, "library.json"),
                new Dictionary<string, ManualMetadata>(),
                ("a", "GTA5", "1920x1080", "RTX 4090"),
                ("b", "Cyber", "3840x2160", "RTX 5090"),
                ("c", "Helldivers", "2560x1440", "RTX 4080"));
            await viewModel.RefreshAsync();
            IReadOnlyList<string>? requested = null;
            viewModel.CompareSelectedRequested += paths => requested = paths;

            var cyber = Assert.Single(viewModel.Rows, row => row.Record.Identity == "b");
            var gta = Assert.Single(viewModel.Rows, row => row.Record.Identity == "a");
            viewModel.ToggleSelectedCommand.Execute(cyber);
            viewModel.ToggleSelectedCommand.Execute(gta);

            Assert.Equal(2, viewModel.SelectedCount);
            Assert.True(viewModel.CanCompareSelected);
            Assert.True(Assert.Single(viewModel.Rows, row => row.Record.Identity == "a").IsSelected);
            Assert.True(Assert.Single(viewModel.Rows, row => row.Record.Identity == "b").IsSelected);

            viewModel.CompareSelectedCommand.Execute(null);

            Assert.NotNull(requested);
            Assert.Equal(["C:/captures/b.csv", "C:/captures/a.csv"], requested);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Pair_browser_keeps_exactly_one_selection_and_confirms_the_requested_slot()
    {
        var (viewModel, directory) = Create(mode: BenchmarkBrowserMode.PairComparison);
        try
        {
            Seed(
                Path.Combine(directory, "library.json"),
                new Dictionary<string, ManualMetadata>(),
                ("a", "GTA5", "1920x1080", "RTX 4090"),
                ("b", "Cyber", "3840x2160", "RTX 5090"));
            await viewModel.RefreshAsync();
            BenchmarkBrowserMode? requestedMode = null;
            IReadOnlyList<string>? requestedPaths = null;
            viewModel.SelectionConfirmedRequested += (mode, paths) =>
            {
                requestedMode = mode;
                requestedPaths = paths;
            };

            var first = Assert.Single(viewModel.Rows, row => row.Record.Identity == "a");
            var second = Assert.Single(viewModel.Rows, row => row.Record.Identity == "b");
            viewModel.ToggleSelectedCommand.Execute(first);
            viewModel.ToggleSelectedCommand.Execute(second);

            Assert.Equal(BenchmarkBrowserMode.PairComparison, viewModel.Mode);
            Assert.True(viewModel.IsPairSelectionMode);
            Assert.Equal("Select comparison benchmark", viewModel.WindowTitle);
            Assert.Equal("Load as Comparison", viewModel.PrimaryActionText);
            Assert.Equal(1, viewModel.SelectedCount);
            Assert.False(Assert.Single(viewModel.Rows, row => row.Record.Identity == "a").IsSelected);
            Assert.True(Assert.Single(viewModel.Rows, row => row.Record.Identity == "b").IsSelected);
            Assert.True(viewModel.CanConfirmSelectionNow);

            viewModel.ConfirmSelectionCommand.Execute(null);

            Assert.Equal(BenchmarkBrowserMode.PairComparison, requestedMode);
            Assert.Equal(["C:/captures/b.csv"], requestedPaths);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Comparison_browser_marks_the_current_base_and_never_selects_it()
    {
        var (viewModel, directory) = Create(
            mode: BenchmarkBrowserMode.PairComparison,
            excludedSelectionPath: "c:\\captures\\a.csv");
        try
        {
            Seed(
                Path.Combine(directory, "library.json"),
                new Dictionary<string, ManualMetadata>(),
                ("a", "GTA5", "1920x1080", "RTX 4090"),
                ("b", "Cyber", "3840x2160", "RTX 5090"));
            await viewModel.RefreshAsync();

            var currentBase = Assert.Single(viewModel.Rows, row => row.Record.Identity == "a");
            var comparison = Assert.Single(viewModel.Rows, row => row.Record.Identity == "b");

            Assert.True(viewModel.IsComparisonSelectionMode);
            Assert.True(currentBase.IsCurrentBase);
            Assert.False(currentBase.Selectable);
            viewModel.ToggleSelectedCommand.Execute(currentBase);
            Assert.Equal(0, viewModel.SelectedCount);

            viewModel.ToggleSelectedCommand.Execute(comparison);
            Assert.True(comparison.Selectable);
            Assert.Equal(1, viewModel.SelectedCount);
            Assert.True(viewModel.CanConfirmSelectionNow);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Changing_source_folder_updates_the_readout_and_indexes_that_folder()
    {
        var (viewModel, directory) = Create();
        try
        {
            var captures = Path.Combine(directory, "captures");
            Directory.CreateDirectory(captures);
            File.WriteAllText(
                Path.Combine(captures, "FrameView_TestGame.exe_2026_08_15T120000_Log.csv"),
                "TimeInSeconds,MsBetweenPresents,Application,Resolution,GPU,CPU\n"
                + "0.0,16.6,TestGame,1920x1080,RTX 4090,Ryzen 7\n"
                + "0.5,16.6,TestGame,1920x1080,RTX 4090,Ryzen 7\n"
                + "1.0,16.6,TestGame,1920x1080,RTX 4090,Ryzen 7\n");

            await viewModel.ChangeCaptureFolderAsync(captures);

            Assert.Equal(captures, viewModel.CaptureFolder);
            Assert.Single(viewModel.Rows);
            Assert.Equal("TestGame", viewModel.Rows[0].Title);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Multi_browser_restores_existing_paths_and_confirms_two_to_eight_peers()
    {
        var selected = new[] { "C:/captures/b.csv", "C:/captures/a.csv" };
        var (viewModel, directory) = Create(
            mode: BenchmarkBrowserMode.Multi,
            initiallySelectedPaths: selected);
        try
        {
            Seed(
                Path.Combine(directory, "library.json"),
                new Dictionary<string, ManualMetadata>(),
                ("a", "GTA5", "1920x1080", "RTX 4090"),
                ("b", "Cyber", "3840x2160", "RTX 5090"),
                ("c", "Helldivers", "2560x1440", "RTX 4080"));
            await viewModel.RefreshAsync();
            IReadOnlyList<string>? requestedPaths = null;
            viewModel.SelectionConfirmedRequested += (mode, paths) =>
            {
                Assert.Equal(BenchmarkBrowserMode.Multi, mode);
                requestedPaths = paths;
            };

            Assert.True(viewModel.IsMultiSelectionMode);
            Assert.Equal("Select benchmarks", viewModel.WindowTitle);
            Assert.Equal("Load selected", viewModel.PrimaryActionText);
            Assert.Equal(2, viewModel.SelectedCount);
            Assert.True(viewModel.CanConfirmSelectionNow);
            Assert.Equal(2, viewModel.Rows.Count(row => row.IsSelected));

            viewModel.ConfirmSelectionCommand.Execute(null);

            Assert.NotNull(requestedPaths);
            Assert.Equal(selected, requestedPaths!.ToArray());
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Multi_selection_is_capped_at_eight_and_survives_filtering()
    {
        var (viewModel, directory) = Create();
        try
        {
            var records = Enumerable.Range(0, 9)
                .Select(index => ($"id{index}", $"Game {index}", "2560x1440", "RTX 4090"))
                .ToArray();
            Seed(
                Path.Combine(directory, "library.json"),
                new Dictionary<string, ManualMetadata>(),
                records);
            await viewModel.RefreshAsync();

            foreach (var row in viewModel.Rows.ToList())
            {
                viewModel.ToggleSelectedCommand.Execute(row);
            }

            Assert.Equal(BenchmarkLibraryViewModel.MaxMultiSelection, viewModel.SelectedCount);
            Assert.True(viewModel.CanCompareSelected);
            Assert.Contains("maximum", viewModel.SelectionSummary, StringComparison.OrdinalIgnoreCase);

            viewModel.SearchText = "Game 0";
            Assert.Single(viewModel.Rows);
            Assert.Equal(BenchmarkLibraryViewModel.MaxMultiSelection, viewModel.SelectedCount);

            viewModel.SearchText = string.Empty;
            Assert.Equal(BenchmarkLibraryViewModel.MaxMultiSelection, viewModel.Rows.Count(row => row.IsSelected));
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

    [Fact]
    public async Task Remove_from_library_persists_ignore_prunes_recent_and_selection()
    {
        var (viewModel, directory) = Create();
        try
        {
            var storePath = Path.Combine(directory, "library.json");
            Seed(
                storePath,
                new Dictionary<string, ManualMetadata>(),
                ("a", "GTA5", "1920x1080", "RTX 4090"),
                ("b", "Cyber", "3840x2160", "RTX 5090"));
            await viewModel.RefreshAsync();
            var row = Assert.Single(viewModel.Rows, item => item.Record.Identity == "a");
            viewModel.ToggleSelectedCommand.Execute(row);
            Assert.Equal(1, viewModel.SelectedCount);

            row = Assert.Single(viewModel.Rows, item => item.Record.Identity == "a");
            viewModel.RemoveFromLibrary(row);

            Assert.Single(viewModel.Rows);
            Assert.DoesNotContain(viewModel.Rows, item => item.Record.Identity == "a");
            Assert.Empty(viewModel.RecentPairs);
            Assert.Equal(0, viewModel.SelectedCount);

            var persisted = new JsonLibraryStore(storePath).Load();
            Assert.Contains("a", persisted.IgnoredIdentities);
            Assert.DoesNotContain("a", persisted.Records.Keys);
            Assert.Empty(persisted.RecentComparisons);
        }
        finally
        {
            Cleanup(directory);
        }
    }
}
