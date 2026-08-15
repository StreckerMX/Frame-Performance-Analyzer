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
/// behavior matches the Python reference. Plot assembly is presentation
/// glue; analytics data arrives from the view model unchanged.
/// </summary>
public partial class SessionChartView : UserControl
{
    private MetricDefinition? _metric;
    private MetricSeries? _series;
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

    public void ShowData(MetricDefinition metric, MetricSeries series)
    {
        _metric = metric;
        _series = series;
        HideTooltip();
        Render();
        NotifyViewChanged();
    }

    public void Clear()
    {
        _metric = null;
        _series = null;
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
        if (_metric is not null && _series is not null)
        {
            Render();
        }
    }

    public void ResetZoom()
    {
        if (_series is null)
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
        if (_metric is null || _series is null)
        {
            return;
        }

        var limits = ChartHost.Plot.Axes.GetLimits();
        var values = FrameViewAnalyzer.Analytics.Statistics.VisibleRangeCalculator.FilterValues(
            _series.X, _series.Y, limits.Left, limits.Right);
        if (values.Count == 0)
        {
            return;
        }

        var minY = values.Min();
        var maxY = values.Max();
        var fitted = ChartViewport.FitY(limits, minY, maxY, _metric.Id == "fps");
        _suppressViewChanged = true;
        ChartHost.Plot.Axes.SetLimits(fitted);
        ChartHost.Refresh();
        _suppressViewChanged = false;
        NotifyViewChanged();
    }

    private void Render()
    {
        if (_metric is null || _series is null)
        {
            return;
        }

        var style = ChartStyle.FromApplicationResources();
        var budget = System.Math.Max(200, (int)(ActualWidth > 10 ? ActualWidth : 800) * 2);
        ChartPlotBuilder.Build(
            ChartHost.Plot, _metric, [_series], style, budget, _markersVisible);

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
        if (!_wheelZoomEnabled || _series is null)
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
        if (!_panEnabled || _series is null)
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

        if (_panAnchor is not null && _series is not null)
        {
            var anchorCoordinates = ChartHost.Plot.GetCoordinates(
                (float)_panAnchor.Value.X, (float)_panAnchor.Value.Y);
            var coordinates = ChartHost.Plot.GetCoordinates((float)position.X, (float)position.Y);
            var delta = anchorCoordinates.X - coordinates.X;
            ChartHost.Plot.Axes.SetLimits(ChartViewport.PanTo(_panStartLimits, _fullLimits, delta));
            ChartHost.Refresh();
            return;
        }

        if (_metric is null || _series is null || _series.X.Length == 0)
        {
            HideTooltip();
            return;
        }

        var limits = ChartHost.Plot.Axes.GetLimits();
        var mouseCoordinates = ChartHost.Plot.GetCoordinates((float)position.X, (float)position.Y);
        var tolerance = System.Math.Max(0.65, limits.HorizontalSpan / 120.0);
        var index = SeriesGeometry.NearestIndex(_series.X, mouseCoordinates.X);
        if (index < 0 || System.Math.Abs(_series.X[index] - mouseCoordinates.X) > tolerance)
        {
            HideTooltip();
            return;
        }

        var x = _series.X[index];
        var y = _series.Y[index];
        var valueText = _metric.Id == "fps"
            ? $"{y:F1}"
            : DisplayText.FormatStat(y, _metric.Unit);
        TooltipText.Text = $"Time: {x:F1} s\n{_metric.Label}: {valueText}";

        if (_crosshair is not null)
        {
            _crosshair.Position = new Coordinates(x, y);
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
        if (_suppressViewChanged || _series is null)
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