using System.Text.Json;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.Analytics.Tests;

/// <summary>
/// Certified statistical/filter parity against the Python reference
/// application. Precision Timeline deliberately changes only the chart X-axis
/// representation: valid capture bins are compressed into analyzed time while
/// their values, statistics, active window, and filtering remain certified by
/// the existing golden fixture.
/// </summary>
public class GoldenFixtureTests
{
    private const double Tolerance = 1e-9;

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    [Fact]
    public async Task Certified_output_matches_the_python_reference()
    {
        using var golden = JsonDocument.Parse(await File.ReadAllTextAsync(FixturePath("golden.json")));
        var reader = new FrameViewCsvReader();
        var service = new CaptureAnalysisService();

        var options = new AnalysisOptions(
            GpuThreshold: 10.0,
            TrimBufferSeconds: 1.0,
            AutoGpuThreshold: true,
            ExcludeTransitions: true);

        var baseCapture = await reader.LoadCaptureAsync(FixturePath("golden_base.csv"));
        var comparisonCapture = await reader.LoadCaptureAsync(FixturePath("golden_comparison.csv"));
        var baseSession = service.Analyze(baseCapture, options);
        var comparisonSession = service.Analyze(comparisonCapture, options);

        AssertSession(baseSession, golden.RootElement.GetProperty("base"));
        AssertSession(comparisonSession, golden.RootElement.GetProperty("comparison"));
        AssertComparison(baseSession, comparisonSession, golden.RootElement.GetProperty("comparison_rows"));
    }

    private static void AssertSession(SessionAnalysis session, JsonElement expected)
    {
        var window = expected.GetProperty("window");
        Assert.NotNull(session.Window);
        AssertClose(window.GetProperty("start").GetDouble(), session.Window!.Start);
        AssertClose(window.GetProperty("end").GetDouble(), session.Window.End);

        var validBins = expected.GetProperty("valid_bins")
            .EnumerateArray()
            .Select(element => element.GetInt32())
            .ToArray();
        Assert.Equal(validBins, session.ValidBins.Order().ToArray());

        var diagnostics = expected.GetProperty("diagnostics");
        Assert.Equal(diagnostics.GetProperty("total_bins").GetInt32(), session.Diagnostics.TotalBins);
        Assert.Equal(diagnostics.GetProperty("visible_bins").GetInt32(), session.Diagnostics.VisibleBins);
        Assert.Equal(diagnostics.GetProperty("below_gpu_bins").GetInt32(), session.Diagnostics.BelowGpuBins);
        Assert.Equal(diagnostics.GetProperty("fps_outlier_bins").GetInt32(), session.Diagnostics.FpsOutlierBins);
        Assert.Equal(0, session.Diagnostics.TransitionEdgeBins);
        Assert.Equal(diagnostics.GetProperty("edge_trimmed_bins").GetInt32(), session.Diagnostics.EdgeTrimmedBins);
        AssertClose(
            diagnostics.GetProperty("fps_upper_bound").GetDouble(),
            session.Diagnostics.FpsUpperBound!.Value);

        AssertClose(
            expected.GetProperty("effective_gpu_threshold").GetDouble(),
            session.EffectiveOptions.GpuThreshold);

        var metadata = expected.GetProperty("metadata");
        Assert.NotNull(session.Metadata);
        Assert.Equal(metadata.GetProperty("application").GetString(), session.Metadata!.Application);
        Assert.Equal(metadata.GetProperty("resolution").GetString(), session.Metadata.Resolution);
        Assert.Equal(metadata.GetProperty("gpu").GetString(), session.Metadata.Gpu);
        Assert.Equal(metadata.GetProperty("cpu").GetString(), session.Metadata.Cpu);
        Assert.Equal(metadata.GetProperty("runtime").GetString(), session.Metadata.Runtime);
        Assert.Equal(metadata.GetProperty("duration").GetString(), session.Metadata.Duration);
        Assert.Equal(metadata.GetProperty("capture_duration").GetString(), session.Metadata.CaptureDuration);
        Assert.Equal(metadata.GetProperty("frame_count").GetInt32(), session.Metadata.FrameCount);
        Assert.Equal(metadata.GetProperty("metric_count").GetInt32(), session.Metadata.MetricCount);

        var expectedMetrics = expected.GetProperty("metrics").EnumerateArray().ToList();
        Assert.Equal(expectedMetrics.Count, session.Catalog.Count);

        var validBinOrder = validBins
            .Select((bin, index) => (bin, index))
            .ToDictionary(pair => pair.bin, pair => pair.index);
        var originalWindowStart = window.GetProperty("start").GetDouble();

        for (var i = 0; i < expectedMetrics.Count; i++)
        {
            var expectedMetric = expectedMetrics[i];
            var metric = session.Catalog[i];

            Assert.Equal(expectedMetric.GetProperty("id").GetString(), metric.Id);
            Assert.Equal(expectedMetric.GetProperty("label").GetString(), metric.Label);
            Assert.Equal(expectedMetric.GetProperty("unit").GetString(), metric.Unit);

            var direction = DirectionFromJson(expectedMetric.GetProperty("higher_is_better"));
            Assert.Equal(direction, metric.Direction);

            var series = SeriesBuilder.Build(session, metric.Id);
            var expectedPoints = expectedMetric.GetProperty("points").EnumerateArray().ToList();
            Assert.Equal(expectedPoints.Count, series.X.Length);
            Assert.Equal(expectedPoints.Count, series.Y.Length);
            for (var p = 0; p < expectedPoints.Count; p++)
            {
                // Golden X is capture time relative to the old active-window
                // origin. Precision Timeline maps the same retained bin to its
                // dense analyzed-time rank instead of preserving loading gaps.
                var oldRelativeX = expectedPoints[p][0].GetDouble();
                var captureBin = (int)Math.Round(originalWindowStart + oldRelativeX);
                Assert.True(validBinOrder.TryGetValue(captureBin, out var analyzedIndex));
                AssertClose(analyzedIndex, series.X[p]);
                AssertClose(expectedPoints[p][1].GetDouble(), series.Y[p]);
            }

            var stats = StatisticsCalculator.Compute(metric, series.Y);
            var expectedStats = expectedMetric.GetProperty("stats");
            foreach (var key in new[] { "avg", "min", "max", "p1", "p01" })
            {
                var actual = key switch
                {
                    "avg" => stats.Avg,
                    "min" => stats.Min,
                    "max" => stats.Max,
                    "p1" => stats.P1,
                    "p01" => stats.P01,
                    _ => null,
                };

                if (expectedStats.TryGetProperty(key, out var expectedValue))
                {
                    Assert.NotNull(actual);
                    AssertClose(expectedValue.GetDouble(), actual!.Value);
                }
                else
                {
                    Assert.Null(actual);
                }
            }
        }
    }

