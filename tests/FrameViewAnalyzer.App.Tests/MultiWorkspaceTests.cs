using System.Windows;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class MultiWorkspaceTests
{
    [Fact]
    public void Chart_workspace_keeps_multi_order_and_renders_all_series_as_peers()
    {
        var first = Session("first.csv", frameTime: 20.0);
        var second = Session("second.csv", frameTime: 10.0);
        var third = Session("third.csv", frameTime: 8.0);
        var viewModel = new ChartViewModel();

        viewModel.SetWorkspace(
        [
            new ChartWorkspaceSession(first, "First"),
            new ChartWorkspaceSession(second, "Second"),
            new ChartWorkspaceSession(third, "Third"),
        ],
        isMultiWorkspace: true);

        Assert.True(viewModel.IsMultiWorkspace);
        Assert.Equal(3, viewModel.WorkspaceSessions.Count);
        Assert.Same(first, viewModel.WorkspaceSessions[0].Session);
        Assert.All(viewModel.WorkspaceSessions, item => Assert.False(item.IsReference));
        Assert.Collection(
            viewModel.SeriesList,
            series => Assert.Equal("First", series.Label),
            series => Assert.Equal("Second", series.Label),
            series => Assert.Equal("Third", series.Label));
        Assert.All(viewModel.SeriesList, series => Assert.False(series.IsReference));
        Assert.Null(viewModel.ComparisonSession);
    }

    [Fact]
    public void Multi_kpis_show_every_benchmark_and_only_the_winner_percentage()
    {
        var regular = Session("regular.csv", frameTime: 10.0); // 100 FPS
        var slow = Session("slow.csv", frameTime: 20.0);       // 50 FPS
        var fast = Session("fast.csv", frameTime: 5.0);        // 200 FPS
        var viewModel = new ChartViewModel();

        viewModel.SetWorkspace(
        [
            new ChartWorkspaceSession(regular, "Regular"),
            new ChartWorkspaceSession(slow, "Slow"),
            new ChartWorkspaceSession(fast, "Fast"),
        ],
        isMultiWorkspace: true);

        var average = viewModel.KpiTiles[0];
        Assert.Equal("AVERAGE", average.Label);
        Assert.Equal(3, average.SeriesValues.Count);
        Assert.Collection(
            average.SeriesValues,
            value =>
            {
                Assert.Equal("Regular", value.Label);
                Assert.Equal("100.0 FPS", value.Value);
                Assert.False(value.IsBest);
                Assert.Empty(value.DeltaText);
            },
            value =>
            {
                Assert.Equal("Slow", value.Label);
                Assert.Equal("50.0 FPS", value.Value);
                Assert.False(value.IsBest);
                Assert.Empty(value.DeltaText);
            },
            value =>
            {
                Assert.Equal("Fast", value.Label);
                Assert.Equal("200.0 FPS", value.Value);
                Assert.True(value.IsBest);
                Assert.Equal("+100.0%", value.DeltaText);
                Assert.False(value.HasComparedColor);
            });
        Assert.Equal(3, average.SeriesValues.Select(value => value.ColorHex).Distinct().Count());
    }

    [Fact]
    public void Multi_frame_points_recalculate_peer_kpis_without_counting_frames_as_seconds()
    {
        var viewModel = new ChartViewModel();
        viewModel.SetWorkspace(
        [
            new ChartWorkspaceSession(Session("first.csv", frameTime: 10.0), "First"),
            new ChartWorkspaceSession(Session("second.csv", frameTime: 20.0), "Second"),
            new ChartWorkspaceSession(Session("third.csv", frameTime: 5.0), "Third"),
        ],
        isMultiWorkspace: true);
        var frameX = Enumerable.Range(0, 20).Select(index => index / 4.0).ToArray();
        var frameSeries = viewModel.SeriesList
            .Select((series, index) => series with
            {
                X = frameX,
                Y = Enumerable.Repeat(30.0 * (index + 1), frameX.Length).ToArray(),
            })
            .ToList();

        viewModel.SetFramePointSeries(frameSeries);

        Assert.Collection(
            viewModel.KpiTiles[0].SeriesValues,
            value => Assert.Equal("30.0 FPS", value.Value),
            value => Assert.Equal("60.0 FPS", value.Value),
            value => Assert.Equal("90.0 FPS", value.Value));
        Assert.All(
            viewModel.KpiTiles[^1].SeriesValues,
            value => Assert.Equal("5 s", value.Value));

        viewModel.ClearFramePointSeries();

        Assert.Equal("100.0 FPS", viewModel.KpiTiles[0].SeriesValues[0].Value);
        Assert.Equal("50.0 FPS", viewModel.KpiTiles[0].SeriesValues[1].Value);
        Assert.Equal("200.0 FPS", viewModel.KpiTiles[0].SeriesValues[2].Value);
    }

    [Fact]
    public void Multi_lower_is_better_winner_percentage_is_negative()
    {
        var regular = Session("regular.csv", frameTime: 11.0);
        var best = Session("best.csv", frameTime: 9.0);
        var slow = Session("slow.csv", frameTime: 12.8);
        var viewModel = new ChartViewModel();

        viewModel.SetWorkspace(
        [
            new ChartWorkspaceSession(regular, "Regular"),
            new ChartWorkspaceSession(best, "Best"),
            new ChartWorkspaceSession(slow, "Slow"),
        ],
        isMultiWorkspace: true);

        viewModel.SelectedMetric = viewModel.Metrics.Single(metric => metric.Id == "frametime");

        var average = viewModel.KpiTiles[0];
        var winner = average.SeriesValues.Single(value => value.Label == "Best");
        Assert.Equal("9.0 ms", winner.Value);
        Assert.True(winner.IsBest);
        Assert.Equal("-18.2%", winner.DeltaText);
        Assert.False(winner.HasComparedColor);
    }

    [Fact]
    public void Multi_with_two_sessions_stays_multi_instead_of_falling_back_to_pair()
    {
        var first = Session("first.csv", frameTime: 10.0);
        var second = Session("second.csv", frameTime: 20.0);
        var viewModel = new ChartViewModel();

        viewModel.SetWorkspace(
        [
            new ChartWorkspaceSession(first, "First"),
            new ChartWorkspaceSession(second, "Second"),
        ],
        isMultiWorkspace: true);

        Assert.True(viewModel.IsMultiWorkspace);
        Assert.Null(viewModel.ComparisonSession);
        Assert.Equal(2, viewModel.KpiTiles[0].SeriesValues.Count);
        Assert.DoesNotContain("→", viewModel.KpiTiles[0].Value);
    }

    [Fact]
    public void Pair_adapter_keeps_the_existing_two_session_contract()
    {
        var baseSession = Session("base.csv", frameTime: 10.0);
        var comparison = Session("comparison.csv", frameTime: 20.0);
        var viewModel = new ChartViewModel();

        viewModel.SetSessions(baseSession, comparison);

        Assert.False(viewModel.IsMultiWorkspace);
        Assert.Equal(2, viewModel.SeriesList.Count);
        Assert.Same(baseSession, viewModel.Session);
        Assert.Same(comparison, viewModel.ComparisonSession);
        Assert.Contains("→", viewModel.KpiTiles[0].Value);
        Assert.Empty(viewModel.KpiTiles[0].SeriesValues);
    }

    [Fact]
    public void Multi_selector_restores_checked_paths_without_reference_state() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var captures = new[]
            {
                new CaptureOption(@"C:\captures\a.csv", "A"),
                new CaptureOption(@"C:\captures\b.csv", "B"),
                new CaptureOption(@"C:\captures\c.csv", "C"),
            };
            var window = new MultiBenchmarkSelectionWindow(
                captures,
                [captures[0].Path, captures[2].Path]);
            try
            {
                Assert.Equal(2, window.SelectedPaths.Count);
                Assert.Contains(captures[0].Path, window.SelectedPaths);
                Assert.Contains(captures[2].Path, window.SelectedPaths);
            }
            finally
            {
                window.Close();
            }
        });

    private static SessionAnalysis Session(string path, double frameTime)
    {
        var rows = new List<string[]>();
        for (var second = 0; second < 5; second++)
        {
            foreach (var offset in new[] { 0.0, 0.25, 0.5 })
            {
                rows.Add(
                [
                    (second + offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    frameTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "80.0",
                ]);
            }
        }

        return new CaptureAnalysisService().Analyze(
            new CaptureData
            {
                Path = path,
                DisplayName = System.IO.Path.GetFileNameWithoutExtension(path),
                Kind = CsvKind.Log,
                Headers = ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
                Columns =
                [
                    rows.Select(row => row[0]).ToArray(),
                    rows.Select(row => row[1]).ToArray(),
                    rows.Select(row => row[2]).ToArray(),
                ],
            },
            new AnalysisOptions(
                GpuThreshold: 10,
                TrimBufferSeconds: 0,
                AutoGpuThreshold: false,
                ExcludeTransitions: false));
    }
}
