using FrameViewAnalyzer.App.Charting;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for PNG report geometry: the header owns its band,
/// one metric remains full-width, and multi-metric exports use the compact
/// adaptive two-column grid.
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
        Assert.Equal(1600, rect.Right - rect.Left);
        Assert.Equal(930, rect.Bottom - rect.Top);
    }

    [Fact]
    public void Single_metric_report_keeps_one_full_width_panel()
    {
        var height = ReportPlotBuilder.RecommendedHeight(panelCount: 1, headerHeight: 110);
        var rect = Assert.Single(ReportPlotBuilder.ReportPanelRects(
            width: 1600,
            height,
            headerHeight: 110,
            panelCount: 1));

        Assert.Equal(0, rect.Left);
        Assert.Equal(1600, rect.Right);
        Assert.Equal(110, rect.Top);
        Assert.Equal(height, rect.Bottom);
    }

    [Fact]
    public void Multiple_metrics_use_two_columns()
    {
        var height = ReportPlotBuilder.RecommendedHeight(panelCount: 6, headerHeight: 110);
        var rects = ReportPlotBuilder.ReportPanelRects(
            width: 1600,
            height,
            headerHeight: 110,
            panelCount: 6);

        Assert.Equal(6, rects.Count);
        Assert.Equal(0, rects[0].Left);
        Assert.True(rects[0].Right < 800);
        Assert.True(rects[1].Left > rects[0].Right);
        Assert.Equal(1600, rects[1].Right);
        Assert.Equal(rects[0].Top, rects[1].Top);
        Assert.Equal(rects[0].Bottom, rects[1].Bottom);
    }

    [Fact]
    public void Odd_final_metric_spans_the_complete_last_row()
    {
        var height = ReportPlotBuilder.RecommendedHeight(panelCount: 5, headerHeight: 110);
        var rects = ReportPlotBuilder.ReportPanelRects(
            width: 1600,
            height,
            headerHeight: 110,
            panelCount: 5);

        var last = rects[^1];
        Assert.Equal(0, last.Left);
        Assert.Equal(1600, last.Right);
        Assert.Equal(height, last.Bottom);
        Assert.True(last.Top > rects[2].Top);
    }

    [Fact]
    public void Recommended_height_grows_by_grid_rows_instead_of_metric_count()
    {
        const int headerHeight = 110;

        var one = ReportPlotBuilder.RecommendedHeight(1, headerHeight);
        var two = ReportPlotBuilder.RecommendedHeight(2, headerHeight);
        var three = ReportPlotBuilder.RecommendedHeight(3, headerHeight);
        var six = ReportPlotBuilder.RecommendedHeight(6, headerHeight);

        Assert.Equal(one, two);
        Assert.True(three > two);
        Assert.True(six > three);
        Assert.Equal(
            headerHeight + 3 * ReportPlotBuilder.GridRowHeight + 2 * ReportPlotBuilder.GridGap,
            six);
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
