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

    [Theory]
    [InlineData(0.0, "0 s")]
    [InlineData(45.0, "45 s")]
    [InlineData(60.0, "1 min")]
    [InlineData(300.0, "5 min")]
    [InlineData(90.0, "1 min 30 s")]
    [InlineData(150.0, "2 min 30 s")]
    [InlineData(3599.0, "59 min 59 s")]
    [InlineData(3600.0, "1 h")]
    [InlineData(7200.0, "2 h")]
    [InlineData(3660.0, "1 h 1 min")]
    [InlineData(4380.0, "1 h 13 min")]
    [InlineData(4385.0, "1 h 13 min 5 s")]
    [InlineData(89.6, "1 min 30 s")]
    [InlineData(59.4, "59 s")]
    [InlineData(double.PositiveInfinity, "0 s")]
    [InlineData(-10.0, "0 s")]
    public void FormatDurationHuman_renders_human_durations(double seconds, string expected)
    {
        Assert.Equal(expected, DisplayText.FormatDurationHuman(seconds));
    }

    [Theory]
    [InlineData(null, "--")]
    [InlineData(12.34, "12.3")]
    [InlineData(99.9, "99.9")]
    [InlineData(150.0, "150")]
    [InlineData(1234.5, "1,234")]
    public void FormatStat_follows_the_python_precision_rules(double? value, string expected)
    {
        Assert.Equal(expected, DisplayText.FormatStat(value));
    }

    [Fact]
    public void FormatStat_appends_the_unit()
    {
        Assert.Equal("12.3 W", DisplayText.FormatStat(12.34, "W"));
    }
}
