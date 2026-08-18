using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage that constructs the real checklist-based Export PNG
/// report window, including InitializeComponent, title editing, and defaults.
/// </summary>
public class ExportReportWindowConstructionTests
{
    [Fact]
    public void Export_report_dialog_constructs_in_all_session_states() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            BaseOnlyConstructs();
            BaseAndComparisonConstructs();
            MultiConstructsWithMultiTitle();
            NoSessionsDisablesExport();
        });

    private static void BaseOnlyConstructs()
    {
        var session = Session();
        var window = new ExportReportWindow(
            [new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", session)],
            session.Catalog);
        try
        {
            var sessionList = (ItemsControl)window.FindName("SessionChecklist");
            var sessionItems = Assert.IsAssignableFrom<IReadOnlyList<ExportSessionChecklistItem>>(
                sessionList.ItemsSource);
            var selectedSession = Assert.Single(sessionItems);
            Assert.True(selectedSession.IsSelected);
            Assert.Equal("Base — GTA5 Enhanced", selectedSession.Label);

            var metricList = (ItemsControl)window.FindName("MetricChecklist");
            var metricItems = Assert.IsAssignableFrom<IReadOnlyList<ExportMetricChecklistItem>>(
                metricList.ItemsSource);
            Assert.Contains(metricItems, item => item.Metric.Id == "fps" && item.IsSelected);
            Assert.Equal(
                ExportReportTitles.PairReportTitle,
                ((TextBox)window.FindName("ReportTitleTextBox")).Text);
            Assert.True(((Button)window.FindName("ExportButton")).IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    private static void BaseAndComparisonConstructs()
    {
        var baseSession = Session();
        var comparisonSession = Session();
        var window = new ExportReportWindow(
        [
            new ExportSessionOption(SessionRole.Base, "Base run", baseSession),
            new ExportSessionOption(SessionRole.Comparison, "Comparison run", comparisonSession),
        ],
        baseSession.Catalog);
        try
        {
            var sessionList = (ItemsControl)window.FindName("SessionChecklist");
            var sessionItems = Assert.IsAssignableFrom<IReadOnlyList<ExportSessionChecklistItem>>(
                sessionList.ItemsSource);
            Assert.Equal(2, sessionItems.Count);
            Assert.All(sessionItems, item => Assert.True(item.IsSelected));
            Assert.Equal("Base — Base run", sessionItems[0].Label);
            Assert.Equal("Comparison — Comparison run", sessionItems[1].Label);

            var titleBox = (TextBox)window.FindName("ReportTitleTextBox");
            Assert.Equal(ExportReportTitles.PairReportTitle, titleBox.Text);
            titleBox.Text = "MY CUSTOM BENCHMARK TITLE";

            ExportReportSelection? requested = null;
            window.ExportRequested += selection => requested = selection;
            ((Button)window.FindName("ExportButton"))
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.NotNull(requested);
            Assert.Equal("MY CUSTOM BENCHMARK TITLE", requested!.ReportTitle);
        }
        finally
        {
            window.Close();
        }
    }

    private static void MultiConstructsWithMultiTitle()
    {
        var first = Session();
        var second = Session();
        var window = new ExportReportWindow(
        [
            new ExportSessionOption(
                SessionRole.Comparison,
                "Run A",
                first,
                WorkspaceIndex: 0,
                IsMultiPeer: true),
            new ExportSessionOption(
                SessionRole.Comparison,
                "Run B",
                second,
                WorkspaceIndex: 1,
                IsMultiPeer: true),
        ],
        first.Catalog);
        try
        {
            Assert.Equal(
                ExportReport.MultiReportTitle,
                ((TextBox)window.FindName("ReportTitleTextBox")).Text);
        }
        finally
        {
            window.Close();
        }
    }

    private static void NoSessionsDisablesExport()
    {
        var session = Session();
        var window = new ExportReportWindow([], session.Catalog);
        try
        {
            Assert.False(((Button)window.FindName("ExportButton")).IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    private static SessionAnalysis Session() =>
        new CaptureAnalysisService().Analyze(
            CaptureWith(
                ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
                [
                    ["0.0", "10.0", "80.0"],
                    ["0.5", "10.0", "80.0"],
                    ["1.0", "10.0", "80.0"],
                    ["1.5", "10.0", "80.0"],
                ]),
            new AnalysisOptions(
                GpuThreshold: 25,
                TrimBufferSeconds: 1,
                AutoGpuThreshold: false,
                ExcludeTransitions: false));

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
