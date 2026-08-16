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

    /// <summary>Y padding below the data band as a fraction of the spread.</summary>
    public const double LowerPaddingFraction = 0.08;

    /// <summary>Y padding above the data band as a fraction of the spread.</summary>
    public const double UpperPaddingFraction = 0.12;

    /// <summary>Padding around a constant non-zero series as a fraction of its value.</summary>
    public const double ConstantSeriesPaddingFraction = 0.02;

    /// <summary>Symmetric padding for an all-zero constant series.</summary>
    public const double ZeroSeriesPadding = 0.5;

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

    /// <summary>
    /// Adaptive Y fit shared by the interactive chart and the PNG report.
    /// Padding scales with the DATA SPREAD (8% below, 12% above), never with
    /// a global absolute floor — tiny-unit metrics (e.g. Time in Present API,
    /// 0.278..0.394 ms) and large-magnitude narrow bands (e.g. GPU clocks,
    /// 2787..2842 MHz) keep tight, meaningful vertical ranges. FPS keeps its
    /// intentional zero baseline. Constant series fall back to a small
    /// relative padding around the value; an all-zero series gets a small
    /// symmetric range.
    /// </summary>
    public static AxisLimits FitY(AxisLimits current, double minY, double maxY, bool fpsBaselineZero)
    {
        var spread = System.Math.Max(maxY - minY, 0.0);

        if (fpsBaselineZero)
        {
            // Intentional FPS behavior: zero baseline with useful headroom.
            var high = maxY + System.Math.Max(
                System.Math.Max(spread * 0.16, System.Math.Abs(maxY) * 0.03),
                1.0);
            return new AxisLimits(current.Left, current.Right, 0.0, high);
        }

        if (spread <= 1e-12)
        {
            // Constant (or nearly constant) series: small relative fallback.
            var center = (minY + maxY) / 2.0;
            var pad = System.Math.Abs(center) <= 1e-12
                ? ZeroSeriesPadding
                : System.Math.Abs(center) * ConstantSeriesPaddingFraction;
            return new AxisLimits(current.Left, current.Right, center - pad, center + pad);
        }

        var lowerPadding = spread * LowerPaddingFraction;
        var upperPadding = spread * UpperPaddingFraction;
        return new AxisLimits(
            current.Left,
            current.Right,
            minY - lowerPadding,
            maxY + upperPadding);
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

    /// <summary>
    /// Canonical chart bounds computed from the FULL-resolution series arrays
    /// — never from decimated rendering data and never from the current
    /// viewport. Establishes the initial fitted view after a session load and
    /// recovers the full range on Auto Zoom / Reset Zoom. X spans the union of
    /// every plotted series; Y is fitted over the full arrays with the
    /// reference headroom (FPS keeps the zero baseline).
    /// </summary>
    public static AxisLimits? FullSeriesLimits(
        IReadOnlyList<MetricSeries> seriesList,
        bool fpsBaselineZero)
    {
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        double? minY = null;
        double? maxY = null;

        foreach (var series in seriesList)
        {
            if (series.X.Length == 0)
            {
                continue;
            }

            minX = System.Math.Min(minX, series.X.Min());
            maxX = System.Math.Max(maxX, series.X.Max());

            var seriesMinY = series.Y.Min();
            var seriesMaxY = series.Y.Max();
            minY = minY is null ? seriesMinY : System.Math.Min(minY.Value, seriesMinY);
            maxY = maxY is null ? seriesMaxY : System.Math.Max(maxY.Value, seriesMaxY);
        }

        if (double.IsInfinity(minX) || minY is null || maxY is null)
        {
            return null;
        }

        return FitY(new AxisLimits(minX, maxX, 0, 1), minY.Value, maxY.Value, fpsBaselineZero);
    }

    /// <summary>
    /// Normalizes a horizontal drag selection: reverses backwards drags,
    /// clamps to the canonical full-series X bounds, and cancels selections
    /// shorter than the minimum span (1.0 s, matching the reference). Returns
    /// null when the selection must be ignored.
    /// </summary>
    public static AxisLimits? NormalizeRangeSelection(
        double startX,
        double endX,
        AxisLimits full,
        double minimumSpanSeconds = 1.0)
    {
        var min = System.Math.Min(startX, endX);
        var max = System.Math.Max(startX, endX);
        min = System.Math.Max(full.Left, min);
        max = System.Math.Min(full.Right, max);
        if (max - min < minimumSpanSeconds)
        {
            return null;
        }

        return new AxisLimits(min, max, full.Bottom, full.Top);
    }
}
