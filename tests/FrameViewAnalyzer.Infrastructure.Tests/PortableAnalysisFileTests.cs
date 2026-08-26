using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Exports;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class PortableAnalysisFileTests
{
    [Fact]
    public void Json_round_trip_preserves_range_series_and_manual_metadata()
    {
        var path = TemporaryPath("json");
        try
        {
            var expected = Document();

            PortableAnalysisFile.WriteJson(path, expected);
            var actual = PortableAnalysisFile.ReadJson(path);

            Assert.Equal(PortableAnalysisExport.FormatVersion, actual.FormatVersion);
            Assert.Equal("pair", actual.WorkspaceMode);
            Assert.NotNull(actual.Range);
            Assert.Equal(12.5, actual.Range!.StartSeconds, precision: 6);
            Assert.Equal(15.5, actual.Range.EndSeconds, precision: 6);
            Assert.Single(actual.Sessions);
            Assert.Equal("Test run", actual.Sessions[0].Name);
            Assert.Equal(new ManualMetadata(BenchmarkName: "Test run", Game: "Example"),
                actual.Sessions[0].ManualMetadata);
            var fps = Assert.Single(actual.Sessions[0].Series);
            Assert.Equal("fps", fps.MetricId);
            Assert.Equal([12.5, 13.5, 14.5, 15.5], fps.TimeSeconds);
            Assert.Equal([100.0, 105.0, 98.0, 110.0], fps.Values);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Csv_round_trip_preserves_range_series_and_workspace_identity()
    {
        var path = TemporaryPath("csv");
        try
        {
            var expected = Document();

            var writtenPoints = PortableAnalysisFile.WriteCsv(path, expected);
            var actual = PortableAnalysisFile.ReadCsv(path);

            Assert.Equal(4, writtenPoints);
            Assert.Equal(PortableAnalysisExport.FormatVersion, actual.FormatVersion);
            Assert.Equal("pair", actual.WorkspaceMode);
            Assert.Equal(12.5, actual.Range!.StartSeconds, precision: 6);
            Assert.Equal(15.5, actual.Range.EndSeconds, precision: 6);
            var session = Assert.Single(actual.Sessions);
            Assert.Equal(0, session.SessionIndex);
            Assert.Equal("base", session.Role);
            Assert.Equal("Test run", session.Name);
            Assert.Equal("capture_Log.csv", session.Source);
            var fps = Assert.Single(session.Series);
            Assert.Equal([12.5, 13.5, 14.5, 15.5], fps.TimeSeconds);
            Assert.Equal([100.0, 105.0, 98.0, 110.0], fps.Values);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Legacy_statistics_csv_is_rejected_as_non_importable()
    {
        var path = TemporaryPath("csv");
        try
        {
            File.WriteAllText(path, "metric_id,metric,base_value\nfps,FPS,100\n");

            var error = Assert.Throws<InvalidDataException>(() => PortableAnalysisFile.ReadCsv(path));

            Assert.Contains("Older Statistics CSV files cannot recreate chart data", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static PortableAnalysisDocument Document() =>
        new(
            PortableAnalysisExport.FormatVersion,
            "pair",
            new PortableRangeDto(12.5, 15.5),
            [
                new PortableSessionDto(
                    0,
                    "base",
                    "Test run",
                    "capture_Log.csv",
                    null,
                    null,
                    new ManualMetadata(BenchmarkName: "Test run", Game: "Example"),
                    [
                        new PortableMetricSeriesDto(
                            "fps",
                            "FPS (Calculated)",
                            "FPS",
                            "Performance",
                            "HigherIsBetter",
                            [12.5, 13.5, 14.5, 15.5],
                            [100.0, 105.0, 98.0, 110.0]),
                    ]),
            ],
            []);

    private static string TemporaryPath(string extension) =>
        Path.Combine(Path.GetTempPath(), $"fva-portable-{Guid.NewGuid():N}.{extension}");
}
