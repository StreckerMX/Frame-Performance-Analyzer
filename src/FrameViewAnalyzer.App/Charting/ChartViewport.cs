using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using ScottPlot;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>
/// Pure viewport math mirroring the Python chart interactions: cursor-anchored
/// wheel zoom, clamped drag pan, and Y auto-fit with the reference padding.
/// No WPF dependencies beyond ScottPlot's AxisLimits value type.
/// </summary>
public static class ChartViewport
{
    public const double MinimumSpanSeconds = 2.0;

    /// <summary>Wheel zoom anchored at the cursor; span never drops below the minimum.</summary>
    public static AxisLimits ZoomAt(AxisLimits current, double anchorX, double scale, AxisLimits full)
    {
        var span = System.Math.Max(MinimumSpanSeconds, current.HorizontalSpan * scale);
        span = System.Math.Min(span, full.HorizontalSpan);
        var relative = (anchorX - current.Left) / System.Math.Max(current.HorizontalSpan, 1e-9);
        var left = anchorX - span * relative;
        var right = left + span;
        if (left < full.Left)
        {
            right += full.Left - left;
            left = full.Left;
        }

        if (right > full.Right)
        {
            left -= right - full.Right;
            right = full.Right;
        }

        left = System.Math.Max(full.Left, left);
        right = System.Math.Min(full.Right, right);
        return new AxisLimits(left, right, current.Bottom, current.Top);
    }

    /// <summary>Drag pan by a delta in data units, clamped to the full range.</summary>
    public static AxisLimits PanTo(AxisLimits current, AxisLimits full, double deltaX)
    {
        var left = current.Left + deltaX;
        var right = current.Right + deltaX;
        if (left < full.Left)
        {
            right += full.Left - left;
            left = full.Left;
        }

        if (right > full.Right)
        {
            left -= right - full.Right;
            right = full.Right;
        }

        return new AxisLimits(left, right, current.Bottom, current.Top);
    }

    /// <summary>Y fit over the visible slice, with the reference headroom.</summary>
    public static AxisLimits FitY(AxisLimits current, double minY, double maxY, bool fpsBaselineZero)
    {
        var spread = System.Math.Max(maxY - minY, 0.0);
        double low;
        double padLow;
        if (fpsBaselineZero)
        {
            low = 0.0;
            padLow = 0.0;
        }
        else
        {
            low = minY;
            padLow = System.Math.Max(
                System.Math.Max(spread * 0.06, System.Math.Abs(minY) * 0.01),
                0.5);
        }

        var high = maxY + System.Math.Max(
            System.Math.Max(spread * 0.16, System.Math.Abs(maxY) * 0.03),
            1.0);
        return new AxisLimits(current.Left, current.Right, low - padLow, high);
    }

    /// <summary>
    /// Global Y fit across every plotted series inside the visible X range.
    /// Series without points in the range are ignored; null is returned when
    /// nothing is visible. FPS metrics keep the zero baseline. Full-resolution
    /// series only — never decimated rendering data.
    /// </summary>
    public static AxisLimits? AutoZoomToSeries(
        AxisLimits current,
        IReadOnlyList<MetricSeries> seriesList,
        bool fpsBaselineZero)
    {
        double? minY = null;
        double? maxY = null;
        foreach (var series in seriesList)
        {
            var values = VisibleRangeCalculator.FilterValues(
                series.X, series.Y, current.Left, current.Right);
            if (values.Count == 0)
            {
                continue;
            }

            var seriesMin = values.Min();
            var seriesMax = values.Max();
            minY = minY is null ? seriesMin : System.Math.Min(minY.Value, seriesMin);
            maxY = maxY is null ? seriesMax : System.Math.Max(maxY.Value, seriesMax);
        }

        if (minY is null || maxY is null)
        {
            return null;
        }

        return FitY(current, minY.Value, maxY.Value, fpsBaselineZero);
    }
}
