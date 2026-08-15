using System.Windows.Controls;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;
using ScottPlot.Plottables;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// ScottPlot host for the session chart. Plot assembly is presentation glue
/// and lives here; the analytics data comes from the view model unchanged.
/// </summary>
public partial class SessionChartView : UserControl
{
    private MetricDefinition? _metric;
    private MetricSeries? _series;
    private Crosshair? _crosshair;

    public SessionChartView() => InitializeComponent();

    public void ShowData(MetricDefinition metric, MetricSeries series)
    {
        _metric = metric;
        _series = series;
        Render();
    }

    public void Clear()
    {
        _metric = null;
        _series = null;
        _crosshair = null;
        ChartHost.Plot.Clear();
        ChartHost.Refresh();
    }

    /// <summary>Re-renders with the current theme brushes (theme switch).</summary>
    public void RefreshStyle()
    {
        if (_metric is not null && _series is not null)
        {
            Render();
        }
    }

    private void Render()
    {
        if (_metric is null || _series is null)
        {
            return;
        }

        var style = ChartStyle.FromApplicationResources();
        var budget = System.Math.Max(200, (int)(ActualWidth > 10 ? ActualWidth : 800) * 2);
        ChartPlotBuilder.Build(ChartHost.Plot, _metric, [_series], style, budget);

        // Crosshair follows the mouse; the custom time/value tooltip text
        // arrives in Phase 6 with interactive tooltip content.
        _crosshair = ChartHost.Plot.Add.Crosshair(0, 0);
        _crosshair.IsVisible = true;
        _crosshair.LinePattern = LinePattern.Dotted;
        _crosshair.LineColor = style.Muted.WithAlpha(0.75);
        _crosshair.TextColor = style.Foreground;
        _crosshair.TextBackgroundColor = style.TooltipBackground;
        _crosshair.MarkerFillColor = style.SeriesA;
        _crosshair.MarkerLineColor = style.Background;
        _crosshair.MarkerShape = MarkerShape.OpenCircle;

        ChartHost.Refresh();
    }
}
