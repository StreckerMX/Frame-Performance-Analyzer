using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using ScottPlot;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>
/// Pure viewport math mirroring the Python chart interactions: cursor-anchored
/// wheel zoom, clamped drag pan, and adaptive Y auto-fit.
/// No WPF dependencies beyond ScottPlot's AxisLimits value type.
/// </summary>
public static class ChartViewport
{
    public const double MinimumSpanSeconds = 2.0;

    /// <summary>Y padding below the data band as a fraction of the spread.</summary>
    public const double LowerPaddingFraction = 0.08;

    /// <summary>Y padding above the data band as a fraction of the spread.</summary>
    public const double UpperPaddingFraction = 0.12;

    /// <summary>Minimum vertical span used by the adaptive FPS axis.</summary>
    public const double MinimumFpsVerticalSpan = 30.0;

    /// <summary>FPS axis limits are rounded outward to this step.</summary>
    public const double FpsAxisStep = 5.0;

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
    /// a global absolute floor, so tiny-unit metrics and large-magnitude
    /// narrow bands keep tight, meaningful vertical ranges.
    ///
    /// FPS uses a dedicated adaptive policy: it preserves at least a 30 FPS
    /// vertical span, rounds outward to 5 FPS steps, and never goes below zero.
    /// This makes small FPS differences readable without letting a 1-2 FPS
    /// variation fill the entire chart height.
    /// </summary>
    public static AxisLimits FitY(AxisLimits current, double minY, double maxY, bool fpsAdaptiveScale)
    {
        var spread = System.Math.Max(maxY - minY, 0.0);

        if (fpsAdaptiveScale)
        {
            return FitAdaptiveFpsY(current, minY, maxY, spread);
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

    private static AxisLimits FitAdaptiveFpsY(
        AxisLimits current,
        double minY,
        double maxY,
        double spread)
    {
        var lower = minY - spread * LowerPaddingFraction;
        var upper = maxY + spread * UpperPaddingFraction;

        // Very narrow FPS bands get a minimum visual context so tiny changes
        // remain visible without being exaggerated into full-height swings.
        if (upper - lower < MinimumFpsVerticalSpan)
        {
            var center = (minY + maxY) / 2.0;
            lower = center - MinimumFpsVerticalSpan / 2.0;
            upper = center + MinimumFpsVerticalSpan / 2.0;
        }

        lower = System.Math.Max(0.0, lower);
        lower = System.Math.Floor(lower / FpsAxisStep) * FpsAxisStep;
        upper = System.Math.Ceiling(upper / FpsAxisStep) * FpsAxisStep;

        // Clamping the lower bound to zero can shrink a low-FPS range below
        // the minimum span. Restore the context on the upper side if needed.
        if (upper - lower < MinimumFpsVerticalSpan)
        {
            upper = lower + MinimumFpsVerticalSpan;
            upper = System.Math.Ceiling(upper / FpsAxisStep) * FpsAxisStep;
        }

        // Always leave at least one nice tick of headroom above the maximum
        // if rounding happened to land exactly on the highest sample.
        if (upper <= maxY + 1e-9)
        {
            upper += FpsAxisStep;
        }

        return new AxisLimits(current.Left, current.Right, lower, upper);
    }

    /// <summary>
    /// Global Y fit across every plotted series inside the visible X range.
    /// Series without points in the range are ignored; null is returned when
    /// nothing is visible. FPS metrics use the adaptive FPS scale. Full-
    /// resolution series only, never decimated rendering data.
    /// </summary>
    public static AxisLimits? AutoZoomToSeries(
        AxisLimits current,
        IReadOnlyList<MetricSeries> seriesList,
        bool fpsAdaptiveScale)
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

        return FitY(current, minY.Value, maxY.Value, fpsAdaptiveScale);
    }

    /// <summary>
    /// Canonical chart bounds computed from the FULL-resolution series arrays
    /// — never from decimated rendering data and never from the current
    /// viewport. Establishes the initial fitted view after a session load and
    /// recovers the full range on Auto Zoom / Reset Zoom. X spans the union of
    /// every plotted series; Y is fitted over the full arrays.
    /// </summary>
    public static AxisLimits? FullSeriesLimits(
        IReadOnlyList<MetricSeries> seriesList,
        bool fpsAdaptiveScale)
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

        return FitY(new AxisLimits(minX, maxX, 0, 1), minY.Value, maxY.Value, fpsAdaptiveScale);
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