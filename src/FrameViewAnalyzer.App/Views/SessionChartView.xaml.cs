using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core.Charting;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Metrics;
using ScottPlot;
using ScottPlot.Plottables;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// ScottPlot host with the reference interaction model. ScottPlot's native
/// input processor is disabled; wheel zoom (cursor-anchored), drag pan, and
/// the hover tooltip are implemented here with pure viewport math so every
/// behavior matches the Python reference. Base and comparison series are
/// plotted together with a legend.
/// </summary>
public partial class SessionChartView : UserControl
{
    private MetricDefinition? _metric;
    private IReadOnlyList<MetricSeries> _seriesList = [];
    private AxisLimits _fullLimits;
    private bool _wheelZoomEnabled = true;
    private bool _panEnabled = true;
    private bool _markersVisible;
    private Point? _panAnchor;
    private AxisLimits _panStartLimits;
    private double? _selectStartX;
    private HorizontalSpan? _selectionOverlay;
    private Crosshair? _crosshair;
    private bool _suppressViewChanged;

    /// <summary>Fired after zoom/pan; null bounds mean the full range.</summary>
    public event Action<AxisLimits?>? ViewChanged;

    public SessionChartView()
    {
        InitializeComponent();

        // The empty plot must never flash the default white ScottPlot
        // background before the first series render; apply the theme upfront.
        ApplyPlotBackground();

        ChartHost.UserInputProcessor.IsEnabled = false;
        ChartHost.MouseWheel += OnMouseWheel;
        ChartHost.MouseMove += OnMouseMove;
        ChartHost.MouseLeave += (_, _) => HideTooltip();
        ChartHost.MouseLeftButtonDown += OnMouseLeftButtonDown;
        ChartHost.MouseLeftButtonUp += OnMouseLeftButtonUp;
        ChartHost.Plot.RenderManager.AxisLimitsChanged += (_, _) => NotifyViewChanged();
    }

    public void ShowData(MetricDefinition metric, IReadOnlyList<MetricSeries> seriesList)
    {
        CancelSelection();

        // A metric switch rebuilds the ScottPlot surface, but the user's time
        // window is workspace state rather than metric state. Carry only the X
        // window across metrics that belong to the same loaded workspace, then
        // recompute Y for the newly selected metric inside that window.
        var previousLimits = _metric is not null && _seriesList.Count > 0
            ? ChartHost.Plot.Axes.GetLimits()
            : (AxisLimits?)null;
        var carryTimeWindow = previousLimits is not null
            && CanCarryTimeWindow(_seriesList, seriesList);

        _metric = metric;
        _seriesList = seriesList;
        HideTooltip();

        var previousSuppression = _suppressViewChanged;
        _suppressViewChanged = true;
        try
        {
            Render(fitToData: true);
            if (carryTimeWindow)
            {
                RestoreTimeWindow(previousLimits!.Value);
            }
        }
        finally
        {
            _suppressViewChanged = previousSuppression;
        }

        NotifyViewChanged();
    }

