using FrameViewAnalyzer.Core.Formatting;

namespace FrameViewAnalyzer.Core.Tests;

public class DisplayTextTests
{
    [Theory]
    [InlineData("GTA5_Enhanced.exe", "GTA5 Enhanced")]
    [InlineData("  My Game  ", "My Game")]
    [InlineData("", "Unnamed benchmark")]
    [InlineData("A_B_C.exe", "A B C")]
    public void CleanGameName_normalizes_application_names(string raw, string expected)
    {
        Assert.Equal(expected, DisplayText.CleanGameName(raw));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5070 Ti", "RTX 5070 Ti")]
    [InlineData("AMD Radeon RX 7900 XTX", "RX 7900 XTX")]
    [InlineData("AMD Ryzen 7 5700X3D 8-Core Processor", "Ryzen 7 5700X3D")]
    public void CompactHardware_shortens_gpu_and_cpu_names(string raw, string expected)
    {
        Assert.Equal(expected, DisplayText.CompactHardware(raw));
    }

    [Theory]
    [InlineData(null, "--")]
    [InlineData(0.0, "--")]
    [InlineData(double.NaN, "--")]
    [InlineData(45.0, "45 s")]
    [InlineData(90.0, "1 min 30 s")]
    [InlineData(337.0, "5 min 37 s")]
    [InlineData(3600.0, "60 min 0 s")]
    public void FormatDuration_renders_compact_durations(double? seconds, string expected)
    {
        Assert.Equal(expected, DisplayText.FormatDuration(seconds));
    }
}
