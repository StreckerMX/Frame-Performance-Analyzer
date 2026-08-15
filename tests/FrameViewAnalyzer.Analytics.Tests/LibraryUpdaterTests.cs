using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

public class LibraryUpdaterTests
{
    private static readonly CaptureInfo Capture = new(
        "C:/captures/FrameView_2026_01_02T033633_Log.csv",
        "FrameView_2026_01_02T033633_Log.csv",
        "Game.exe",
        "1920x1080",
        "RTX 4080",
        "Ryzen 7",
        120.5);

    [Fact]
    public void New_records_carry_detected_context_and_timestamps()
    {
        var record = LibraryUpdater.NewRecord(Capture, "id-1", "2026-01-02T00:00:00Z");

        Assert.Equal("id-1", record.Identity);
        Assert.Equal("Game.exe", record.Game);
        Assert.Equal("1920x1080", record.Resolution);
        Assert.Equal(120.5, record.DurationSeconds);
        Assert.True(record.Available);
        Assert.Equal("2026-01-02T00:00:00Z", record.AddedAt);
        Assert.Equal(record.AddedAt, record.LastSeenAt);
    }

    [Fact]
    public void Upsert_merges_context_and_keeps_the_original_added_at()
    {
        var library = new LibraryModel();
        LibraryUpdater.Upsert(library, Capture, "id-1", "2026-01-01T00:00:00Z");

        var updated = LibraryUpdater.Upsert(
            library,
            Capture with { Resolution = "3840x2160" },
            "id-1",
            "2026-01-02T00:00:00Z");

        Assert.Equal("3840x2160", updated.Resolution);
        Assert.Equal("2026-01-01T00:00:00Z", updated.AddedAt);
        Assert.Equal("2026-01-02T00:00:00Z", updated.LastSeenAt);
        Assert.Single(library.Records);
    }

    [Fact]
    public void Comparisons_are_newest_first_deduplicated_and_capped()
    {
        var recent = new List<(string, string)>
        {
            ("a", "b"),
            ("c", "d"),
            ("e", "f"),
            ("g", "h"),
            ("i", "j"),
        };

        var updated = LibraryUpdater.WithComparison(recent, "c", "d");

        Assert.Equal(("c", "d"), updated[0]);
        Assert.Equal(5, updated.Count);
        Assert.Equal(1, updated.Count(pair => pair == ("c", "d")));
    }

    [Fact]
    public void Stats_digest_caches_average_and_lows_from_a_session()
    {
        var library = new LibraryModel();
        var record = LibraryUpdater.Upsert(library, Capture, "id-1", "now");
        var session = new CaptureAnalysisService().Analyze(
            TestCapture.MakeSession(seconds: 6));

        LibraryUpdater.UpdateStats(library, session, "id-1");

        var stats = library.Records["id-1"].StatsSummary;
        Assert.Contains("avg_fps", stats.Keys);
        Assert.Contains("p1_fps", stats.Keys);
        Assert.Contains("p01_fps", stats.Keys);
        Assert.Equal(100.0, stats["avg_fps"], precision: 9);
    }
}
