namespace FrameViewAnalyzer.Core.Tests;

public class SessionSlotsTests
{
    [Fact]
    public void Removing_the_comparison_keeps_the_base()
    {
        var (baseSession, comparisonSession, promoted) = SessionSlots.Remove("base", "comparison", SessionSlots.ComparisonSlot);

        Assert.Equal("base", baseSession);
        Assert.Null(comparisonSession);
        Assert.False(promoted);
    }

    [Fact]
    public void Removing_the_base_with_a_comparison_promotes_it()
    {
        var (baseSession, comparisonSession, promoted) = SessionSlots.Remove("base", "comparison", SessionSlots.BaseSlot);

        Assert.Equal("comparison", baseSession);
        Assert.Null(comparisonSession);
        Assert.True(promoted);
    }

    [Fact]
    public void Removing_the_base_alone_empties_the_slots()
    {
        var (baseSession, comparisonSession, promoted) = SessionSlots.Remove("base", null, SessionSlots.BaseSlot);

        Assert.Null(baseSession);
        Assert.Null(comparisonSession);
        Assert.False(promoted);
    }

    [Fact]
    public void Removing_an_empty_base_slot_is_a_no_op()
    {
        var (baseSession, comparisonSession, promoted) = SessionSlots.Remove<string>(null, null, SessionSlots.BaseSlot);

        Assert.Null(baseSession);
        Assert.Null(comparisonSession);
        Assert.False(promoted);
    }

    [Fact]
    public void Unknown_slots_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => SessionSlots.Remove("base", "comparison", "c"));
        Assert.Throws<ArgumentException>(() => SessionSlots.Remove<string>(null, null, string.Empty));
    }
}
