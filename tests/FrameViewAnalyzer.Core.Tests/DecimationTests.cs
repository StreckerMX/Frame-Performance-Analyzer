using FrameViewAnalyzer.Core.Charting;

namespace FrameViewAnalyzer.Core.Tests;

public class DecimationTests
{
    [Fact]
    public void Select_returns_raw_points_when_they_fit_the_budget()
    {
        var xs = new double[] { 0, 1, 2, 3 };
        var ys = new double[] { 10, 20, 30, 40 };

        var (resultXs, resultYs) = Decimation.Select(xs, ys, pointBudget: 10);

        Assert.Equal(xs, resultXs);
        Assert.Equal(ys, resultYs);
    }

    [Fact]
    public void MinMaxEnvelope_preserves_global_extremes()
    {
        var xs = Enumerable.Range(0, 1000).Select(i => (double)i).ToArray();
        var ys = Enumerable.Range(0, 1000).Select(i => (double)(i % 97)).ToArray();

        var (resultXs, resultYs) = Decimation.MinMaxEnvelope(xs, ys, bucketCount: 50);

        Assert.True(resultYs.Length <= 100);
        Assert.Equal(ys.Min(), resultYs.Min());
        Assert.Equal(ys.Max(), resultYs.Max());
        for (var i = 1; i < resultXs.Length; i++)
        {
            Assert.True(resultXs[i] >= resultXs[i - 1]);
        }
    }

    [Fact]
    public void MinMaxEnvelope_of_tiny_input_is_the_input()
    {
        var (xs, ys) = Decimation.MinMaxEnvelope([1.0], [2.0], 5);

        Assert.Equal([1.0], xs);
        Assert.Equal([2.0], ys);
    }

    [Fact]
    public void Lttb_keeps_endpoints_and_respects_the_threshold()
    {
        var xs = Enumerable.Range(0, 500).Select(i => (double)i).ToArray();
        var ys = Enumerable.Range(0, 500).Select(i => 100.0 + 20.0 * System.Math.Sin(i / 20.0)).ToArray();

        var (resultXs, resultYs) = Decimation.Lttb(xs, ys, threshold: 50);

        Assert.Equal(50, resultXs.Length);
        Assert.Equal(xs[0], resultXs[0]);
        Assert.Equal(xs[^1], resultXs[^1]);
        Assert.Equal(ys[0], resultYs[0]);
        Assert.Equal(ys[^1], resultYs[^1]);
        for (var i = 1; i < resultXs.Length; i++)
        {
            Assert.True(resultXs[i] > resultXs[i - 1]);
        }
    }

    [Fact]
    public void Lttb_returns_raw_when_below_threshold()
    {
        var xs = new double[] { 0, 1, 2 };
        var ys = new double[] { 5, 6, 7 };

        var (resultXs, resultYs) = Decimation.Lttb(xs, ys, threshold: 10);

        Assert.Equal(xs, resultXs);
    }

    [Fact]
    public void Select_uses_lttb_for_mild_and_envelope_for_heavy_reduction()
    {
        var xs = Enumerable.Range(0, 1000).Select(i => (double)i).ToArray();
        var ys = Enumerable.Range(0, 1000).Select(i => (double)i).ToArray();

        var (lttbXs, _) = Decimation.Select(xs, ys, pointBudget: 700);
        var (envelopeXs, _) = Decimation.Select(xs, ys, pointBudget: 50);

        Assert.Equal(700, lttbXs.Length);
        Assert.True(envelopeXs.Length <= 100);
        Assert.Equal(ys.Min(), Decimation.Select(xs, ys, 50).Ys.Min());
    }
}

public class SeriesGeometryTests
{
    [Fact]
    public void Gaps_are_inserted_at_wide_time_jumps()
    {
        var xs = new double[] { 0, 1, 2, 10, 11 };
        var ys = new double[] { 1, 2, 3, 4, 5 };

        var (resultXs, resultYs) = SeriesGeometry.InsertGapBreaks(xs, ys);

        Assert.Equal(6, resultXs.Length);
        Assert.True(double.IsNaN(resultXs[3]));
        Assert.True(double.IsNaN(resultYs[3]));
        Assert.Equal(1, resultXs.Count(double.IsNaN));
    }

