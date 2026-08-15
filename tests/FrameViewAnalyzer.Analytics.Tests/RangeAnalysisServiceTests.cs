using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

/// <summary>
/// Ports of the Python test_range_analysis.py cases; the algorithms must
/// produce identical ranges.
/// </summary>
public class RangeAnalysisServiceTests
{
    private readonly RangeAnalysisService _service = new();

    private static List<ChartPoint> Plateau(params (double X, double Y)[] pairs) =>
        [.. pairs.Select(pair => new ChartPoint(pair.X, pair.Y))];

    private static List<ChartPoint> PerSecond(Func<int, double> y, int count) =>
        Plateau([.. Enumerable.Range(0, count).Select(x => ((double)x, y(x)))]);

    public class WorstPerformanceRegion
    {
        [Fact]
        public void Higher_is_better_picks_the_lowest_window()
        {
            // 0-9 s: 100; 10-19 s: 50 (one sample per second).
            var series = PerSecond(x => x < 10 ? 100.0 : 50.0, 20);

            var region = new RangeAnalysisService().WorstPerformanceRegion(series, true);

            Assert.Equal(new TimeRange(10.0, 19.0), region);
        }

        [Fact]
        public void Lower_is_better_picks_the_highest_window()
        {
            var series = PerSecond(x => x < 10 ? 100.0 : 50.0, 20);

            var region = new RangeAnalysisService().WorstPerformanceRegion(series, false);

            Assert.Equal(new TimeRange(0.0, 9.0), region);
        }

        [Fact]
        public void Neutral_direction_returns_null()
        {
            var series = PerSecond(_ => 100.0, 20);

            Assert.Null(new RangeAnalysisService().WorstPerformanceRegion(series, null));
        }

        [Fact]
        public void Capture_shorter_than_the_window_returns_null()
        {
            var shortSeries = PerSecond(_ => 100.0, 8);

            Assert.Null(new RangeAnalysisService().WorstPerformanceRegion(shortSeries, true));
        }

        [Fact]
        public void Too_few_samples_returns_null()
        {
            var sparse = Plateau((0.0, 100.0), (5.0, 50.0), (10.0, 80.0));

            Assert.Null(new RangeAnalysisService().WorstPerformanceRegion(sparse, true));
        }
    }

    public class MostStableRegion
    {
        [Fact]
        public void Picks_the_flat_middle_region()
        {
            var series = PerSecond(
                x => x is >= 10 and < 20 ? 100.0 : (x % 2 == 0 ? 80.0 : 120.0),
                30);

            var region = new RangeAnalysisService().MostStableRegion(series);

            Assert.Equal(new TimeRange(10.0, 19.0), region);
        }

        [Fact]
        public void Constant_series_returns_the_earliest_window()
        {
            var series = PerSecond(_ => 42.0, 20);

            var region = new RangeAnalysisService().MostStableRegion(series);

            Assert.Equal(new TimeRange(0.0, 9.0), region);
        }

        [Fact]
        public void Short_capture_returns_null()
        {
            var shortSeries = PerSecond(_ => 42.0, 6);

            Assert.Null(new RangeAnalysisService().MostStableRegion(shortSeries));
        }

        [Fact]
        public void Sparse_gaps_return_null()
        {
            var gapped = Plateau(
                [.. Enumerable.Range(0, 4).Select(x => ((double)x, 42.0)),
                 .. Enumerable.Range(20, 4).Select(x => ((double)x, 42.0))]);

            Assert.Null(new RangeAnalysisService().MostStableRegion(gapped));
        }
    }

    public class LargestDropRegion
    {
        [Fact]
        public void Higher_is_better_finds_the_peak_to_trough()
        {
            var series = PerSecond(x => x < 10 ? 100.0 : 50.0, 20);

            var region = new RangeAnalysisService().LargestDropRegion(series, true);

            Assert.Equal(new TimeRange(0.0, 10.0), region);
        }

        [Fact]
        public void Lower_is_better_finds_the_valley_to_peak()
        {
            var series = PerSecond(x => x < 10 ? 50.0 : 100.0, 20);

            var region = new RangeAnalysisService().LargestDropRegion(series, false);

            Assert.Equal(new TimeRange(0.0, 10.0), region);
        }

        [Fact]
        public void Neutral_direction_returns_null()
        {
            var series = PerSecond(_ => 100.0, 20);

            Assert.Null(new RangeAnalysisService().LargestDropRegion(series, null));
        }

        [Fact]
        public void Insignificant_noise_returns_null()
        {
            var series = PerSecond(x => 100.0 + (x % 2) * 0.5, 20);

            Assert.Null(new RangeAnalysisService().LargestDropRegion(series, true));
        }

        [Fact]
        public void Short_series_returns_null()
        {
            var series = PerSecond(_ => 100.0, 5);

            Assert.Null(new RangeAnalysisService().LargestDropRegion(series, true));
        }
    }

    public class LargestAbDifferenceRegion
    {
        [Fact]
        public void Picks_the_region_where_sessions_diverge()
        {
            var baseSeries = PerSecond(_ => 100.0, 20);
            var comparison = PerSecond(x => x < 10 ? 120.0 : 40.0, 20);

            var region = new RangeAnalysisService().LargestAbDifferenceRegion(baseSeries, comparison);

            Assert.Equal(new TimeRange(10.0, 19.0), region);
        }

        [Fact]
        public void Unequal_session_lengths_still_find_a_region()
        {
            var baseSeries = PerSecond(_ => 100.0, 20);
            var comparison = PerSecond(_ => 120.0, 5);

            var region = new RangeAnalysisService().LargestAbDifferenceRegion(baseSeries, comparison);

            Assert.Equal(new TimeRange(0.0, 9.0), region);
        }

        [Fact]
        public void Empty_comparison_returns_null()
        {
            var baseSeries = PerSecond(_ => 100.0, 20);

            Assert.Null(new RangeAnalysisService().LargestAbDifferenceRegion(baseSeries, []));
        }

        [Fact]
        public void Non_overlapping_sessions_return_null()
        {
            var baseSeries = PerSecond(_ => 100.0, 10);
            var comparison = Plateau(
                [.. Enumerable.Range(100, 10).Select(x => ((double)x, 100.0))]);

            Assert.Null(new RangeAnalysisService().LargestAbDifferenceRegion(baseSeries, comparison));
        }
    }
}
