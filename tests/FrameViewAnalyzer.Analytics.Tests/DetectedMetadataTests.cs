using System.Globalization;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Tests;

/// <summary>
/// Parity tests for the detected manual-metadata prefill values, ported
/// from the Python detected_field_values behavior.
/// </summary>
public class DetectedMetadataTests
{
    private static SessionAnalysis SessionOf(
        params (string Header, string[] Values)[] columns)
    {
        var headers = new List<string> { "TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)" };
        headers.AddRange(columns.Select(column => column.Header));
        var offsets = new[] { 0.0, 0.25, 0.5 };
        var rows = new List<string[]>();
        for (var second = 0; second < 4; second++)
        {
            foreach (var offset in offsets)
            {
                var row = new List<string>
                {
                    (second + offset).ToString(CultureInfo.InvariantCulture),
                    "10.0",
                    "80.0",
                };
                foreach (var column in columns)
                {
                    var valueIndex = System.Math.Min(rows.Count, column.Values.Length - 1);
                    row.Add(column.Values[valueIndex]);
                }

                rows.Add(row.ToArray());
            }
        }

        return new CaptureAnalysisService().Analyze(
            TestCapture.CaptureWith(headers, rows));
    }

    private static (string, string[]) Column(string header, params string[] values) =>
        (header, values);

    [Fact]
    public void Game_uses_the_application_name()
    {
        var session = SessionOf(Column("Application", "Game.exe"));

        var values = DetectedMetadata.DetectFieldValues(session);

        Assert.Equal("Game", values["game"]);
    }

    [Fact]
    public void Game_falls_back_to_the_display_name()
    {
        var session = SessionOf();

        var values = DetectedMetadata.DetectFieldValues(session);

        Assert.Equal("capture", values["game"]);
    }

    [Fact]
    public void Resolution_and_driver_version_are_detected()
    {
        var session = SessionOf(
            Column("Resolution", "3840x2160"),
            Column("GPU Base Driver", "560.70"));

        var values = DetectedMetadata.DetectFieldValues(session);

        Assert.Equal("3840x2160", values["resolution"]);
        Assert.Equal("560.70", values["driver_version"]);
    }

    [Fact]
    public void Dlss_is_detected_with_its_quality_mode()
    {
        var session = SessionOf(
            Column("DLSS", "Enabled"),
            Column("DLSS Mode", "Quality"));

        var values = DetectedMetadata.DetectFieldValues(session);

        Assert.Equal("DLSS", values["upscaler"]);
        Assert.Equal("Quality", values["upscaler_quality"]);
    }

    [Fact]
    public void Disabled_dlss_is_not_detected()
    {
        var session = SessionOf(Column("DLSS", "Disabled"));

        var values = DetectedMetadata.DetectFieldValues(session);

        Assert.False(values.ContainsKey("upscaler"));
    }

    [Fact]
    public void Frame_generation_badge_comes_from_the_multiplier()
    {
        var session = SessionOf(Column("Frame Gen Multiplier", "2.0"));

        var values = DetectedMetadata.DetectFieldValues(session);

        Assert.Equal("x2", values["frame_generation"]);
    }

    [Fact]
    public void Disabled_frame_generation_is_not_detected()
    {
        var session = SessionOf(Column("Frame Gen Multiplier", "1.0"));

        var values = DetectedMetadata.DetectFieldValues(session);

        Assert.False(values.ContainsKey("frame_generation"));
    }

    [Fact]
    public void Ray_reconstruction_is_detected_from_the_column()
    {
        var enabled = SessionOf(Column("Ray Reconstruction", "Enabled"));
        var disabled = SessionOf(Column("Ray Reconstruction", "Disabled"));

        var enabledValues = DetectedMetadata.DetectFieldValues(enabled);
        var disabledValues = DetectedMetadata.DetectFieldValues(disabled);

        Assert.Equal("Ray Reconstruction", enabledValues["ray_tracing"]);
        Assert.False(disabledValues.ContainsKey("ray_tracing"));
    }
}
