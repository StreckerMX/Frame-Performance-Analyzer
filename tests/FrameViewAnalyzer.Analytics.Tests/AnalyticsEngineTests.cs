using FrameViewAnalyzer.Analytics.Filtering;
using FrameViewAnalyzer.Analytics.Samples;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

public class AnalyticsEngineTests
{
    private readonly CaptureAnalysisService _service = new();

    [Fact]
    public void Auto_gpu_threshold_is_bounded()
    {
        var low = SamplesWithUtils(Enumerable.Repeat(1.0, 10).ToArray());
        var high = SamplesWithUtils(Enumerable.Repeat(100.0, 10).ToArray());

        Assert.Equal(5.0, _service.ComputeAutoGpuThreshold(low));
        Assert.Equal(55.0, _service.ComputeAutoGpuThreshold(high));
    }

    [Fact]
    public void Auto_threshold_uses_seconds_instead_of_frame_weighting()
    {
        var times = new List<double>();
        var utils = new List<double>();
        for (var second = 0; second < 8; second++)
        {
            for (var offset = 0; offset < 10; offset++)
            {
                times.Add(second + offset / 10.0);
                utils.Add(90.0);
            }
        }

        for (var offset = 0; offset < 1000; offset++)
        {
            times.Add(8 + offset / 1000.0);
            utils.Add(20.0);
        }

        var samples = new ParsedSamples
        {
            TimeSeconds = times.ToArray(),
            FrametimeMs = new double[times.Count],
            Fps = new double[times.Count],
            GpuUtilPercent = utils.ToArray(),
            RowIndex = Enumerable.Range(0, times.Count).ToArray(),
        };

        Assert.Equal(50.0, _service.ComputeAutoGpuThreshold(samples));
    }

    [Fact]
    public void Auto_threshold_uses_clear_gpu_valley_when_fallback_cuts_through_low_state()
    {
        var samples = SamplesWithUtils(
        [
            40, 42, 44, 46,
            78, 79, 80, 81, 82, 79, 80, 81,
            78, 79, 80, 81, 82, 80, 79, 81,
        ]);

        var threshold = _service.ComputeAutoGpuThreshold(samples);

        Assert.InRange(threshold, 57.0, 59.0);
    }

    [Fact]
    public void Auto_threshold_keeps_fallback_when_it_already_clears_low_state()
    {
        var samples = SamplesWithUtils(
        [
            30, 30, 30, 30,
            80, 80, 80, 80, 80, 80, 80, 80,
            80, 80, 80, 80, 80, 80, 80, 80,
        ]);

        Assert.Equal(44.0, _service.ComputeAutoGpuThreshold(samples));
    }

    [Fact]
    public void Non_finite_timestamps_are_skipped()
    {
        var capture = TestCapture.CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents"],
            [["0", "10"], ["inf", "10"]]);

        var samples = ParsedSampleBuilder.Build(capture);

