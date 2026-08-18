using System.Windows;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class MultiWorkspaceTests
{
    [Fact]
    public void Chart_workspace_normalizes_reference_and_renders_all_series()
    {
        var first = Session("first.csv", frameTime: 20.0);
        var reference = Session("reference.csv", frameTime: 10.0);
        var third = Session("third.csv", frameTime: 8.0);
        var viewModel = new ChartViewModel();

        viewModel.SetWorkspace(
        [
            new ChartWorkspaceSession(first, "First"),
            new ChartWorkspaceSession(reference, "Reference", IsReference: true),
            new ChartWorkspaceSession(third, "Third"),
        ]);

        Assert.True(viewModel.IsMultiWorkspace);
        Assert.Equal(3, viewModel.WorkspaceSessions.Count);
        Assert.Same(reference, viewModel.WorkspaceSessions[0].Session);
        Assert.True(viewModel.WorkspaceSessions[0].IsReference);
        Assert.Equal(["Reference", "First", "Third"],
            viewModel.SeriesList.Select(series => series.Label).ToArray());
        Assert.Null(viewModel.ComparisonSession);
    }

    [Fact]
    public void Multi_kpis_describe_the_reference_not_an_arbitrary_comparison()
    {
        var reference = Session("reference.csv", frameTime: 10.0); // 100 FPS
        var slow = Session("slow.csv", frameTime: 20.0);           // 50 FPS
        var fast = Session("fast.csv", frameTime: 5.0);            // 200 FPS
        var viewModel = new ChartViewModel();

        viewModel.SetWorkspace(
        [
            new ChartWorkspaceSession(reference, "Reference", IsReference: true),
            new ChartWorkspaceSession(slow, "Slow"),
            new ChartWorkspaceSession(fast, "Fast"),
        ]);

        Assert.Equal("AVERAGE", viewModel.KpiTiles[0].Label);
        Assert.Equal("100.0", viewModel.KpiTiles[0].Value);
        Assert.Empty(viewModel.KpiTiles[0].DeltaText);
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
    }

    [Fact]
    public void Multi_selector_restores_checked_paths_and_reference() =>
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
                [captures[0].Path, captures[2].Path],
                captures[2].Path);
            try
            {
                Assert.Equal(2, window.SelectedPaths.Count);
                Assert.Equal(captures[2].Path, window.ReferencePath);
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