    private static void AssertComparison(
        SessionAnalysis baseSession,
        SessionAnalysis comparisonSession,
        JsonElement expectedRows)
    {
        var service = new ComparisonService();
        var rows = service.Compare(baseSession, comparisonSession);
        var byKey = rows.ToDictionary(
            row => $"{row.MetricId}|{row.StatisticKey}",
            StringComparer.Ordinal);

        var expected = expectedRows.EnumerateArray().ToList();
        Assert.Equal(expected.Count, rows.Count);

        foreach (var expectedRow in expected)
        {
            var metricId = expectedRow.GetProperty("metric_id").GetString()!;
            var statisticKey = expectedRow.GetProperty("statistic_key").GetString()!;
            var key = $"{metricId}|{statisticKey}";
            Assert.True(byKey.ContainsKey(key), $"missing row for {key}");
            var row = byKey[key];

            AssertCloseOrNull(expectedRow.GetProperty("base_value"), row.BaseValue);
            AssertCloseOrNull(expectedRow.GetProperty("comparison_value"), row.ComparisonValue);
            AssertCloseOrNull(expectedRow.GetProperty("delta"), row.Delta);
            AssertCloseOrNull(expectedRow.GetProperty("delta_percent"), row.DeltaPercent);

            var expectedKind = expectedRow.GetProperty("kind");
            var kind = expectedKind.ValueKind == JsonValueKind.Null
                ? ImprovementKind.None
                : expectedKind.GetString() switch
                {
                    "improvement" => ImprovementKind.Improvement,
                    "regression" => ImprovementKind.Regression,
                    _ => ImprovementKind.None,
                };
            Assert.Equal(kind, row.Kind);
        }
    }

    private static MetricDirection DirectionFromJson(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null
            ? MetricDirection.Undefined
            : element.GetBoolean()
                ? MetricDirection.HigherIsBetter
                : MetricDirection.LowerIsBetter;

    private static void AssertCloseOrNull(JsonElement expected, double? actual)
    {
        if (expected.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(actual);
        }
        else
        {
            Assert.NotNull(actual);
            AssertClose(expected.GetDouble(), actual!.Value);
        }
    }

    private static void AssertClose(double expected, double actual) =>
        Assert.True(
            Math.Abs(expected - actual) <= Tolerance,
            $"expected {expected:R}, actual {actual:R} (tolerance {Tolerance})");
}
