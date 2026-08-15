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
    private Crosshair? _crosshair;
    private bool _suppressViewChanged;

    /// <summary>Fired after zoom/pan; null bounds mean the full range.</summary>
    public event Action<AxisLimits?>? ViewChanged;

    public SessionChartView()
    {
        InitializeComponent();
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
        _metric = metric;
        _seriesList = seriesList;
        HideTooltip();
        Render();
        NotifyViewChanged();
    }

    public void Clear()
    {
        _metric = null;
        _seriesList = [];
        _crosshair = null;
        HideTooltip();
        ChartHost.Plot.Clear();
        ChartHost.Refresh();
    }

    public void ApplyInteractions(bool wheelZoomEnabled, bool panEnabled, bool markersVisible)
    {
        _wheelZoomEnabled = wheelZoomEnabled;
        _panEnabled = panEnabled;
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
        if (_metric is null || _seriesList.Count == 0)
        {
            return;
        }

        var limits = ChartHost.Plot.Axes.GetLimits();
        var fitted = ChartViewport.AutoZoomToSeries(limits, _seriesList, _metric.Id == "fps");
        if (fitted is null)
        {
            return;
        }

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

    private void Render()
    {
        if (_metric is null || _seriesList.Count == 0)
        {
            return;
        }

        var style = ChartStyle.FromApplicationResources();
        var budget = System.Math.Max(200, (int)(ActualWidth > 10 ? ActualWidth : 800) * 2);
        ChartPlotBuilder.Build(
            ChartHost.Plot, _metric, _seriesList, style, budget, _markersVisible);

        _fullLimits = ChartHost.Plot.Axes.GetLimits();

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
        if (!_panEnabled || _seriesList.Count == 0)
        {
            return;
        }

        _panAnchor = e.GetPosition(ChartHost);
        _panStartLimits = ChartHost.Plot.Axes.GetLimits();
        ChartHost.CaptureMouse();
        HideTooltip();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panAnchor is null)
        {
            return;
        }

        _panAnchor = null;
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
