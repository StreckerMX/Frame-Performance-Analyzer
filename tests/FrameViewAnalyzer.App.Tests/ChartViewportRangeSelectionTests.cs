using FrameViewAnalyzer.App.Charting;
using ScottPlot;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for horizontal drag range selection (parity gap G8):
/// the selection normalizes backwards drags, cancels spans under one second,
/// clamps to the canonical full-series bounds, and never depends on the
/// rendered/decimated viewport.
/// </summary>
public class ChartViewportRangeSelectionTests
{
    private static readonly AxisLimits Full = new(0, 150, 0, 200);

    [Fact]
    public void Forward_drag_produces_the_expected_range()
    {
        var selection = ChartViewport.NormalizeRangeSelection(10, 40, Full);

        Assert.NotNull(selection);
        Assert.Equal(10, selection.Value.Left, precision: 9);
        Assert.Equal(40, selection.Value.Right, precision: 9);
        Assert.Equal(Full.Bottom, selection.Value.Bottom, precision: 9);
        Assert.Equal(Full.Top, selection.Value.Top, precision: 9);
    }

    [Fact]
    public void Reverse_drag_is_normalized_to_ordered_bounds()
    {
        var selection = ChartViewport.NormalizeRangeSelection(40, 10, Full);

        Assert.NotNull(selection);
        Assert.Equal(10, selection.Value.Left, precision: 9);
        Assert.Equal(40, selection.Value.Right, precision: 9);
    }

    [Fact]
    public void Selections_shorter_than_one_second_are_ignored()
    {
        Assert.Null(ChartViewport.NormalizeRangeSelection(10, 10.5, Full));
        Assert.Null(ChartViewport.NormalizeRangeSelection(10.5, 10, Full));
    }

    [Fact]
    public void Exactly_one_second_is_accepted()
    {
        var selection = ChartViewport.NormalizeRangeSelection(10, 11, Full);

        Assert.NotNull(selection);
    }

    [Fact]
    public void Selection_clamps_to_the_canonical_full_range()
    {
        var selection = ChartViewport.NormalizeRangeSelection(-30, 400, Full);

        Assert.NotNull(selection);
        Assert.Equal(Full.Left, selection.Value.Left, precision: 9);
        Assert.Equal(Full.Right, selection.Value.Right, precision: 9);
    }

    [Fact]
    public void Selection_uses_the_full_bounds_even_after_a_narrow_view()
    {
        // A narrowed visible range must never become the source of selection
        // bounds; the canonical full range is always the clamp window.
        var selection = ChartViewport.NormalizeRangeSelection(-5, 500, Full);

        Assert.NotNull(selection);
        Assert.Equal(0, selection.Value.Left, precision: 9);
        Assert.Equal(150, selection.Value.Right, precision: 9);
    }
}
