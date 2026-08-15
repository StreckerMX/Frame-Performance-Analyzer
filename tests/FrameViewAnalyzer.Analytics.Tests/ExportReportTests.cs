using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

public class ExportReportTests
{
    private static SessionAnalysis SessionOf(
        string application = "Game.exe",
        string resolution = "3840x2160",
        string displayName = "FrameView_2026_01_02T033633_Log")
    {
        var capture = new CaptureData
        {
            Path = "C:/captures/FrameView_2026_01_02T033633_Log.csv",
            DisplayName = displayName,
            Kind = CsvKind.Log,
            Headers =
            [
                "TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)",
                "Application", "Resolution", "GPU Base Driver",
            ],
            Columns =
            [
                ["0.0", "0.25", "0.5", "0.75", "1.0", "1.25"],
                ["10.0", "10.0", "10.0", "10.0", "10.0", "10.0"],
                ["80.0", "80.0", "80.0", "80.0", "80.0", "80.0"],
                [application, application, application, application, application, application],
                [resolution, resolution, resolution, resolution, resolution, resolution],
                ["560.70", "560.70", "560.70", "560.70", "560.70", "560.70"],
            ],
        };
        return new CaptureAnalysisService().Analyze(capture);
    }

    [Fact]
    public void Report_metrics_start_with_fps_and_include_the_visible_metric()
    {
        var session = SessionOf();
        var ids = ExportReport.SelectReportMetricIds(session.Catalog, "gpu0_util");

        Assert.Equal("fps", ids[0]);
        Assert.Contains("gpu0_util", ids);
        Assert.True(ids.Count <= ExportReport.MaxReportMetrics);
    }

    [Fact]
    public void File_stem_is_sanitized_and_metric_scoped()
    {
        var session = SessionOf(application: "My Game_!!.exe");

        var stem = ExportReport.BuildFileStem(session, ["fps", "frametime"]);

        Assert.Equal("FrameView_My_Game_fps_frametime", stem);
    }

    [Fact]
    public void File_stem_falls_back_to_the_unnamed_benchmark_name()
    {
        var session = SessionOf(application: "--", displayName: "");

        var stem = ExportReport.BuildFileStem(session, []);

        Assert.Equal("FrameView_Unnamed_benchmark_chart", stem);
    }

    [Fact]
    public void Session_label_combines_game_and_resolution()
    {
        Assert.Equal("Game — 3840x2160", ExportReport.SessionExportLabel(SessionOf()));
    }

    [Fact]
    public void Statistics_rows_cover_the_metric_union()
    {
        var baseSession = SessionOf();

        var rows = ExportReport.BuildStatisticsRows(baseSession);

        Assert.NotEmpty(rows);
        Assert.Contains(rows, row => row.MetricId == "fps" && row.StatisticKey == "avg");
        Assert.All(rows, row => Assert.Equal("FrameView_2026_01_02T033633_Log", row.BaseSession));
    }

    [Fact]
    public void Statistics_payload_embeds_sessions_and_manual_metadata()
    {
        var baseSession = SessionOf();
        var manual = new ManualMetadata(BenchmarkName: "RTX Run");

        var payload = ExportReport.BuildStatisticsPayload(baseSession, null, manual);

        Assert.Equal("1", payload.FormatVersion);
        Assert.Single(payload.Sessions);
        Assert.Equal("base", payload.Sessions[0].Role);
        Assert.Equal("RTX Run", payload.Sessions[0].ManualMetadata!.BenchmarkName);
    }

    [Fact]
    public void Benchmark_package_carries_records_sorted_by_source_name()
    {
        var library = new LibraryModel();
        library.Records["b"] = new LibraryRecord(
            "b", "C:/b.csv", "b.csv", "Game B", "1080p", "RTX 4070", "Ryzen 5",
            30.0, "now", "now");
        library.Records["a"] = new LibraryRecord(
            "a", "C:/a.csv", "a.csv", "Game A", "4K", "RTX 5090", "Ryzen 7",
            60.0, "now", "now",
            StatsSummary: new Dictionary<string, double> { ["avg_fps"] = 100.0 });

        var package = ExportReport.BuildBenchmarkPackage(library);

        Assert.Equal(ExportReport.PackageVersion, package.PackageVersion);
        Assert.Equal(["a", "b"], package.Captures.Select(capture => capture.Identity));
        Assert.Equal("4K", package.Captures[0].Detected.Resolution);
    }
}
