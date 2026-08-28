using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class AnalysisRangeViewModelTests
{
    [Fact]
    public void Snapshot_options_use_raw_mode_when_precision_filtering_is_off()
    {
        var viewModel = new AnalysisRangeViewModel
        {
            AutoGpuThresholdEnabled = false,
            GpuThreshold = 37.0,
            TrimBufferSeconds = 2.5,
            ExcludeTransitionsEnabled = false,
        };

        var options = viewModel.SnapshotOptions();

        Assert.True(options.AutoGpuThreshold);
        Assert.Equal(37.0, options.GpuThreshold);
        Assert.Equal(0.0, options.TrimBufferSeconds);
        Assert.False(options.ExcludeTransitions);
    }

    [Fact]
    public void Snapshot_options_use_the_full_automatic_pipeline_when_enabled()
    {
        var viewModel = new AnalysisRangeViewModel
        {
            AutoGpuThresholdEnabled = false,
            GpuThreshold = 400.0,
            TrimBufferSeconds = 9.0,
            ExcludeTransitionsEnabled = true,
        };

        var options = viewModel.SnapshotOptions();

        Assert.True(options.AutoGpuThreshold);
        Assert.Equal(80.0, options.GpuThreshold);
        Assert.Equal(AnalysisConstants.DefaultTrimBufferSeconds, options.TrimBufferSeconds);
        Assert.True(options.ExcludeTransitions);
    }

    [Fact]
    public void Precision_filter_toggle_raises_immediately_with_binary_options()
    {
        var viewModel = new AnalysisRangeViewModel();
        viewModel.Attach(Session(excludeTransitions: true), null);
        var events = new List<AnalysisOptions>();
        viewModel.OptionsChanged += (_, options) => events.Add(options);

        viewModel.ExcludeTransitionsEnabled = false;

        var raw = Assert.Single(events);
        Assert.False(raw.ExcludeTransitions);
        Assert.Equal(0.0, raw.TrimBufferSeconds);
        Assert.True(raw.AutoGpuThreshold);

        events.Clear();
        viewModel.ExcludeTransitionsEnabled = true;

        var filtered = Assert.Single(events);
        Assert.True(filtered.ExcludeTransitions);
        Assert.Equal(AnalysisConstants.DefaultTrimBufferSeconds, filtered.TrimBufferSeconds);
        Assert.True(filtered.AutoGpuThreshold);
    }

    [Fact]
    public void Multi_precision_filter_toggle_raises_immediately()
    {
        var viewModel = new AnalysisRangeViewModel();
        viewModel.AttachMulti([Session(excludeTransitions: true), Session(excludeTransitions: true)]);
        AnalysisOptions? received = null;
        viewModel.MultiOptionsChanged += (_, options) => received = options;

        viewModel.ExcludeTransitionsEnabled = false;

        Assert.NotNull(received);
        Assert.False(received.ExcludeTransitions);
        Assert.Equal(0.0, received.TrimBufferSeconds);
    }

    [Fact]
    public void Attach_adopts_effective_mode_without_raising_changes()
    {
        var viewModel = new AnalysisRangeViewModel();
        var raised = 0;
        viewModel.OptionsChanged += (_, _) => raised++;

        viewModel.Attach(Session(excludeTransitions: false), null);

        Assert.Equal(0, raised);
        Assert.True(viewModel.IsEnabled);
        Assert.True(viewModel.AutoGpuThresholdEnabled);
        Assert.Equal(0.0, viewModel.TrimBufferSeconds);
        Assert.False(viewModel.ExcludeTransitionsEnabled);
        Assert.False(viewModel.FilteringControlsEnabled);
    }

    [Fact]
    public void Attach_without_a_session_disables_the_controls()
    {
        var viewModel = new AnalysisRangeViewModel();

        viewModel.Attach(null, null);

        Assert.False(viewModel.IsEnabled);
        Assert.False(viewModel.FilteringControlsEnabled);
        Assert.False(viewModel.ManualGpuThresholdEnabled);
    }

    [Fact]
    public void ApplyNow_raises_the_canonical_snapshot_once()
    {
        var viewModel = new AnalysisRangeViewModel
        {
            ExcludeTransitionsEnabled = false,
            GpuThreshold = 44.0,
        };
        AnalysisOptions? received = null;
        var raised = 0;
        viewModel.OptionsChanged += (_, options) =>
        {
            received = options;
            raised++;
        };

        viewModel.ApplyNow();

        Assert.Equal(1, raised);
        Assert.Equal(44.0, received!.GpuThreshold);
        Assert.Equal(0.0, received.TrimBufferSeconds);
        Assert.True(received.AutoGpuThreshold);
        Assert.False(received.ExcludeTransitions);
    }

    [Fact]
    public void Diagnostics_identify_the_complete_raw_data_mode()
    {
        var viewModel = new AnalysisRangeViewModel();
        var session = Session(excludeTransitions: false);

        viewModel.Attach(session, null);

        Assert.Equal(session.Bins.Count, session.ValidBins.Count);
        Assert.Equal(0, session.Diagnostics.BelowGpuBins);
        Assert.Equal(0, session.Diagnostics.FpsOutlierBins);
        Assert.Equal(0, session.Diagnostics.TransitionEdgeBins);
        Assert.Equal(0, session.Diagnostics.EdgeTrimmedBins);
        Assert.Contains("recorded frames", viewModel.AnalysisSummaryText);
        Assert.Contains("analyzed frames", viewModel.AnalysisSummaryText);
        Assert.Contains("chart samples", viewModel.AnalysisSummaryText);
        Assert.Contains("Raw data mode", viewModel.AnalysisSummaryText);
        Assert.Contains("No GPU gate", viewModel.FilterHelpText);
        Assert.Contains("Raw data", viewModel.FilterMethodText);
        Assert.Contains("Every recorded sample", viewModel.FilterMethodText);
    }

    [Fact]
    public void Diagnostics_identify_the_complete_precision_pipeline()
    {
        var viewModel = new AnalysisRangeViewModel();

        viewModel.Attach(Session(excludeTransitions: true), null);

        Assert.Contains("FrameView telemetry", viewModel.FilterHelpText);
        Assert.Contains("Precision filtering", viewModel.FilterMethodText);
        Assert.Contains("Automatic GPU gate", viewModel.FilterMethodText);
        Assert.Contains("multimetric validation", viewModel.FilterMethodText);
        Assert.True(viewModel.FilteringControlsEnabled);
    }

    private static SessionAnalysis Session(bool excludeTransitions)
    {
        var capture = CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            [
                ["0.0", "10.0", "80.0"],
                ["0.5", "10.0", "80.0"],
                ["1.0", "10.0", "80.0"],
                ["1.5", "10.0", "80.0"],
                ["2.0", "10.0", "80.0"],
                ["2.5", "10.0", "80.0"],
                ["3.0", "10.0", "80.0"],
                ["3.5", "10.0", "80.0"],
            ]);
        return new CaptureAnalysisService().Analyze(
            capture,
            new AnalysisOptions(
                GpuThreshold: 25,
                TrimBufferSeconds: excludeTransitions
                    ? AnalysisConstants.DefaultTrimBufferSeconds
                    : 0.0,
                AutoGpuThreshold: true,
                ExcludeTransitions: excludeTransitions));
    }

    private static CaptureData CaptureWith(string[] headers, string[][] rows)
    {
        var columns = new string[headers.Length][];
        for (var i = 0; i < headers.Length; i++)
        {
            columns[i] = new string[rows.Length];
            for (var r = 0; r < rows.Length; r++)
            {
                columns[i][r] = rows[r][i];
            }
        }

        return new CaptureData
        {
            Path = "capture.csv",
            DisplayName = "capture",
            Kind = CsvKind.Log,
            Headers = headers,
            Columns = columns,
        };
    }
}
