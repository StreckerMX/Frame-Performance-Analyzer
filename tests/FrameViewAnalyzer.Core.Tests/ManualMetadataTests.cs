using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Core.Tests;

public class ManualMetadataTests
{
    [Fact]
    public void Default_metadata_is_empty()
    {
        var metadata = new ManualMetadata();

        Assert.True(metadata.IsEmpty);
        Assert.Null(metadata.ConfigLine);
        Assert.Empty(metadata.Tags);
    }

    [Fact]
    public void Config_line_joins_the_configuration_fields()
    {
        var metadata = new ManualMetadata(
            BenchmarkName: "RTX Run",
            Game: "GTA V",
            Resolution: "4K",
            GraphicsPreset: "Very High",
            Upscaler: "DLSS",
            UpscalerQuality: "Quality",
            FrameGeneration: "FG x2",
            RayTracing: "RT",
            DriverVersion: "560.70",
            Notes: "not part of the line");

        Assert.Equal("4K · Very High · DLSS Quality · FG x2 · RT", metadata.ConfigLine);
        Assert.False(metadata.IsEmpty);
    }

    [Fact]
    public void Config_line_omits_missing_fields()
    {
        var metadata = new ManualMetadata(Resolution: "2560x1440");

        Assert.Equal("2560x1440", metadata.ConfigLine);
    }

    [Fact]
    public void Config_line_is_null_for_non_configuration_fields()
    {
        var metadata = new ManualMetadata(BenchmarkName: "Run 1", Notes: "notes", Tags: ["gpu"]);

        Assert.Null(metadata.ConfigLine);
        Assert.False(metadata.IsEmpty);
    }
}
