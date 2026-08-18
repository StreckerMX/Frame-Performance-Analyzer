using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class ExportSessionOptionTests
{
    [Fact]
    public void Session_labels_remain_role_aware_and_path_free()
    {
        var session = Session();
        var baseOption = new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", session);
        var comparisonOption = new ExportSessionOption(SessionRole.Comparison, "GTA5 Enhanced", session);

        Assert.Equal("Base — GTA5 Enhanced", baseOption.Label);
        Assert.Equal("Comparison — GTA5 Enhanced", comparisonOption.Label);
        Assert.DoesNotContain(@"C:\", baseOption.Label);
        Assert.DoesNotContain('\\', comparisonOption.Label);
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, true)]
    [InlineData(2, 8, true)]
    [InlineData(1, 9, false)]
    public void Checklist_validation_requires_sessions_and_one_to_eight_metrics(
        int sessions,
        int metrics,
        bool expected)
    {
        Assert.Equal(expected, ExportReportWindow.CanExport(sessions, metrics));
    }

    [Fact]
    public void Build_selection_returns_only_checked_sessions_and_metrics()
    {
        var baseOption = new ExportSessionOption(SessionRole.Base, "Base run", Session());
        var comparisonOption = new ExportSessionOption(SessionRole.Comparison, "Comparison run", Session());
        var sessions = new[]
        {
            new ExportSessionChecklistItem(baseOption, isSelected: false),
            new ExportSessionChecklistItem(comparisonOption, isSelected: true),
        };
        var metrics = new[]
        {
            new ExportMetricChecklistItem(CoreMetricCatalog.CoreById["fps"], isSelected: true),
            new ExportMetricChecklistItem(CoreMetricCatalog.CoreById["frametime"], isSelected: false),
        };

        var selection = ExportReportWindow.BuildSelection(sessions, metrics);

        var selectedSession = Assert.Single(selection.Sessions);
        Assert.Same(comparisonOption, selectedSession);
        Assert.Equal(["fps"], selection.MetricIds);
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
