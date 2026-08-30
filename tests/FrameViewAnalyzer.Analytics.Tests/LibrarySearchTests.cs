using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

/// <summary>
/// Search / filter / sort / display parity for the Benchmark Library,
/// ported from the Python library tests.
/// </summary>
public class LibrarySearchTests
{
    private static LibraryRecord Record(
        string identity,
        string game,
        string resolution = "1920x1080",
        string gpu = "RTX 4090",
        string lastSeen = "2026-01-02T00:00:00Z",
        string sourceName = "FrameView_2026_01_02T033633_Log.csv",
        string? cpu = "Ryzen 7") =>
        new(
            identity,
            $"C:/captures/{sourceName}",
            sourceName,
            game,
            resolution,
            gpu,
            cpu ?? string.Empty,
            42.0,
            "2026-01-01T00:00:00Z",
            lastSeen);

    private static readonly LibraryRecord Gta = Record("a", "GTA5 Enhanced");
    private static readonly LibraryRecord Cyber = Record(
        "b", "Cyberpunk 2077", "3840x2160", "RTX 5090", "2026-01-05T00:00:00Z");
    private static readonly LibraryRecord Old = Record(
        "c", "Older Game", "1280x720", "RTX 3060", "2025-12-01T00:00:00Z");

    private static readonly Dictionary<string, ManualMetadata> Manual = new(StringComparer.Ordinal)
    {
        ["a"] = new ManualMetadata(
            BenchmarkName: "Night Run",
            GraphicsPreset: "Ultra",
            Upscaler: "DLSS",
            Tags: ["night", "gpu"]),
    };

    [Fact]
    public void Search_matches_record_text_case_insensitively()
    {
        Assert.Equal(["a"], LibrarySearch.SearchRecords([Gta, Cyber], "gta5").Select(r => r.Identity));
        Assert.Equal(["b"], LibrarySearch.SearchRecords([Gta, Cyber], "RTX 5090").Select(r => r.Identity));
        Assert.Equal(3, LibrarySearch.SearchRecords([Gta, Cyber, Old], "").Count);
    }

    [Fact]
    public void Search_includes_manual_metadata_text()
    {
        Assert.Equal(
            ["a"],
            LibrarySearch.SearchRecords([Gta, Cyber], "Night", Manual).Select(r => r.Identity));
        Assert.Equal(
            ["a"],
            LibrarySearch.SearchRecords([Gta, Cyber], "dlss", Manual).Select(r => r.Identity));
    }

    [Fact]
    public void Filters_are_and_style()
    {
        var byGame = LibrarySearch.FilterRecords([Gta, Cyber], game: "cyber");
        Assert.Equal(["b"], byGame.Select(r => r.Identity));

        var byGpu = LibrarySearch.FilterRecords([Gta, Cyber, Old], gpu: "RTX 50");
        Assert.Equal(["b"], byGpu.Select(r => r.Identity));

        var byResolution = LibrarySearch.FilterRecords([Gta, Cyber, Old], resolution: "3840");
        Assert.Equal(["b"], byResolution.Select(r => r.Identity));

        var byTags = LibrarySearch.FilterRecords([Gta, Cyber], Manual, tags: ["night", "gpu"]);
        Assert.Equal(["a"], byTags.Select(r => r.Identity));

        var missingTag = LibrarySearch.FilterRecords([Gta, Cyber], Manual, tags: ["night", "cpu"]);
        Assert.Empty(missingTag);
    }

    [Fact]
    public void Sort_by_date_is_newest_first_and_by_name_is_stable()
    {
        Assert.Equal(
            ["b", "a", "c"],
            LibrarySearch.SortRecords([Old, Gta, Cyber]).Select(r => r.Identity));

        Assert.Equal(
            ["b", "a", "c"],
            LibrarySearch.SortRecords([Old, Gta, Cyber], LibraryConstants.SortName)
                .Select(r => r.Identity));
    }

    [Fact]
    public void Row_title_and_subtitle_prefer_manual_context()
    {
        var manual = Manual["a"];

        Assert.Equal("Night Run", LibrarySearch.LibraryRowTitle(Gta, manual));
        Assert.Equal("GTA5 Enhanced", LibrarySearch.LibraryRowTitle(Gta));
        Assert.Equal("Ultra · DLSS  ·  1920x1080  ·  RTX 4090  ·  Ryzen 7  ·  42s", LibrarySearch.LibraryRowSubtitle(Gta, manual));
    }

    [Theory]
    [InlineData(42.0, "42s")]
    [InlineData(106.0, "1min 46s")]
    [InlineData(455.0, "7min 35s")]
    [InlineData(3769.0, "1h 2min")]
    [InlineData(7200.0, "2h")]
    public void Row_subtitle_formats_capture_duration_for_people(double seconds, string expected)
    {
        var subtitle = LibrarySearch.LibraryRowSubtitle(Gta with { DurationSeconds = seconds });

        Assert.EndsWith(expected, subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void Stamp_reads_the_frameview_file_name()
    {
        Assert.Equal("2026-01-02 03:36", LibrarySearch.LibraryStamp(Gta));
        Assert.Equal(
            string.Empty,
            LibrarySearch.LibraryStamp(Gta with { SourceName = "unrelated.csv" }));
    }

    [Fact]
    public void Library_game_prefers_the_manual_scene()
    {
        Assert.Equal("GTA5 Enhanced", LibrarySearch.LibraryGame(Gta, Manual["a"]));
        Assert.Equal("Scene 2", LibrarySearch.LibraryGame(Gta, new ManualMetadata(Game: "Scene 2")));
        Assert.Equal("GTA5 Enhanced", LibrarySearch.LibraryGame(Gta));
    }
}
