using ScottPlot;

namespace FrameViewAnalyzer.App.ViewModels;

public partial class ChartViewModel
{
    /// <summary>
    /// Current user-visible chart window. Null means the complete active
    /// workspace range. Exporters use this exact X window so PNG/CSV/JSON
    /// snapshots match what the user is investigating on screen.
    /// </summary>
    public AxisLimits? VisibleBounds => _visibleBounds;
}
