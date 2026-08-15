using System.Windows;
using System.Windows.Media;
using ScottPlot;

namespace FrameViewAnalyzer.App.Charting;

/// <summary>Theme-derived colors for the chart, converted to ScottPlot colors.</summary>
public sealed record ChartStyle(
    ScottPlot.Color Background,
    ScottPlot.Color Foreground,
    ScottPlot.Color Muted,
    ScottPlot.Color Grid,
    ScottPlot.Color SeriesA,
    ScottPlot.Color SeriesB,
    ScottPlot.Color Accent,
    ScottPlot.Color TooltipBackground,
    ScottPlot.Color TooltipBorder)
{
    /// <summary>
    /// Reads the current application theme brushes; falls back to the dark
    /// palette when no WPF Application exists (headless tests).
    /// </summary>
    public static ChartStyle FromApplicationResources()
    {
        var background = BrushColor("ChartBackgroundBrush", "#080808");
        var foreground = BrushColor("TextBrush", "#FFFFFF");
        var muted = BrushColor("MutedBrush", "#8C8C8C");
        var grid = BrushColor("GridBrush", "#1E1E1E");
        var seriesA = BrushColor("SeriesABrush", "#76B900");
        var seriesB = BrushColor("SeriesBBrush", "#4FA3D1");
        var accent = BrushColor("AccentBrush", "#76B900");
        var tooltipBackground = BrushColor("TooltipBackgroundBrush", "#121212");
        var tooltipBorder = BrushColor("TooltipBorderBrush", "#2E2E2E");
        return new ChartStyle(
            background, foreground, muted, grid, seriesA, seriesB, accent,
            tooltipBackground, tooltipBorder);
    }

    private static ScottPlot.Color BrushColor(string key, string fallbackHex)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            var color = brush.Color;
            return new ScottPlot.Color(color.R, color.G, color.B, color.A);
        }

        return ScottPlot.Color.FromHex(fallbackHex);
    }
}
