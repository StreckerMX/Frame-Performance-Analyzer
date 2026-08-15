namespace FrameViewAnalyzer.Core;

/// <summary>
/// Session-slot transitions for the Base/Comparison pair. Removing the base
/// promotes the comparison into the base slot; removing the comparison keeps
/// the base untouched. Mirrors the Python reference exactly.
/// </summary>
public static class SessionSlots
{
    public const string BaseSlot = "a";
    public const string ComparisonSlot = "b";

    public static (T? Base, T? Comparison, bool Promoted) Remove<T>(
        T? baseSession,
        T? comparisonSession,
        string slot)
    {
        if (slot == ComparisonSlot)
        {
            return (baseSession, default, false);
        }

        if (slot != BaseSlot)
        {
            throw new ArgumentException($"Unknown session slot: {slot}", nameof(slot));
        }

        if (comparisonSession is not null)
        {
            return (comparisonSession, default, true);
        }

        return (default, default, false);
    }
}
