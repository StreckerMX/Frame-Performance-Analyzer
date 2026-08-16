using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Core.Formatting;

/// <summary>Display text for comparison deltas, mirroring the Python reference.</summary>
public static class ComparisonText
{
    /// <summary>"↑ 9.6%", "↓ 4.2%", neutral without arrow, "--" when missing.</summary>
    public static string FormatDelta(double? delta, double? deltaPercent, ImprovementKind kind)
    {
        if (delta is null)
        {
            return "--";
        }

        var arrow = kind switch
        {
            ImprovementKind.Improvement => "↑ ",
            ImprovementKind.Regression => "↓ ",
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