        Assert.Equal(1, samples.Count);
    }

    [Fact]
    public void Non_finite_options_fall_back_safely()
    {
        Assert.Equal(1.0, FilterProfileDetector.NormalizeTrimBuffer(double.PositiveInfinity));
        Assert.Equal("--", DisplayText.FormatDuration(double.PositiveInfinity));
    }

    [Fact]
    public void Zero_trim_preserves_every_complete_bin()
    {
        var session = _service.Analyze(
            TestCapture.MakeSession(),
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));

        var series = SeriesBuilder.Build(session, "fps");

        Assert.Equal(10, series.X.Length);
        Assert.Equal(
            Enumerable.Range(0, 10).Select(value => (double)value),
            series.X);
        Assert.All(series.Y, value => Assert.Equal(100.0, value));
    }

    [Fact]
    public void Trim_removes_exactly_one_bin_from_each_outer_edge()
    {
        var session = _service.Analyze(
            TestCapture.MakeSession(),
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 1, AutoGpuThreshold: false));

        var series = SeriesBuilder.Build(session, "fps");

        Assert.Equal(8, series.X.Length);
        Assert.Equal(
            Enumerable.Range(0, 8).Select(value => (double)value),
            series.X);
    }

    [Fact]
    public void Latency_percentiles_use_the_high_tail()
    {
        var stats = StatisticsCalculator.Compute(
            CoreMetricCatalog.CoreById["latency"],
            [1.0, 2.0, 3.0, 100.0]);

        Assert.True(stats.P1 > 90.0, $"p1 was {stats.P1}");
        Assert.Equal(100.0, stats.Max);
    }

    [Fact]
    public void Metadata_reports_runtime_durations_and_metric_count()
    {
        var session = _service.Analyze(
            TestCapture.MakeSession(seconds: 4, withMetadata: true),
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));

        Assert.NotNull(session.Metadata);
        Assert.Equal("Game.exe", session.Metadata.Application);
        Assert.Equal("DXGI", session.Metadata.Runtime);
        Assert.Equal("4 s", session.Metadata.Duration);
        Assert.Equal("4 s", session.Metadata.CaptureDuration);
        Assert.Equal(session.Catalog.Count, session.Metadata.MetricCount);
    }

    [Fact]
    public void Excludes_high_fps_transitions_between_benchmark_scenes_and_compresses_time()
    {
        var session = _service.Analyze(
            TestCapture.MakeMultiScene(),
            new AnalysisOptions(TrimBufferSeconds: 0, AutoGpuThreshold: true));

        var series = SeriesBuilder.Build(session, "fps");

        Assert.Equal(
            Enumerable.Range(0, 9).Select(value => (double)value),
            series.X);
        Assert.True(series.Y.Max() < 120.0);
        Assert.Equal(1, session.Diagnostics.FpsOutlierBins);
        Assert.Equal(3, session.Diagnostics.BelowGpuBins);
    }

    [Fact]
    public void Multimetric_transition_filter_removes_contaminated_edge_but_preserves_real_fps_dip()
    {
        var session = _service.Analyze(
            MakeHelldiversStyleTransition(),
            new AnalysisOptions(
                GpuThreshold: 38,
                TrimBufferSeconds: 0,
                AutoGpuThreshold: false,
                ExcludeTransitions: true));

        var fps = SeriesBuilder.Build(session, "fps");

        // Second 5 is deliberately bad gameplay: FPS falls to 75 while GPU,
        // CPU and presentation telemetry remain gameplay-like. It must count.
        Assert.Contains(5, session.ValidBins);
        Assert.Contains(fps.Y, value => Math.Abs(value - 75.0) < 0.01);

        // Second 6 is a partially contaminated transition modeled after the
        // real HELLDIVERS capture: cadence collapses together with GPU power,
        // queue depth and several latency/present signals. Second 7 is the
        // first-pass low-GPU loading bin. Both must be absent.
        Assert.DoesNotContain(6, session.ValidBins);
        Assert.DoesNotContain(7, session.ValidBins);
        Assert.Equal(1, session.Diagnostics.BelowGpuBins);
        Assert.True(session.Diagnostics.TransitionEdgeBins >= 1);
        Assert.InRange(fps.Y.Min(), 74.99, 75.01);
    }

    [Fact]
    public void Disabling_exclusion_really_disables_gpu_and_transition_filters()
    {
        var session = _service.Analyze(
            TestCapture.MakeLowGpu(),
            new AnalysisOptions(
                GpuThreshold: 10,
                TrimBufferSeconds: 0,
                AutoGpuThreshold: false,
                ExcludeTransitions: false));

        var series = SeriesBuilder.Build(session, "fps");

        Assert.Equal(6, series.X.Length);
        Assert.Equal(0, session.Diagnostics.BelowGpuBins);
        Assert.Equal(0, session.Diagnostics.FpsOutlierBins);
        Assert.Equal(0, session.Diagnostics.TransitionEdgeBins);
    }

    [Fact]
    public void Analyze_rejects_non_log_files()
    {
        var summary = TestCapture.CaptureWith(
            ["Log Name", "Avg FPS"],
            [["Run A", "100"]],
            CsvKind.Summary);

        Assert.Throws<ArgumentException>(() => _service.Analyze(summary));
    }

    [Fact]
    public void All_bins_below_gpu_threshold_produce_no_active_window()
    {
        var session = _service.Analyze(
            TestCapture.MakeLowGpu(),
            new AnalysisOptions(GpuThreshold: 10, TrimBufferSeconds: 0, AutoGpuThreshold: false));

        Assert.Null(session.Window);
        Assert.Empty(session.ValidBins);
        Assert.Empty(SeriesBuilder.Build(session, "fps").Y);
        Assert.Equal(0, session.Diagnostics.VisibleBins);
        Assert.Equal(6, session.Diagnostics.BelowGpuBins);
    }

    [Fact]
    public void Reanalysis_reuses_parsed_samples_and_metric_catalog()
    {
        var session = _service.Analyze(TestCapture.MakeSession());

        var updated = _service.Reanalyze(
            session,
            new AnalysisOptions(GpuThreshold: 50, TrimBufferSeconds: 2, AutoGpuThreshold: false));

        Assert.Same(session.Samples, updated.Samples);
        Assert.Same(session.Catalog, updated.Catalog);
        Assert.Same(session.Bins, updated.Bins);
        Assert.Equal(50.0, updated.EffectiveOptions.GpuThreshold);
    }

    private static CaptureData MakeHelldiversStyleTransition()
    {
        var headers = new[]
        {
            "TimeInSeconds",
            "MsBetweenPresents",
            "MsBetweenSimulationStart",
            "MsBetweenDisplayChange",
            "GPU0Util(%)",
            "NV Pwr(W) (API)",
            "GPU0Clk(MHz)",
            "GPU0MemClk(MHz)",
            "CPUUtil(%)",
            "CPU Package Power(W)",
            "CPUClk(MHz)",
            "Render Queue Depth",
            "MsPCLatency",
            "MsRenderPresentLatency",
            "MsUntilDisplayed",
            "MsInPresentAPI",
            "Frame Gen Multiplier",
            "Dropped",
        };

        var rows = new List<string[]>();
        for (var second = 0; second < 12; second++)
        {
            var fps = second switch
            {
                5 => 75.0,  // genuine performance drop: keep it
                6 => 35.0,  // contaminated transition edge: remove it
                7 => 95.0,  // low-GPU loading bin: first pass removes it
                _ => 120.0,
            };

            var transition = second == 6;
            var loading = second == 7;
            var gpu = transition ? 46.0 : loading ? 18.0 : 65.0;
            var power = transition ? 110.0 : loading ? 90.0 : 205.0;
            var frameTime = 1000.0 / fps;
            var simulation = transition ? 28.3 : 8.33;
            var display = transition ? 29.8 : 8.33;
            var queue = transition ? 0.03 : 0.50;
            var pcLatency = transition ? 69.0 : 24.0;
            var renderPresent = transition ? 0.63 : 3.5;
            var untilDisplayed = transition ? 1.24 : 5.8;
            var presentApi = transition ? 0.39 : 0.23;

            for (var sample = 0; sample < 4; sample++)
            {
                string F(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                rows.Add(
                [
                    F(second + sample * 0.20),
                    F(frameTime),
                    F(simulation),
                    F(display),
                    F(gpu),
                    F(power),
                    "2820",
                    "14001",
                    "32",
                    "56",
                    "3910",
                    F(queue),
                    F(pcLatency),
                    F(renderPresent),
                    F(untilDisplayed),
                    F(presentApi),
                    "0",
                    "0",
                ]);
            }
        }

        return TestCapture.CaptureWith(headers, rows);
    }

    private static ParsedSamples SamplesWithUtils(double[] utils)
    {
        return new ParsedSamples
        {
            TimeSeconds = Enumerable.Range(0, utils.Length).Select(i => (double)i).ToArray(),
            FrametimeMs = new double[utils.Length],
            Fps = new double[utils.Length],
            GpuUtilPercent = utils,
            RowIndex = Enumerable.Range(0, utils.Length).ToArray(),
        };
    }
}
