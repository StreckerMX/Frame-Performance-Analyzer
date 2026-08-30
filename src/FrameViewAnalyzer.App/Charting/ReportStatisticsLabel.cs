using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>
/// Adds compact metric statistics to the bottom axis of report-only plots.
/// Values always come from the full-resolution report series, before visual
/// decimation, and visible time remains measured in analyzed seconds.
/// </summary>
internal static class ReportStatisticsLabel
{
    internal static void Apply(
        Plot plot,
        IReadOnlyList<MetricSeries> seriesList,
        ChartStyle style)
    {
        var reportSeries = seriesList.Where(series => series.IsReportSeries).ToList();
        if (reportSeries.Count == 0 || reportSeries.Count != seriesList.Count)
        {
            return;
        }

        var text = BuildText(reportSeries);
        if (text.Length == 0)
        {
            return;
        }

        plot.Axes.Bottom.Label.Text = $"Capture time (s)\n{text}";
        plot.Axes.Bottom.Label.FontSize = IsMulti(reportSeries) ? 8.5f : 9.5f;
        plot.Axes.Bottom.Label.ForeColor = style.Foreground.WithAlpha(0.86);
    }

    internal static string BuildText(IReadOnlyList<MetricSeries> seriesList)
    {
        if (seriesList.Count == 0)
        {
            return string.Empty;
        }

        return IsMulti(seriesList)
            ? BuildMultiText(seriesList)
            : BuildPairText(seriesList);
    }

    private static string BuildPairText(IReadOnlyList<MetricSeries> seriesList)
    {
        var metric = seriesList[0].Metric;
        var entries = seriesList
            .Where(series => series.Y.Length > 0)
            .Select(series => new StatisticsEntry(
                series,
                StatisticsCalculator.Compute(metric, series.Y),
                VisibleSeconds(metric, series)))
            .ToList();
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var baseEntry = entries.FirstOrDefault(entry => entry.Series.Role == SessionRole.Base)
            ?? entries[0];
        var comparisonEntry = entries.FirstOrDefault(entry =>
            !ReferenceEquals(entry, baseEntry)
            && entry.Series.Role == SessionRole.Comparison);

        var cells = new List<string>();
        foreach (var (key, label) in StatisticFields(metric))
        {
            if (key == "time")
            {
                cells.Add($"{label} {FormatVisibleTime(baseEntry.VisibleSeconds, comparisonEntry?.VisibleSeconds)}");
                continue;
            }

            cells.Add(FormatPairStatistic(
                metric,
                label,
                StatisticValue(baseEntry.Statistics, key),
                comparisonEntry is null ? null : StatisticValue(comparisonEntry.Statistics, key)));
        }

        var perLine = metric.Id == "fps" ? 3 : 2;
        return WrapCells(cells, perLine);
    }

    private static string BuildMultiText(IReadOnlyList<MetricSeries> seriesList)
    {
        var metric = seriesList[0].Metric;
        var lines = new List<string>();
        foreach (var series in seriesList.Where(series => series.Y.Length > 0))
        {
            var statistics = StatisticsCalculator.Compute(metric, series.Y);
            var parts = new List<string> { $"B{series.WorkspaceIndex + 1}" };
            foreach (var (key, label) in StatisticFields(metric))
            {
                parts.Add(key == "time"
                    ? $"{label} {DisplayText.FormatDurationHuman(VisibleSeconds(metric, series))}"
                    : $"{label} {FormatMetricValue(metric, StatisticValue(statistics, key))}");
            }

            lines.Add(string.Join("  ·  ", parts));
        }

        return string.Join("\n", lines);
    }

