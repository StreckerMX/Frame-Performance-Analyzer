using System.IO;
using System.Text;
using System.Text.Json;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Exports;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class ExportServiceTests
{
    private readonly ExportService _service = new();

    private static string TempPath(string name) =>
        Path.Combine(Path.GetTempPath(), "fva-exp-" + Guid.NewGuid().ToString("N"), name);

    private static IReadOnlyList<ComparisonRow> SampleRows() =>
    [
        new ComparisonRow(
            "fps", "FPS (Calculated)", "Performance", "FPS", "avg", "Average",
            "base", 100.0, "comparison", 90.0, -10.0, -10.0, ImprovementKind.Regression),
    ];

    private static SessionAnalysis SessionOf()
    {
        var capture = new CaptureData
        {
            Path = "C:/captures/FrameView_2026_01_02T033633_Log.csv",
            DisplayName = "FrameView_2026_01_02T033633_Log",
            Kind = CsvKind.Log,
            Headers = ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            Columns =
            [
                ["0.0", "0.25", "0.5", "0.75", "1.0", "1.25"],
                ["10.0", "10.0", "10.0", "10.0", "10.0", "10.0"],
                ["80.0", "80.0", "80.0", "80.0", "80.0", "80.0"],
            ],
        };
        return new CaptureAnalysisService().Analyze(capture);
    }

    [Fact]
    public void Statistics_csv_has_a_utf8_bom_header_and_rows()
    {
        var path = TempPath("stats.csv");
        try
        {
            var count = _service.WriteStatisticsCsv(path, SampleRows());

            Assert.Equal(1, count);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);
            var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            Assert.StartsWith("metric_id,metric,category", text);
            Assert.Contains("fps,FPS (Calculated)", text);
            Assert.Contains("-10", text);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Statistics_json_embeds_sessions_statistics_and_manual_metadata()
    {
        var path = TempPath("stats.json");
        try
        {
            var document = ExportReport.BuildStatisticsPayload(
                SessionOf(),
                null,
                new ManualMetadata(BenchmarkName: "RTX Run"));

            _service.WriteStatisticsJson(path, document);

            using var parsed = JsonDocument.Parse(File.ReadAllText(path));
            var root = parsed.RootElement;
            Assert.Equal("1", root.GetProperty("format_version").GetString());
            Assert.Equal(1, root.GetProperty("sessions").GetArrayLength());
            Assert.Equal(
                "RTX Run",
                root.GetProperty("sessions")[0]
                    .GetProperty("manual_metadata")
                    .GetProperty("benchmark_name")
                    .GetString());
            Assert.True(root.GetProperty("statistics").GetArrayLength() > 0);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static string PackageJson(string identity = "id1", string game = "GTA V") =>
        $$"""
        {
          "package_version": 1,
          "exported_at": "2026-01-02T00:00:00Z",
          "captures": [
            {
              "identity": "{{identity}}",
              "source_path": "C:/captures/a.csv",
              "source_name": "a.csv",
              "source_available": true,
              "detected": {"game": "{{game}}", "resolution": "4K", "gpu": "RTX 5090", "cpu": "Ryzen 7", "duration_seconds": 60.0},
              "manual_metadata": {"benchmark_name": "Night Run", "tags": ["gpu"]},
              "stats_summary": {"avg_fps": 100.0, "p1_fps": 80.0}
            }
          ]
        }
        """;

    [Fact]
    public void Package_export_import_round_trips_records_and_manual_metadata()
    {
        var library = new LibraryModel();
        var result = _service.ImportBenchmarkPackage(library, PackageJson());

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.True(library.Records.ContainsKey("id1"));
        var record = library.Records["id1"];
        Assert.Equal("GTA V", record.Game);
        Assert.Equal(100.0, record.StatsSummary["avg_fps"]);
        Assert.Equal("Night Run", result.ManualMetadataByIdentity["id1"].BenchmarkName);
    }

    [Fact]
    public void Validation_reports_missing_fields_with_python_messages()
    {
        var json = """
        {
          "package_version": 1,
          "captures": [
            {"source_name": "a.csv", "detected": {"game": "GTA V", "resolution": ""}, "stats_summary": {"avg_fps": 100.0}}
          ]
        }
        """;

        var result = _service.ValidateBenchmarkPackage(json);

        Assert.Empty(result.Valid);
        Assert.Contains("Capture 0 (a.csv): missing resolution.", result.Errors);
    }

    [Fact]
    public void Unsupported_package_version_is_reported()
    {
        var result = _service.ValidateBenchmarkPackage("""{"package_version": 99, "captures": []}""");

        Assert.Equal(["Unsupported or missing package_version."], result.Errors);
    }

    [Fact]
    public void Missing_identity_imports_under_an_imported_key()
    {
        var library = new LibraryModel();

        var result = _service.ImportBenchmarkPackage(library, PackageJson(identity: ""));

        Assert.Equal(1, result.Imported);
        Assert.True(library.Records.ContainsKey("imported:a.csv"));
    }
}
