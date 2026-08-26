using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class MultiBenchmarkSelectionViewModelTests
{
    [Fact]
    public void Choice_surfaces_timestamp_duration_resolution_and_hardware()
    {
        const string path = "C:/Captures/FrameView_helldivers2.exe_2026_08_18T222313_Log.csv";
        var choice = new MultiBenchmarkChoiceViewModel(
            new CaptureOption(path, "helldivers2"),
            isSelected: true);

        choice.ApplyInfo(new CaptureInfo(
            path,
            "FrameView_helldivers2.exe_2026_08_18T222313_Log.csv",
            "helldivers2.exe",
            "2560x1440",
            "NVIDIA GeForce RTX 5070 Ti",
            "AMD Ryzen 7 5700X3D 8-Core Processor",
            104.2));

        Assert.Equal("Captured 2026-08-18 22:23", choice.CaptureTimeText);
        Assert.Contains("2560x1440", choice.TechnicalLine);
        Assert.Contains("1m 44s", choice.TechnicalLine);
        Assert.Contains("RTX 5070 Ti", choice.TechnicalLine);
        Assert.Contains("Ryzen 7 5700X3D", choice.HardwareLine);
    }

    [Fact]
    public void Picker_exposes_the_active_capture_folder()
    {
        var captures = new[]
        {
            new CaptureOption(
                "C:/FrameView/Captures/FrameView_Game_2026_08_18T222313_Log.csv",
                "Game"),
        };

        var explicitFolder = new MultiBenchmarkSelectionViewModel(
            captures,
            [],
            "D:/Benchmarks/Current");
        var inferredFolder = new MultiBenchmarkSelectionViewModel(captures, []);

        Assert.Equal("D:/Benchmarks/Current", explicitFolder.CaptureFolder);
        Assert.Equal("C:/FrameView/Captures", inferredFolder.CaptureFolder.Replace('\\', '/'));
    }
}