    private static string FormatPairStatistic(
        MetricDefinition metric,
        string label,
        double? baseValue,
        double? comparisonValue)
    {
        if (baseValue is null && comparisonValue is null)
        {
            return $"{label} --";
        }

        if (baseValue is null)
        {
            return $"{label} {FormatMetricValue(metric, comparisonValue)}";
        }

        if (comparisonValue is null)
        {
            return $"{label} {FormatMetricValue(metric, baseValue)}";
        }

        var kind = CoreMetricCatalog.ClassifyImprovement(
            metric.Direction,
            baseValue,
            comparisonValue);
        var (delta, deltaPercent) = ComparisonService.ComputeDelta(baseValue, comparisonValue);
        var deltaText = ComparisonText.FormatDelta(delta, deltaPercent, kind);
        return $"{label} {FormatMetricValue(metric, baseValue)} → {FormatMetricValue(metric, comparisonValue)} {deltaText}";
    }

    private static int VisibleSeconds(MetricDefinition metric, MetricSeries series)
    {
        if (series.Y.Length == 0)
        {
            return 0;
        }

        if (series.SourceSession is not { } session || series.X.Length == 0)
        {
            // Portable analyzed-data imports expose the one-second analyzed
            // representation directly, so point count is the duration fallback.
            return series.Y.Length;
        }

        var summary = SeriesBuilder.Build(session, metric.Id);
        if (summary.X.Length == 0)
        {
            return 0;
        }

        var minimum = System.Math.Min(series.X[0], series.X[^1]);
        var maximum = System.Math.Max(series.X[0], series.X[^1]);
        var (_, count) = VisibleRangeCalculator.Compute(
            metric,
            summary.X,
            summary.Y,
            minimum,
            maximum);
        return count;
    }

    private static IReadOnlyList<(string Key, string Label)> StatisticFields(
        MetricDefinition metric) =>
        metric.Id == "fps"
            ?
            [
                ("avg", "AVERAGE"),
                ("p1", "1% LOW"),
                ("p01", "0.1% LOW"),
                ("max", "MAX"),
                ("min", "MIN"),
                ("time", "TIME"),
            ]
            :
            [
                ("avg", "AVERAGE"),
                ("max", "MAX"),
                ("min", "MIN"),
                ("time", "TIME"),
            ];

    private static double? StatisticValue(MetricStatistics statistics, string key) => key switch
    {
        "avg" => statistics.Avg,
        "min" => statistics.Min,
        "max" => statistics.Max,
        "p1" => statistics.P1,
        "p01" => statistics.P01,
        _ => null,
    };

    private static string FormatMetricValue(MetricDefinition metric, double? value)
    {
        if (value is null)
        {
            return "--";
        }

        // The chart title/Y axis already identify FPS, so repeating the unit
        // six times would only consume report width.
        if (metric.Id == "fps" || string.IsNullOrWhiteSpace(metric.Unit))
        {
            return $"{value:F1}";
        }

        return metric.Unit == "%"
            ? $"{value:F1}%"
            : $"{value:F1} {metric.Unit}";
    }

    private static string FormatVisibleTime(int baseSeconds, int? comparisonSeconds)
    {
        if (comparisonSeconds is null || comparisonSeconds.Value == baseSeconds)
        {
            return DisplayText.FormatDurationHuman(baseSeconds);
        }

        return $"{DisplayText.FormatDurationHuman(baseSeconds)} · {DisplayText.FormatDurationHuman(comparisonSeconds.Value)}";
    }

    private static string WrapCells(IReadOnlyList<string> cells, int perLine)
    {
        var lines = new List<string>();
        for (var index = 0; index < cells.Count; index += perLine)
        {
            lines.Add(string.Join("    ·    ", cells.Skip(index).Take(perLine)));
        }

        return string.Join("\n", lines);
    }

    private static bool IsMulti(IReadOnlyList<MetricSeries> seriesList) =>
        seriesList.Count > 1
        && seriesList.All(series => series.Role == SessionRole.Comparison);

    private sealed record StatisticsEntry(
        MetricSeries Series,
        MetricStatistics Statistics,
        int VisibleSeconds);
}
