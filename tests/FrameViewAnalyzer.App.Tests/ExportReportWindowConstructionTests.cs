using System.Windows;
using System.Windows.Controls;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage that actually CONSTRUCTS the Export PNG report window
/// (InitializeComponent included) — the previous helper-level tests could not
/// catch the XAML initialization-order crash. All scenarios run on the shared
/// STA test host so the test Application and its theme resources are safe.
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
            NoSessionsConstructs();
            SelectionToggleUpdatesExportState();
        });

    private static void BaseOnlyConstructs()
    {
        var window = new ExportReportWindow(
            [new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", Session())]);
        try
        {
            var combo = (ComboBox)window.FindName("SessionOptions");
            var options = Assert.IsAssignableFrom<IReadOnlyList<ExportSessionOption>>(combo.ItemsSource);
            Assert.Single(options);
            Assert.Equal(0, combo.SelectedIndex);
            Assert.False(string.IsNullOrWhiteSpace(options[0].Label));
            Assert.Equal("Base — GTA5 Enhanced", options[0].Label);
        }
        finally
        {
            window.Close();
        }
    }

    private static void BaseAndComparisonConstructs()
    {
        var window = new ExportReportWindow(
        [
            new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", Session()),
            new ExportSessionOption(SessionRole.Comparison, "GTA5 Enhanced", Session()),
        ]);
        try
        {
            var combo = (ComboBox)window.FindName("SessionOptions");
            var options = Assert.IsAssignableFrom<IReadOnlyList<ExportSessionOption>>(combo.ItemsSource);
            Assert.Equal(2, options.Count);
            Assert.Equal("Base — GTA5 Enhanced", options[0].Label);
            Assert.Equal("Comparison — GTA5 Enhanced", options[1].Label);
            Assert.Equal(0, combo.SelectedIndex);
        }
        finally
        {
            window.Close();
        }
    }

    private static void NoSessionsConstructs()
    {
        var window = new ExportReportWindow([]);
        try
        {
            var exportButton = (Button)window.FindName("ExportButton");
            Assert.True(exportButton.IsEnabled, "All-sessions mode stays valid without sessions.");

            var single = (RadioButton)window.FindName("SingleRadio");
            single.IsChecked = true;
            Assert.False(exportButton.IsEnabled,
                "Selected-session export must be disabled without a selection.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void SelectionToggleUpdatesExportState()
    {
        var window = new ExportReportWindow(
        [
            new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", Session()),
            new ExportSessionOption(SessionRole.Comparison, "GTA5 Enhanced", Session()),
        ]);
        try
        {
            var exportButton = (Button)window.FindName("ExportButton");
            var single = (RadioButton)window.FindName("SingleRadio");
            var combo = (ComboBox)window.FindName("SessionOptions");

            single.IsChecked = true;
            Assert.True(exportButton.IsEnabled, "A valid selection enables the export.");

            combo.SelectedItem = null;
            Assert.False(exportButton.IsEnabled, "Clearing the selection disables the export.");
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