    [Fact]
    public void Close_points_produce_no_gaps()
    {
        var xs = new double[] { 0, 0.5, 1.0, 1.5 };
        var ys = new double[] { 1, 2, 3, 4 };

        var (resultXs, resultYs) = SeriesGeometry.InsertGapBreaks(xs, ys);

        Assert.Equal(xs, resultXs);
        Assert.Equal(ys, resultYs);
    }

    [Fact]
    public void Empty_series_stays_empty()
    {
        var (xs, ys) = SeriesGeometry.InsertGapBreaks([], []);

        Assert.Empty(xs);
        Assert.Empty(ys);
    }

    [Fact]
    public void Nearest_index_finds_exact_matches()
    {
        Assert.Equal(2, SeriesGeometry.NearestIndex([0.0, 1.0, 2.0, 3.0], 2.0));
    }

    [Fact]
    public void Nearest_index_resolves_between_points()
    {
        Assert.Equal(1, SeriesGeometry.NearestIndex([0.0, 1.0, 2.0, 3.0], 1.4));
        Assert.Equal(2, SeriesGeometry.NearestIndex([0.0, 1.0, 2.0, 3.0], 1.6));
    }

    [Fact]
    public void Nearest_index_clamps_to_the_edges()
    {
        Assert.Equal(0, SeriesGeometry.NearestIndex([0.0, 1.0, 2.0], -5.0));
        Assert.Equal(2, SeriesGeometry.NearestIndex([0.0, 1.0, 2.0], 99.0));
        Assert.Equal(-1, SeriesGeometry.NearestIndex([], 1.0));
    }

    [Fact]
    public void FindGaps_reports_spans_above_the_minimum_rule()
    {
        var xs = new double[] { 0, 1, 2, 9, 10, 20, 21 };

        var gaps = SeriesGeometry.FindGaps(xs);

        Assert.Equal(2, gaps.Count);
        Assert.Equal(new SeriesGeometry.GapSpan(2, 9), gaps[0]);
        Assert.Equal(7.0, gaps[0].DurationSeconds);
        Assert.Equal(new SeriesGeometry.GapSpan(10, 20), gaps[1]);
    }

    [Fact]
    public void FindGaps_ignores_small_deltas_and_empty_input()
    {
        Assert.Empty(SeriesGeometry.FindGaps([]));
        Assert.Empty(SeriesGeometry.FindGaps([1.0]));
        Assert.Empty(SeriesGeometry.FindGaps([0.0, 1.0, 2.0]));
    }

    [Fact]
    public void MergeOverlapping_unions_overlapping_and_adjacent_spans()
    {
        var merged = SeriesGeometry.MergeOverlapping(
        [
            new SeriesGeometry.GapSpan(2, 9),
            new SeriesGeometry.GapSpan(8, 12),
            new SeriesGeometry.GapSpan(12, 15),
            new SeriesGeometry.GapSpan(20, 21),
        ]);

        Assert.Equal(2, merged.Count);
        Assert.Equal(new SeriesGeometry.GapSpan(2, 15), merged[0]);
        Assert.Equal(new SeriesGeometry.GapSpan(20, 21), merged[1]);
    }

    [Fact]
    public void Label_threshold_matches_the_reference_omitted_rule()
    {
        // The Python reference labels gaps of ~3 s or more as "N s omitted".
        Assert.True(SeriesGeometry.LabelThresholdSeconds >= 3.0);
        var gaps = SeriesGeometry.FindGaps([0.0, 1.0, 5.0, 6.0]);
        Assert.True(gaps[0].DurationSeconds >= SeriesGeometry.LabelThresholdSeconds);
    }
}