    /// <summary>
    /// Returns true when the old and new plotted series represent the same
    /// workspace. Metrics may be available for only a subset of sessions, so a
    /// source-session subset still counts as the same workspace.
    /// </summary>
    private static bool CanCarryTimeWindow(
        IReadOnlyList<MetricSeries> current,
        IReadOnlyList<MetricSeries> next)
    {
        if (current.Count == 0 || next.Count == 0)
        {
            return false;
        }

        var currentSources = current
            .Where(series => series.SourceSession is not null)
            .Select(series => series.SourceSession!)
            .ToList();
        var nextSources = next
            .Where(series => series.SourceSession is not null)
            .Select(series => series.SourceSession!)
            .ToList();

        if (currentSources.Count > 0 || nextSources.Count > 0)
        {
            if (currentSources.Count == 0 || nextSources.Count == 0)
            {
                return false;
            }

            var smaller = currentSources.Count <= nextSources.Count ? currentSources : nextSources;
            var larger = currentSources.Count <= nextSources.Count ? nextSources : currentSources;
            return smaller.All(source =>
                larger.Any(candidate => ReferenceEquals(source, candidate)));
        }

        // Test/standalone series may not carry SourceSession. In that case use
        // the stable presentation identity instead of preserving blindly.
        if (current.Count != next.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (current[index].Role != next[index].Role
                || current[index].WorkspaceIndex != next[index].WorkspaceIndex
                || !string.Equals(current[index].Label, next[index].Label, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Restores only the previous horizontal window after a metric rebuild.
    /// Vertical limits are recalculated from the new metric's visible values.
    /// </summary>
    private void RestoreTimeWindow(AxisLimits previousLimits)
    {
        if (_metric is null || _seriesList.Count == 0)
        {
            return;
        }

        var minimum = System.Math.Max(previousLimits.Left, _fullLimits.Left);
        var maximum = System.Math.Min(previousLimits.Right, _fullLimits.Right);
        if (maximum - minimum <= 1e-9)
        {
            return;
        }

        var visible = new AxisLimits(
            minimum,
            maximum,
            _fullLimits.Bottom,
            _fullLimits.Top);
        var fitted = ChartViewport.AutoZoomToSeries(
            visible,
            _seriesList,
            _metric.Id == "fps");
        ChartHost.Plot.Axes.SetLimits(fitted ?? visible);
        ChartHost.Refresh();
    }

    public void Clear()
    {
        CancelSelection();
        _metric = null;
        _seriesList = [];
        _crosshair = null;
        HideTooltip();
        ChartHost.Plot.Clear();
        ApplyPlotBackground();
        ChartHost.Refresh();
    }

    /// <summary>Keeps the plot surface on the current theme colors.</summary>
    private void ApplyPlotBackground()
    {
        var style = ChartStyle.FromApplicationResources();
        ChartHost.Plot.FigureBackground.Color = style.Background;
        ChartHost.Plot.DataBackground.Color = style.Background;
    }

    public void ApplyInteractions(bool wheelZoomEnabled, bool panEnabled, bool markersVisible)
    {
        var panToggled = _panEnabled != panEnabled;
        _wheelZoomEnabled = wheelZoomEnabled;
        _panEnabled = panEnabled;
        if (panToggled)
        {
            // Pan and range selection must never fight over the drag gesture:
            // switching modes cancels any in-progress selection.
            CancelSelection();
        }

        var reRender = _markersVisible != markersVisible;
        _markersVisible = markersVisible;
        if (reRender)
        {
            Render();
        }
    }

    /// <summary>Re-renders with the current theme brushes (theme switch).</summary>
    public void RefreshStyle()
    {
        if (_metric is not null && _seriesList.Count > 0)
        {
            Render();
        }
    }

    public void ResetZoom()
    {
        CancelSelection();
        if (_seriesList.Count == 0)
        {
            return;
        }

        _suppressViewChanged = true;
        ChartHost.Plot.Axes.SetLimits(_fullLimits);
        ChartHost.Refresh();
        _suppressViewChanged = false;
        NotifyViewChanged();
    }

    public void AutoZoom()
    {
        CancelSelection();
        if (_metric is null || _seriesList.Count == 0)
        {
            return;
        }

        // Auto Zoom recovers the canonical full-series bounds, never the
        // currently rendered/decimated viewport subset.
        var fitted = ChartViewport.FullSeriesLimits(_seriesList, _metric.Id == "fps");
        if (fitted is null)
        {
            return;
        }

        _fullLimits = fitted.Value;
        _suppressViewChanged = true;
        ChartHost.Plot.Axes.SetLimits(fitted.Value);
        ChartHost.Refresh();
        _suppressViewChanged = false;
        NotifyViewChanged();
    }

    /// <summary>
    /// Jumps the visible range to a time window (Analyze actions). The window
    /// is clamped to the full range and very short targets are padded to a
    /// one-second span so the zoom always lands, like the Python reference.
    /// </summary>
    public void ZoomToRange(double minimum, double maximum)
    {
        if (_metric is null || _seriesList.Count == 0)
        {
            return;
        }

        if (maximum - minimum < 1.0)
        {
            var midpoint = (minimum + maximum) / 2.0;
            minimum = midpoint - 0.5;
            maximum = midpoint + 0.5;
        }

        minimum = System.Math.Max(minimum, _fullLimits.Left);
        maximum = System.Math.Min(maximum, _fullLimits.Right);

        ApplyZoomToWindow(minimum, maximum);
    }

    private void ApplyZoomToWindow(double minimum, double maximum)
    {
        if (_metric is null || _seriesList.Count == 0)
        {
            return;
        }

        var current = ChartHost.Plot.Axes.GetLimits();
        var next = ChartViewport.AutoZoomToSeries(
            new AxisLimits(minimum, maximum, current.Bottom, current.Top),
            _seriesList,
            _metric.Id == "fps");

        _suppressViewChanged = true;
        ChartHost.Plot.Axes.SetLimits(next ?? new AxisLimits(minimum, maximum, current.Bottom, current.Top));
        ChartHost.Refresh();
        _suppressViewChanged = false;
        NotifyViewChanged();
    }

    private void Render(bool fitToData = false)
    {
        if (_metric is null || _seriesList.Count == 0)
        {
            return;
        }

        var style = ChartStyle.FromApplicationResources();
        var budget = System.Math.Max(200, (int)(ActualWidth > 10 ? ActualWidth : 800) * 2);

        // Rebuilding the plot resets the axes; preserve the current view unless
        // this is a data change that must establish a fresh canonical fit.
        var previousLimits = ChartHost.Plot.Axes.GetLimits();
        ChartPlotBuilder.Build(
            ChartHost.Plot, _metric, _seriesList, style, budget, _markersVisible);

        if (fitToData)
        {
            var fitted = ChartViewport.FullSeriesLimits(_seriesList, _metric.Id == "fps");
            if (fitted is not null)
            {
                _fullLimits = fitted.Value;
                ChartHost.Plot.Axes.SetLimits(fitted.Value);
            }
        }
        else
        {
            ChartHost.Plot.Axes.SetLimits(previousLimits);
        }

        _crosshair = ChartHost.Plot.Add.Crosshair(0, 0);
        _crosshair.IsVisible = false;
        _crosshair.LinePattern = LinePattern.Dotted;
        _crosshair.LineColor = style.Muted.WithAlpha(0.75);
        _crosshair.TextColor = style.Foreground;
        _crosshair.TextBackgroundColor = style.TooltipBackground;
        _crosshair.MarkerFillColor = style.SeriesA;
        _crosshair.MarkerLineColor = style.Background;
        _crosshair.MarkerShape = MarkerShape.OpenCircle;
        _crosshair.MarkerSize = 6;

        ChartHost.Refresh();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_wheelZoomEnabled || _seriesList.Count == 0)
        {
            e.Handled = true;
            return;
        }

        var position = e.GetPosition(ChartHost);
        var coordinates = ChartHost.Plot.GetCoordinates((float)position.X, (float)position.Y);
        var current = ChartHost.Plot.Axes.GetLimits();
        var scale = e.Delta > 0 ? 0.75 : 1.35;
        var next = ChartViewport.ZoomAt(current, coordinates.X, scale, _fullLimits);

        ChartHost.Plot.Axes.SetLimits(next);
        ChartHost.Refresh();
        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_seriesList.Count == 0 || _metric is null)
        {
            return;
        }

        if (_panEnabled)
        {
            _panAnchor = e.GetPosition(ChartHost);
            _panStartLimits = ChartHost.Plot.Axes.GetLimits();
            ChartHost.CaptureMouse();
            HideTooltip();
            return;
        }

        // Drag pan is OFF: begin a horizontal time-range selection.
        var position = e.GetPosition(ChartHost);
        var coordinates = ChartHost.Plot.GetCoordinates((float)position.X, (float)position.Y);
        BeginRangeSelection(coordinates.X);
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panAnchor is not null)
        {
            _panAnchor = null;
            if (ChartHost.IsMouseCaptured)
            {
                ChartHost.ReleaseMouseCapture();
            }

            return;
        }

        // _selectStartX is the authoritative gesture state: EVERY mouse-up
        // while a selection is active finalizes it, even when no mouse move
        // ever created an overlay (a plain click must still release the mouse
        // capture and clear the start). The end coordinate comes from the
        // actual mouse-up position, not from the last MouseMove event.
        if (_selectStartX is not null)
        {
            var position = e.GetPosition(ChartHost);
            var coordinates = ChartHost.Plot.GetCoordinates((float)position.X, (float)position.Y);
            EndRangeSelection(coordinates.X);
            e.Handled = true;
            return;
        }
    }

    /// <summary>Begins a horizontal range-selection gesture at a clamped start X.</summary>
    internal void BeginRangeSelection(double startX)
    {
        _selectStartX = ClampToFullRange(startX);
        ChartHost.CaptureMouse();
        HideTooltip();
    }

    /// <summary>
    /// Updates the translucent selection overlay for the current pointer X.
    /// No-op when no gesture is active, so a move after a completed click can
    /// never resurrect selection state.
    /// </summary>
    internal void UpdateRangeSelection(double endX)
    {
        if (_selectStartX is null || _metric is null)
        {
            return;
        }

        var clamped = ClampToFullRange(endX);
        var minX = System.Math.Min(_selectStartX.Value, clamped);
        var maxX = System.Math.Max(_selectStartX.Value, clamped);

        if (_selectionOverlay is not null)
        {
            ChartHost.Plot.Remove(_selectionOverlay);
        }

        var style = ChartStyle.FromApplicationResources();
        _selectionOverlay = ChartHost.Plot.Add.HorizontalSpan(minX, maxX);
        _selectionOverlay.FillColor = style.SeriesA.WithAlpha(0.18);
        ChartHost.Refresh();
    }

    /// <summary>
    /// Finalizes an active gesture from the mouse-up X: clamps, normalizes,
    /// and ALWAYS cancels (clears the start, removes any overlay, releases
    /// the capture). Normalized spans >= 1 second are applied as a zoom;
    /// shorter ones are silently ignored. No-op when no gesture is active.
    /// </summary>
    internal void EndRangeSelection(double endX)
    {
        if (_selectStartX is null)
        {
            return;
        }

        var startX = _selectStartX.Value;
        var selection = ChartViewport.NormalizeRangeSelection(
            startX,
            ClampToFullRange(endX),
            _fullLimits);
        CancelSelection();

        if (selection is not null)
        {
            ApplyZoomToWindow(selection.Value.Left, selection.Value.Right);
        }
    }

    /// <summary>Whether a range-selection gesture is currently active.</summary>
    internal bool IsRangeSelectionActive => _selectStartX is not null;

    private double ClampToFullRange(double x) =>
        System.Math.Clamp(x, _fullLimits.Left, _fullLimits.Right);

    /// <summary>
    /// Removes the temporary selection overlay and any in-progress selection.
    /// Called on mode switches, zoom resets, session/metric changes, and clear.
    /// </summary>
    private void CancelSelection()
    {
        if (_selectionOverlay is not null)
        {
            ChartHost.Plot.Remove(_selectionOverlay);
            _selectionOverlay = null;
            ChartHost.Refresh();
        }

        _selectStartX = null;
        if (ChartHost.IsMouseCaptured)
        {
            ChartHost.ReleaseMouseCapture();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(ChartHost);

        if (_panAnchor is not null && _seriesList.Count > 0)
        {
            var anchorCoordinates = ChartHost.Plot.GetCoordinates(
                (float)_panAnchor.Value.X, (float)_panAnchor.Value.Y);
            var coordinates = ChartHost.Plot.GetCoordinates((float)position.X, (float)position.Y);
            var delta = anchorCoordinates.X - coordinates.X;
            ChartHost.Plot.Axes.SetLimits(ChartViewport.PanTo(_panStartLimits, _fullLimits, delta));
            ChartHost.Refresh();
            return;
        }

        if (_selectStartX is not null && _metric is not null)
        {
            // Defensive invariant: selection state must never exist while the
            // left button is no longer pressed. The gesture is finalized on
            // mouse-up; if stale state ever survives, cancel it instead of
            // drawing a new overlay.
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelSelection();
                return;
            }

            // Update the translucent selection overlay, clamped to the
            // canonical full-series X bounds; suppress the tooltip while
            // actively selecting.
            HideTooltip();
            var coordinates = ChartHost.Plot.GetCoordinates((float)position.X, (float)position.Y);
            UpdateRangeSelection(coordinates.X);
            e.Handled = true;
            return;
        }

        if (_metric is null || _seriesList.Count == 0)
        {
            HideTooltip();
            return;
        }

        var limits = ChartHost.Plot.Axes.GetLimits();
        var mouseCoordinates = ChartHost.Plot.GetCoordinates((float)position.X, (float)position.Y);
        var tolerance = System.Math.Max(0.65, limits.HorizontalSpan / 120.0);

        // Probe every plotted series independently against the cursor X;
        // Base-only, Comparison-only, and overlapping regions all work.
        var hits = SeriesProbe.Select(_seriesList, mouseCoordinates.X, tolerance);
        var anchor = SeriesProbe.Anchor(hits);
        if (anchor is null)
        {
            HideTooltip();
            return;
        }

        var lines = new List<string> { $"Time: {anchor.Value.X:F1} s" };
        foreach (var hit in hits)
        {
            var valueText = _metric.Id == "fps"
                ? $"{hit.Y:F1}"
                : DisplayText.FormatStat(hit.Y, _metric.Unit);
            lines.Add($"{hit.Series.LabelOrDefault}: {valueText}");
        }

        TooltipText.Text = string.Join("\n", lines);

        if (_crosshair is not null)
        {
            _crosshair.Position = new Coordinates(anchor.Value.X, anchor.Value.Y);
            _crosshair.IsVisible = true;
            ChartHost.Refresh();
        }

        var pixel = ChartHost.Plot.GetPixel(new Coordinates(mouseCoordinates.X, mouseCoordinates.Y));
        var offsetX = (double)pixel.X + 12;
        var offsetY = (double)pixel.Y + 12;
        TooltipOverlay.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = TooltipOverlay.DesiredSize.Width;
        var height = TooltipOverlay.DesiredSize.Height;
        if (offsetX + width > ActualWidth - 6)
        {
            offsetX = pixel.X - width - 12;
        }

        if (offsetY + height > ActualHeight - 6)
        {
            offsetY = pixel.Y - height - 12;
        }

        offsetX = System.Math.Max(6, offsetX);
        offsetY = System.Math.Max(6, offsetY);
        TooltipOverlay.Margin = new Thickness(offsetX, offsetY, 0, 0);
        TooltipOverlay.Visibility = Visibility.Visible;
    }

    private void HideTooltip()
    {
        TooltipOverlay.Visibility = Visibility.Collapsed;
        if (_crosshair is not null && _crosshair.IsVisible)
        {
            _crosshair.IsVisible = false;
            ChartHost.Refresh();
        }
    }

    private void NotifyViewChanged()
    {
        if (_suppressViewChanged || _seriesList.Count == 0)
        {
            return;
        }

        var limits = ChartHost.Plot.Axes.GetLimits();
        var fullSpan = _fullLimits.HorizontalSpan;
        var currentSpan = limits.HorizontalSpan;
        var isFull = System.Math.Abs(currentSpan - fullSpan) < 0.01;
        ViewChanged?.Invoke(isFull ? null : limits);
    }
}
