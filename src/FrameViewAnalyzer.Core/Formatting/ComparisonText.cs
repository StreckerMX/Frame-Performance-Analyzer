using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Core.Formatting;

/// <summary>Display text for comparison deltas, mirroring the Python reference.</summary>
public static class ComparisonText
{
    /// <summary>"▲ +9.6%", "▼ -4.2%", neutral without arrow, "--" when missing.</summary>
    public static string FormatDelta(double? delta, double? deltaPercent, ImprovementKind kind)
    {
        if (delta is null)
        {
            return "--";
        }

        var arrow = kind switch
        {
            ImprovementKind.Improvement => "▲ ",
            ImprovementKind.Regression => "▼ ",
            _ => string.Empty,
        };

        return deltaPercent is not null
            ? $"{arrow}{deltaPercent:+.1f}%"
            : $"{arrow}{delta:+.1f}";
    }
}
