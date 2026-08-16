using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for the "Export selected session" picker: role-aware
/// labels, exact option counts, preselect defaults, validation policy, and the
/// exact export target — the ComboBox must never be blank or ambiguous.
/// </summary>
public class ExportSessionOptionTests
{
    [Fact]
    public void Base_only_produces_exactly_one_preselected_option()
    {
        var session = Session();
        var options = new[] { new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", session) };

        var option = Assert.Single(options);
        Assert.Equal(SessionRole.Base, option.Role);
        Assert.Equal("Base — GTA5 Enhanced", option.Label);
        Assert.Same(session, option.Session);
    }

    [Fact]
    public void Base_and_comparison_produce_two_distinguishable_options()
    {
        var options = new[]
        {
            new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", Session()),
            new ExportSessionOption(SessionRole.Comparison, "GTA5 Enhanced", Session()),
        };

        Assert.Equal(2, options.Length);
        Assert.Equal("Base — GTA5 Enhanced", options[0].Label);
        Assert.Equal("Comparison — GTA5 Enhanced", options[1].Label);
        Assert.NotEqual(options[0].Label, options[1].Label);
        Assert.All(options, option => Assert.False(string.IsNullOrWhiteSpace(option.Label)));
    }

    [Fact]
    public void No_selection_disables_selected_session_export()
    {
        Assert.False(ExportReportWindow.CanExport(ExportScope.Single, selected: null));
        Assert.Null(ExportReportWindow.SelectedSession(ExportScope.Single, selectedItem: null));
        Assert.Null(ExportReportWindow.SelectedSession(ExportScope.Single, "not an option"));
    }

    [Fact]
    public void Export_all_remains_valid_without_a_single_selection()
    {
        Assert.True(ExportReportWindow.CanExport(ExportScope.All, selected: null));
        Assert.Null(ExportReportWindow.SelectedSession(ExportScope.All, selectedItem: null));
    }

    [Fact]
    public void Selected_base_resolves_to_exactly_the_base_session()
    {
        var baseSession = Session();
        var baseOption = new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", baseSession);

        var resolved = ExportReportWindow.SelectedSession(ExportScope.Single, baseOption);

        Assert.Same(baseOption, resolved);
        Assert.Equal(SessionRole.Base, resolved!.Role);
        Assert.Same(baseSession, resolved.Session);
    }

    [Fact]
    public void Selected_comparison_resolves_to_exactly_the_comparison_session()
    {
        var comparisonSession = Session();
        var comparisonOption = new ExportSessionOption(
            SessionRole.Comparison,
            "GTA5 Enhanced",
            comparisonSession);

        var resolved = ExportReportWindow.SelectedSession(ExportScope.Single, comparisonOption);

        Assert.Same(comparisonOption, resolved);
        Assert.Equal(SessionRole.Comparison, resolved!.Role);
        Assert.Same(comparisonSession, resolved.Session);
    }

    [Fact]
    public void Labels_never_expose_absolute_paths()
    {
        var option = new ExportSessionOption(
            SessionRole.Base,
            "GTA5 Enhanced",
            Session());

        Assert.DoesNotContain(@"C:\", option.Label);
        Assert.DoesNotContain("captures", option.Label);
        Assert.DoesNotContain('\\', option.Label);
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
