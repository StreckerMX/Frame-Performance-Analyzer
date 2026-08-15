using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class SessionDetailsViewModelTests
{
    [Fact]
    public void Sections_follow_the_reference_hierarchy()
    {
        var viewModel = new SessionDetailsViewModel(Session());

        Assert.Equal("Complete data · capture", viewModel.Title);
        Assert.Equal(
            "How the results were obtained",
            viewModel.Sections[0].Title);
        Assert.Equal("Benchmark identity", viewModel.Sections[1].Title);
        Assert.Equal("System used", viewModel.Sections[2].Title);
        Assert.Equal("Frame presentation", viewModel.Sections[3].Title);
        Assert.Contains(viewModel.Sections, section => section.Title.StartsWith("Telemetry · "));
    }

    [Fact]
    public void Missing_columns_render_as_em_dash()
    {
        var viewModel = new SessionDetailsViewModel(Session());

        var system = viewModel.Sections.Single(section => section.Title == "System used");
        Assert.All(
            system.Rows,
            row => Assert.False(string.IsNullOrEmpty(row.Value)));
    }

    [Fact]
    public void Presentation_values_are_humanized()
    {
        var viewModel = new SessionDetailsViewModel(Session());

        var presentation = viewModel.Sections.Single(section => section.Title == "Frame presentation");
        var tearing = presentation.Rows.Single(row => row.Label == "Tearing allowed");
        var sync = presentation.Rows.Single(row => row.Label == "Synchronization interval");

        Assert.Equal("Not allowed", tearing.Value);
        Assert.Equal("0 · no mandatory V-SYNC wait", sync.Value);
    }

    [Fact]
    public void Identity_section_shows_the_capture_context()
    {
        var viewModel = new SessionDetailsViewModel(Session());

        var identity = viewModel.Sections.Single(section => section.Title == "Benchmark identity");
        Assert.Equal("GTA5.exe", identity.Rows.Single(row => row.Label == "Application").Value);
        Assert.Equal("4K", identity.Rows.Single(row => row.Label == "Resolution").Value);
    }

    private static SessionAnalysis Session() =>
        new CaptureAnalysisService().Analyze(CaptureWith(
            [
                "TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)",
                "Application", "Resolution", "GPU", "CPU",
                "Operating system", "Tearing allowed", "Synchronization interval",
            ],
            [
                ["0.0", "10.0", "80.0", "GTA5.exe", "4K", "RTX 5090", "Ryzen 7", "Windows 11", "0", "0"],
                ["0.5", "10.0", "80.0", "GTA5.exe", "4K", "RTX 5090", "Ryzen 7", "Windows 11", "0", "0"],
                ["1.0", "10.0", "80.0", "GTA5.exe", "4K", "RTX 5090", "Ryzen 7", "Windows 11", "0", "0"],
                ["1.5", "10.0", "80.0", "GTA5.exe", "4K", "RTX 5090", "Ryzen 7", "Windows 11", "0", "0"],
                ["2.0", "10.0", "80.0", "GTA5.exe", "4K", "RTX 5090", "Ryzen 7", "Windows 11", "0", "0"],
                ["2.5", "10.0", "80.0", "GTA5.exe", "4K", "RTX 5090", "Ryzen 7", "Windows 11", "0", "0"],
            ]));

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
