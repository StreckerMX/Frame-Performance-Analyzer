using ScottPlot;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>
/// Stable colors shared by Multi chart lines and visible-range KPI rows.
/// Keeping the mapping index-based makes the same benchmark keep the same
/// color while metrics and zoom ranges change.
/// </summary>
public static class MultiSeriesPalette
{
    private static readonly string[] HexColors =
    [
        "#76B900", // NVIDIA green
        "#4FA3D1", // blue
        "#E69F00", // orange
        "#CC79A7", // magenta
        "#009E73", // teal
        "#D55E00", // vermilion
        "#56B4E9", // sky blue
        "#F0E442", // yellow
    ];

    public static string HexAt(int index) =>
        HexColors[Math.Abs(index) % HexColors.Length];

    public static Color ColorAt(int index) => Color.FromHex(HexAt(index));
}
