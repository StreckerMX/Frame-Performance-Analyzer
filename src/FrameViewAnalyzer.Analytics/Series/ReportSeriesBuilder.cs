namespace FrameViewAnalyzer.Analytics.Series;

/// <summary>
/// Chooses the source resolution used by PNG report charts. When Frame points
/// is active, reports use the same true analyzed frame-level data as the
/// interactive chart. If frame-level rows are unavailable (for example a
/// portable analyzed-data import), the normal one-second analyzed series is
/// used as a compatibility fallback.
/// </summary>
public static class ReportSeriesBuilder
{
    public static MetricSeries Build(
        SessionAnalysis session,
        string metricId,
        bool useFramePoints)
    {
        if (useFramePoints)
        {
            var frameSeries = FramePointSeriesBuilder.Build(session, metricId);
            if (frameSeries.Y.Length > 0)
            {
                return frameSeries;
            }
        }

        return SeriesBuilder.Build(session, metricId);
    }
}
