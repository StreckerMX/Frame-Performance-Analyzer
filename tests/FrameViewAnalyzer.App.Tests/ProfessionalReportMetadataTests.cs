using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class ProfessionalReportMetadataTests
{
    [Fact]
    public void Custom_user_title_keeps_professional_layout_when_explicitly_requested()
    {
        var header = new ReportPlotBuilder.ReportHeader(
            "BENCHMARK CRIMSON DESERT",
            [],
            UseProfessionalLayout: true);

        Assert.True(ReportPlotBuilder.ShouldUseProfessionalLayout(header));
    }

    [Fact]
    public void Arbitrary_legacy_title_remains_legacy_without_explicit_report_semantics()
    {
        var header = new ReportPlotBuilder.ReportHeader("Night Run", []);

        Assert.False(ReportPlotBuilder.ShouldUseProfessionalLayout(header));
    }

    [Fact]
    public void Manual_metadata_lines_include_configuration_driver_tags_and_notes()
    {
        var metadata = new ManualMetadata(
            GraphicsPreset: "Optimized",
            Upscaler: "DLSS",
            UpscalerQuality: "Quality",
            FrameGeneration: "FG x4",
            RayTracing: "Full RT",
            DriverVersion: "581.29",
            Notes: "No background apps",
            Tags: ["clean", "comparison"]);

        var lines = ReportPlotBuilder.ReportManualMetadataLines(metadata);

        Assert.Equal(3, lines.Count);
        Assert.Contains("Optimized", lines[0]);
        Assert.Contains("DLSS Quality", lines[0]);
        Assert.Contains("FG x4", lines[0]);
        Assert.Contains("Full RT", lines[0]);
        Assert.Contains("Driver 581.29", lines[1]);
        Assert.Contains("Tags: clean, comparison", lines[1]);
        Assert.Equal("Notes: No background apps", lines[2]);
    }
}
