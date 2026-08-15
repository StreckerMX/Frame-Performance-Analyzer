namespace FrameViewAnalyzer.Core.Charting;

/// <summary>
/// Visualization-only decimation. Statistics and analytics always use
/// full-resolution series; these strategies only reduce the points given
/// to the chart. Inputs must be aligned (xs ascending, no NaN).
/// </summary>
public static class Decimation
{
    /// <summary>
    /// Chooses the strategy for a pixel budget: full resolution when it
    /// fits, LTTB for mild reduction, and the min/max envelope when the
    /// series is far denser than the budget.
    /// </summary>
    public static (double[] Xs, double[] Ys) Select(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        int pointBudget)
    {
        if (xs.Count <= pointBudget || pointBudget <= 0)
        {
            return ([.. xs], [.. ys]);
        }

        return xs.Count <= pointBudget * 2
            ? Lttb(xs, ys, pointBudget)
            : MinMaxEnvelope(xs, ys, System.Math.Max(1, pointBudget / 2));
    }

    /// <summary>
    /// Min/max envelope: each bucket contributes its minimum and maximum
    /// (with their x positions), so extreme values survive every zoom level.
    /// At most 2 points per bucket, emitted in original order.
    /// </summary>
    public static (double[] Xs, double[] Ys) MinMaxEnvelope(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        int bucketCount)
    {
        var count = xs.Count;
        if (count <= 1 || bucketCount <= 0)
        {
            return ([.. xs], [.. ys]);
        }

        var bucketSize = System.Math.Max(1, (int)System.Math.Ceiling((double)count / bucketCount));
        var resultXs = new List<double>(bucketCount * 2);
        var resultYs = new List<double>(bucketCount * 2);

        for (var start = 0; start < count; start += bucketSize)
        {
            var end = System.Math.Min(start + bucketSize, count);
            var minIndex = start;
            var maxIndex = start;
            for (var i = start + 1; i < end; i++)
            {
                if (ys[i] < ys[minIndex])
                {
                    minIndex = i;
                }

                if (ys[i] > ys[maxIndex])
                {
                    maxIndex = i;
                }
            }

            if (minIndex <= maxIndex)
            {
                AddAt(xs, ys, resultXs, resultYs, minIndex);
                if (maxIndex != minIndex)
                {
                    AddAt(xs, ys, resultXs, resultYs, maxIndex);
                }
            }
            else
            {
                AddAt(xs, ys, resultXs, resultYs, maxIndex);
                AddAt(xs, ys, resultXs, resultYs, minIndex);
            }
        }

        return (resultXs.ToArray(), resultYs.ToArray());
    }

    /// <summary>
    /// Largest-Triangle-Three-Buckets downsampling: keeps the first and
    /// last points and reduces the rest to roughly <paramref name="threshold"/>
    /// points while preserving visual shape.
    /// </summary>
    public static (double[] Xs, double[] Ys) Lttb(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        int threshold)
    {
        var count = xs.Count;
        if (threshold <= 2 || count <= threshold)
        {
            return ([.. xs], [.. ys]);
        }

        var sampled = new double[threshold];
        var sampledX = new double[threshold];
        sampled[0] = ys[0];
        sampledX[0] = xs[0];
        sampled[threshold - 1] = ys[count - 1];
        sampledX[threshold - 1] = xs[count - 1];

        var bucketSize = (count - 2.0) / (threshold - 2);
        var currentIndex = 0;

        for (var bucket = 0; bucket < threshold - 2; bucket++)
        {
            var rangeStart = (int)System.Math.Floor((bucket + 1) * bucketSize) + 1;
            var rangeEnd = System.Math.Min((int)System.Math.Floor((bucket + 2) * bucketSize) + 1, count - 1);
            if (rangeEnd <= rangeStart)
            {
                rangeEnd = System.Math.Min(rangeStart + 1, count - 1);
            }

            // The final bucket may start past the last usable index; give it
            // the second-to-last point so every bucket contributes exactly
            // one sample and the final point is never duplicated.
            if (rangeStart >= count - 1)
            {
                rangeStart = count - 2;
                rangeEnd = count - 1;
            }

            var averageX = 0.0;
            var averageY = 0.0;
            for (var i = rangeStart; i < rangeEnd; i++)
            {
                averageX += xs[i];
                averageY += ys[i];
            }

            averageX /= rangeEnd - rangeStart;
            averageY /= rangeEnd - rangeStart;

            var previousX = sampledX[currentIndex];
            var previousY = sampled[currentIndex];
            var nextX = xs[count - 1];
            var nextY = ys[count - 1];

            var bestIndex = rangeStart;
            var bestArea = double.NegativeInfinity;
            for (var i = rangeStart; i < rangeEnd; i++)
            {
                var area = System.Math.Abs(
                    (previousX - xs[i]) * (nextY - previousY)
                    - (previousX - nextX) * (ys[i] - previousY));
                if (area > bestArea)
                {
                    bestArea = area;
                    bestIndex = i;
                }
            }

            currentIndex++;
            sampled[currentIndex] = ys[bestIndex];
            sampledX[currentIndex] = xs[bestIndex];
        }

        return (sampledX, sampled);
    }

    private static void AddAt(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        List<double> resultXs,
        List<double> resultYs,
        int index)
    {
        resultXs.Add(xs[index]);
        resultYs.Add(ys[index]);
    }
}

