namespace FrameViewAnalyzer.Core.Math;

/// <summary>
/// Statistics kernels shared by the analytics engine. Percentile
/// interpolation must match the Python reference exactly (linear between
/// the bracketing order statistics).
/// </summary>
public static class Statistics
{
    public static double? Mean(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sum = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
        }

        return sum / values.Count;
    }

    /// <summary>
    /// Linearly interpolated percentile over an ascending-sorted list,
    /// identical to the Python reference implementation.
    /// </summary>
    public static double? Percentile(IReadOnlyList<double> sortedValues, double p)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var position = (sortedValues.Count - 1) * p;
        var lower = (int)System.Math.Floor(position);
        var upper = System.Math.Min(lower + 1, sortedValues.Count - 1);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        return sortedValues[lower]
            + (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }
}
