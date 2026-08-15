using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class CaptureLabelBuilderTests
{
    private static CaptureInfo Info(
        string fileName = "FrameView_A.exe_2026_08_13T033633_Log.csv",
        double? durationSeconds = 337.0) => new(
            Path: fileName,
            Name: "A.exe_2026_08_13T033633",
            Application: "GTA5 Enhanced",
            Resolution: "2560x1440",
            Gpu: "NVIDIA GeForce RTX 5070 Ti",
            Cpu: "AMD Ryzen 7 5700X3D 8-Core Processor",
            DurationSeconds: durationSeconds);

    [Fact]
    public void Label_contains_real_metadata_and_stamp()
    {
        var label = CaptureLabelBuilder.BuildLabel(Info());

        Assert.Contains("GTA5 Enhanced", label);
        Assert.Contains("2560x1440", label);
        Assert.Contains("2026-08-13 03:36", label);
        Assert.Contains("5 min 37 s", label);
    }

    [Fact]
    public void Same_game_runs_get_distinct_labels()
    {
        var first = CaptureLabelBuilder.BuildLabel(Info());
        var second = CaptureLabelBuilder.BuildLabel(
            Info(
                "FrameView_A.exe_2026_08_13T071536_Log.csv",
                durationSeconds: 106.0));

        Assert.NotEqual(first, second);
        Assert.Contains("2026-08-13 07:15", second);
    }

    [Fact]
    public void Label_never_exceeds_maximum_length()
    {
        var label = CaptureLabelBuilder.BuildLabel(Info(), maximumLength: 60);

        Assert.True(label.Length <= 60, $"label was '{label}'");
        Assert.Contains("2026-08-13 03:36", label);
    }

    [Fact]
    public void Stamp_comes_from_the_frameview_filename()
    {
        Assert.Equal(
            "2026-08-13 03:36",
            CaptureLabelBuilder.CaptureStamp(Info()));
    }
}
