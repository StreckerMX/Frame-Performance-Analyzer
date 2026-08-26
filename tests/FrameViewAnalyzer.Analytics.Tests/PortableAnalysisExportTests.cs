using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Analytics.Filtering;
using FrameViewAnalyzer.Analytics.Samples;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

public class PortableAnalysisExportTests
{
    [Fact]
    public void Build_clips_every_metric_to_the_requested_visible_time_range()
    {
        var session = ImportedSession(
            "base",
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9],
            [100, 101, 102, 103, 104, 105, 106, 107, 108, 109]);
        var option = new ExportSessionOption(SessionRole.Base, "Base run", session);

        var document = PortableAnalysisExport.Build(
            [option],
            isMultiWorkspace: false,
            rangeStartSeconds: 3,
            rangeEndSeconds: 6);

        Assert.NotNull(document.Range);
        Assert.Equal(3, document.Range!.StartSeconds, precision: 6);
        Assert.Equal(6, document.Range.EndSeconds, precision: 6);
        var fps = Assert.Single(Assert.Single(document.Sessions).Series);
        Assert.Equal([3.0, 4.0, 5.0, 6.0], fps.TimeSeconds);
        Assert.Equal([103.0, 104.0, 105.0, 106.0], fps.Values);

        var average = document.Statistics.Single(row =>
            row.MetricId == "fps" && row.StatisticKey == "avg");
        Assert.Equal(104.5, average.BaseValue!.Value, precision: 6);
    }

    [Fact]
    public void Restored_portable_session_uses_the_exported_series_without_raw_capture_rows()
    {
        var original = ImportedSession(
            "base",
            [10, 11, 12, 13, 14],
            [90, 95, 100, 105, 110]);
        var document = PortableAnalysisExport.Build(
            [new ExportSessionOption(SessionRole.Base, "Visible slice", original)],
            isMultiWorkspace: false,
            rangeStartSeconds: 11,
            rangeEndSeconds: 13);

        var restored = Assert.Single(PortableAnalysisExport.RestoreSessions(document, "slice.json"));
        var series = SeriesBuilder.Build(restored.Session, "fps");

        Assert.True(restored.Session.IsPortableImport);
        Assert.Empty(restored.Session.Samples.TimeSeconds);
        Assert.Equal([11.0, 12.0, 13.0], series.X);
        Assert.Equal([95.0, 100.0, 105.0], series.Y);
        Assert.Equal("Visible slice", restored.Label);
        Assert.Equal("base", restored.Role);
    }

    private static SessionAnalysis ImportedSession(
        string name,
        double[] x,
        double[] y)
    {
        var fps = CoreMetricCatalog.CoreById["fps"];
        return new SessionAnalysis
        {
            Capture = new CaptureData
            {
                Path = $"{name}.csv",
                DisplayName = name,
                Kind = CsvKind.Log,
                Headers = [],
                Columns = [],
            },
            Catalog = [fps],
            Samples = new ParsedSamples
            {
                TimeSeconds = [],
                FrametimeMs = [],
                Fps = [],
                GpuUtilPercent = [],
                RowIndex = [],
            },
            EffectiveOptions = new AnalysisOptions(),
            Bins = [],
            RowsByBin = new Dictionary<int, int[]>(),
            Window = new ActiveWindow(x.Min(), x.Max()),
            ValidBins = new HashSet<int>(),
            Diagnostics = new FilterDiagnostics(
                TotalBins: x.Length,
                VisibleBins: x.Length),
            ImportedSeries = new Dictionary<string, ImportedSeriesData>(StringComparer.Ordinal)
            {
                [fps.Id] = new ImportedSeriesData(x, y),
            },
        };
    }
}
