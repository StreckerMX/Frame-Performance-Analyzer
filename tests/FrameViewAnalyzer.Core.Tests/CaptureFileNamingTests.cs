using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Core.Tests;

public class CaptureFileNamingTests
{
    [Fact]
    public void Sanitize_strips_frameview_prefix_and_log_suffix()
    {
        Assert.Equal(
            "Game.exe_2026-08-12",
            CaptureFileNaming.SanitizeDisplayName("FrameView_Game.exe_2026-08-12_Log.csv"));
    }

    [Fact]
    public void Sanitize_ellipsizes_names_longer_than_forty_chars()
    {
        var name = CaptureFileNaming.SanitizeDisplayName(
            "FrameView_" + new string('x', 50) + "_Log.csv");

        Assert.Equal(40, name.Length);
        Assert.EndsWith("…", name);
    }

    [Fact]
    public void Stamp_comes_from_the_frameview_filename()
    {
        Assert.True(
            CaptureFileNaming.TryParseCaptureStamp(
                "FrameView_A.exe_2026_08_13T033633_Log.csv",
                out var stamp));

        Assert.Equal("2026-08-13 03:36", CaptureFileNaming.FormatStamp(stamp));
    }

    [Fact]
    public void Stamp_parse_fails_without_the_pattern()
    {
        Assert.False(CaptureFileNaming.TryParseCaptureStamp("capture.csv", out _));
    }

    [Fact]
    public void Log_suffix_constant_is_stable()
    {
        Assert.Equal("_Log.csv", CaptureFileNaming.LogSuffix);
    }
}
