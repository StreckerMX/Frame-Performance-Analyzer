using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Core.Formatting;

/// <summary>Display text for comparison deltas, mirroring the Python reference.</summary>
public static class ComparisonText
{
    /// <summary>
    /// Formats a comparison delta. The arrow always reflects the observed value
    /// movement (up/down), while <paramref name="kind"/> controls whether the
    /// change is styled as an improvement or regression by the caller.
    /// </summary>
    public static string FormatDelta(double? delta, double? deltaPercent, ImprovementKind kind)
    {
        if (delta is null)
        {
            return "--";
        }

        var arrow = kind == ImprovementKind.None
            ? string.Empty
            : delta.Value switch
            {
                > 0 => "↑ ",
                < 0 => "↓ ",
                _ => string.Empty,
            };

        if (deltaPercent is not null)
        {
            var percent = kind == ImprovementKind.None
                ? $"{deltaPercent:+0.0;-0.0}%"
                : $"{System.Math.Abs(deltaPercent.Value):0.0}%";
            return $"{arrow}{percent}";
        }

        var raw = kind == ImprovementKind.None
            ? $"{delta:+0.0;-0.0}"
            : $"{System.Math.Abs(delta.Value):0.0}";
        return $"{arrow}{raw}";
    }
}
