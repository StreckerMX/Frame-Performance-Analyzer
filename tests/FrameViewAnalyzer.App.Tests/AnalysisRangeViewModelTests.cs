using System.Windows.Threading;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class AnalysisRangeViewModelTests
{
    [Fact]
    public void Snapshot_options_reflect_the_controls()
    {
        var viewModel = new AnalysisRangeViewModel
        {
            AutoGpuThresholdEnabled = false,
            GpuThreshold = 37.0,
            TrimBufferSeconds = 2.5,
            ExcludeTransitionsEnabled = false,
        };

        var options = viewModel.SnapshotOptions();

        Assert.False(options.AutoGpuThreshold);
        Assert.Equal(37.0, options.GpuThreshold);
        Assert.Equal(2.5, options.TrimBufferSeconds);
        Assert.False(options.ExcludeTransitions);
    }

    [Fact]
    public void Snapshot_options_clamps_to_the_reference_bounds()
    {
        var viewModel = new AnalysisRangeViewModel
        {
            GpuThreshold = 400.0,
            TrimBufferSeconds = -3.0,
        };

        var options = viewModel.SnapshotOptions();

        Assert.Equal(80.0, options.GpuThreshold);
        Assert.Equal(0.0, options.TrimBufferSeconds);
    }

    [Fact]
    public void Manual_slider_is_only_enabled_when_auto_mode_is_off()
    {
        var viewModel = new AnalysisRangeViewModel();

        Assert.False(viewModel.ManualGpuThresholdEnabled);

        // The helper session is analyzed with auto mode off, so attaching it
        // enables the manual slider.
        viewModel.Attach(Session(), null);
        Assert.True(viewModel.ManualGpuThresholdEnabled);

        viewModel.AutoGpuThresholdEnabled = true;
        Assert.False(viewModel.ManualGpuThresholdEnabled);
    }

    [Fact]
    public void Attach_adopts_effective_options_without_raising_changes()
    {
        var viewModel = new AnalysisRangeViewModel();
        var raised = 0;
        viewModel.OptionsChanged += (_, _) => raised++;
        var session = Session();

        viewModel.Attach(session, null);

        Assert.Equal(0, raised);
        Assert.True(viewModel.IsEnabled);
        Assert.False(viewModel.AutoGpuThresholdEnabled);
        Assert.Equal(25.0, viewModel.GpuThreshold);
        Assert.Equal(2.0, viewModel.TrimBufferSeconds);
        Assert.False(viewModel.ExcludeTransitionsEnabled);
    }

    [Fact]
    public void Attach_without_a_session_disables_the_controls()
    {
        var viewModel = new AnalysisRangeViewModel();

        viewModel.Attach(null, null);

        Assert.False(viewModel.IsEnabled);
        Assert.False(viewModel.ManualGpuThresholdEnabled);
    }

    [Fact]
    public void ApplyNow_raises_the_snapshot_once()
    {
        var viewModel = new AnalysisRangeViewModel();
        AnalysisOptions? received = null;
        var raised = 0;
        viewModel.OptionsChanged += (_, options) =>
        {
            received = options;
            raised++;
        };
        viewModel.GpuThreshold = 44.0;

        viewModel.ApplyNow();

        Assert.Equal(1, raised);
        Assert.Equal(44.0, received!.GpuThreshold);
    }

    [Fact]
    public void Rapid_changes_within_one_interval_fire_once_with_the_last_values()
    {
        var viewModel = new AnalysisRangeViewModel();
        viewModel.Attach(Session(), null);

        var events = new List<AnalysisOptions>();
        viewModel.OptionsChanged += (_, options) => events.Add(options);

        // Several changes inside one debounce interval must collapse into a
        // single trailing-edge OptionsChanged carrying the last values.
        viewModel.GpuThreshold = 15.0;
        viewModel.TrimBufferSeconds = 3.0;
        viewModel.GpuThreshold = 42.0;

        // Pump the dispatcher until the debounce tick fires; a generous
        // timeout timer only guards against a pathological hang.
        var frame = new DispatcherFrame();
        viewModel.OptionsChanged += (_, _) => frame.Continue = false;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            frame.Continue = false;
        };
        timeout.Start();
        Dispatcher.PushFrame(frame);

        var options = Assert.Single(events);
        Assert.Equal(42.0, options.GpuThreshold);
        Assert.Equal(3.0, options.TrimBufferSeconds);
    }

    [Fact]
    public void Diagnostics_reflect_transition_and_threshold_exclusions()
    {
        var viewModel = new AnalysisRangeViewModel();
        var session = Session();

        viewModel.Attach(session, null);

        Assert.Contains("analyzed", viewModel.AnalysisSummaryText);
        Assert.Contains(
            viewModel.AutoGpuThresholdEnabled ? "90th percentile" : "GPU utilization",
            viewModel.FilterHelpText);
    }

    private static SessionAnalysis Session()
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
                TrimBufferSeconds: 2,
                AutoGpuThreshold: false,
                ExcludeTransitions: false));
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
