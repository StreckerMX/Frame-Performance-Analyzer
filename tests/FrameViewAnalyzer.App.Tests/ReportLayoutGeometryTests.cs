using FrameViewAnalyzer.App.Charting;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for the PNG report geometry: every metric panel spans
/// the full content width, the header occupies only its own region, and the
/// plot area starts below the header.
/// </summary>
public class ReportLayoutGeometryTests
{
    [Fact]
    public void Content_rect_spans_the_full_report_width_below_the_header()
    {
        var rect = ReportPlotBuilder.ReportContentRect(width: 1600, height: 1040, headerHeight: 110);

        Assert.Equal(0, rect.Left);
        Assert.Equal(1600, rect.Right);
        Assert.Equal(110, rect.Top);
        Assert.Equal(1040, rect.Bottom);
        // Every metric panel inherits the full content width — no narrow
        // left-column charts.
        Assert.Equal(1600, rect.Right - rect.Left);
        Assert.Equal(930, rect.Bottom - rect.Top);
    }

    [Fact]
    public void Header_height_is_small_relative_to_the_report()
    {
        var header = new ReportPlotBuilder.ReportHeader(
            "Night Run",
            ["3840x2160  ·  RTX 5090", "DLSS Quality"]);

        var headerHeight = ReportPlotBuilder.MeasureHeaderHeight(header);

        Assert.True(headerHeight > 0);
        Assert.True(headerHeight < 200, $"Header unexpectedly tall: {headerHeight}");
        Assert.True(headerHeight < 1040 / 4, "Header must occupy only its own region");
    }

    [Fact]
    public void Content_rect_uses_the_full_height_when_no_header_exists()
    {
        var rect = ReportPlotBuilder.ReportContentRect(width: 1600, height: 1040, headerHeight: 0);

        Assert.Equal(0, rect.Top);
        Assert.Equal(1040, rect.Bottom);
    }
}
