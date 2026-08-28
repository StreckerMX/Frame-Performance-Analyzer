using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.Core.Charting;
using ScottPlot;
using ScottPlot.Plottables;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Precision Timeline presentation refinements kept separate from the core
/// interaction code: Frame points replace the one-second curve, cursor values
/// live in a fixed legend-style readout, and grid lines are normalized to the
/// ticks which actually carry labels.
/// </summary>
public partial class SessionChartView
{
    private bool _precisionPresentationAttached;
    private string _cursorReadoutSignature = string.Empty;
    private readonly List<CursorReadoutRow> _cursorReadoutRows = [];

    private void ChartHost_PrecisionLoaded(object sender, RoutedEventArgs e)
    {
        if (_precisionPresentationAttached)
        {
            return;
        }

        _precisionPresentationAttached = true;
        ChartHost.Plot.RenderManager.RenderStarting += (_, _) =>
        {
            NormalizeGridTicks();
            ApplyFramePointReplacement();
            EnsureCursorReadoutRows();

            // The fixed WPF readout below replaces ScottPlot's floating/boxed
            // legend so Pair/Multi values have one stable home.
            ChartHost.Plot.HideLegend();
        };
    }

    private void ChartHost_PrecisionMouseMove(object sender, MouseEventArgs e)
    {
        var activeSeries = ActiveCursorSeries();
        if (_metric is null || activeSeries.Count == 0)
        {
            ClearCursorValues();
            return;
        }

        EnsureCursorReadoutRows();
        var position = e.GetPosition(ChartHost);
        var coordinates = ChartHost.Plot.GetCoordinates((float)position.X, (float)position.Y);

        for (var index = 0; index < activeSeries.Count && index < _cursorReadoutRows.Count; index++)
        {
            var series = activeSeries[index];
            var row = _cursorReadoutRows[index];
            if (series.X.Length == 0
                || coordinates.X < series.X[0]
                || coordinates.X > series.X[^1])
            {
                row.Value.Text = string.Empty;
                continue;
            }

            var nearest = SeriesGeometry.NearestIndex(series.X, coordinates.X);
            if (nearest < 0)
            {
                row.Value.Text = string.Empty;
                continue;
            }

            row.Value.Text = $"  {FormatCursorValue(series.Y[nearest])}";
        }
    }

    private void ChartHost_PrecisionMouseLeave(object sender, MouseEventArgs e) =>
        ClearCursorValues();

    /// <summary>
    /// Frame points are an alternate representation, not an overlay. The
    /// summary series remain in the plot so toggling is instant, but they are
    /// hidden while the detailed frame series are active. Average/reference
    /// lines and the rest of the chart stay visible.
    /// </summary>
    private void ApplyFramePointReplacement()
    {
        var frameDetailActive = _framePointSeriesList.Count > 0;
        var framePlots = new HashSet<Scatter>(_framePointPlots);

        foreach (var plottable in ChartHost.Plot.PlottableList)
        {
            switch (plottable)
            {
                case SignalXY signal:
                    signal.IsVisible = !frameDetailActive;
                    break;
                case Scatter scatter when !framePlots.Contains(scatter):
                    scatter.IsVisible = !frameDetailActive;
                    break;
            }
        }
    }

    /// <summary>
    /// ScottPlot distinguishes major/minor ticks internally. For this UI the
    /// rule is simpler: if a tick has a visible numeric label it is major and
    /// therefore receives a grid line; unlabeled ticks are minor and receive
    /// none. This produces exactly one grid line per visible axis number.
    /// </summary>
    private void NormalizeGridTicks()
    {
        NormalizeTicks(ChartHost.Plot.Axes.Bottom.TickGenerator.Ticks);
        NormalizeTicks(ChartHost.Plot.Axes.Left.TickGenerator.Ticks);
    }

    internal static void NormalizeTicks(Tick[] ticks)
    {
        for (var index = 0; index < ticks.Length; index++)
        {
            var tick = ticks[index];
            var labeled = !string.IsNullOrWhiteSpace(tick.Label);
            if (tick.IsMajor == labeled)
            {
                continue;
            }

            ticks[index] = new Tick(tick.Position, tick.Label, labeled);
        }
    }

    private IReadOnlyList<MetricSeries> ActiveCursorSeries() =>
        _framePointSeriesList.Count > 0 ? _framePointSeriesList : _seriesList;

    private void EnsureCursorReadoutRows()
    {
        if (_metric is null)
        {
            CursorReadoutPanel.Children.Clear();
            _cursorReadoutRows.Clear();
            _cursorReadoutSignature = string.Empty;
            return;
        }

        var seriesList = ActiveCursorSeries();
        var style = ChartStyle.FromApplicationResources();
        var isMultiWorkspace = seriesList.Count > 1
            && seriesList.All(series => !series.IsReference && series.Role == Core.SessionRole.Comparison);
        var colors = seriesList
            .Select(series => ChartPlotBuilder.SeriesColor(
                style,
                series.WorkspaceIndex,
                seriesList.Count,
                series.Role,
                isMultiWorkspace))
            .ToArray();
        var signature = string.Join(
            "|",
            new[] { _metric.Id }
                .Concat(seriesList.Select((series, index) =>
                    $"{series.LabelOrDefault}:{series.Role}:{series.WorkspaceIndex}:{colors[index].ARGB}")));
        if (string.Equals(signature, _cursorReadoutSignature, StringComparison.Ordinal))
        {
            return;
        }

        _cursorReadoutSignature = signature;
        _cursorReadoutRows.Clear();
        CursorReadoutPanel.Children.Clear();

        for (var index = 0; index < seriesList.Count; index++)
        {
            var series = seriesList[index];
            var color = colors[index];
            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, index == 0 ? 0 : 2, 0, 0),
            };
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(ToWpfColor(color)),
            };
            var label = new TextBlock
            {
                Text = series.LabelOrDefault,
                Foreground = (Brush)FindResource("TextSoftBrush"),
                FontSize = (double)FindResource("FontSize.Small"),
            };
            var value = new TextBlock
            {
                Text = string.Empty,
                Foreground = new SolidColorBrush(ToWpfColor(color)),
                FontSize = (double)FindResource("FontSize.Small"),
                FontWeight = FontWeights.SemiBold,
            };

            rowPanel.Children.Add(dot);
            rowPanel.Children.Add(label);
            rowPanel.Children.Add(value);
            CursorReadoutPanel.Children.Add(rowPanel);
            _cursorReadoutRows.Add(new CursorReadoutRow(value));
        }
    }

    private string FormatCursorValue(double value)
    {
        if (_metric is null || !double.IsFinite(value))
        {
            return "--";
        }

        if (_metric.Id == "fps")
        {
            return $"{value:F1} FPS";
        }

        return string.IsNullOrWhiteSpace(_metric.Unit)
            ? $"{value:F2}"
            : $"{value:F2} {_metric.Unit}";
    }

    private void ClearCursorValues()
    {
        foreach (var row in _cursorReadoutRows)
        {
            row.Value.Text = string.Empty;
        }
    }

    private static System.Windows.Media.Color ToWpfColor(ScottPlot.Color color)
    {
        var argb = color.ARGB;
        return System.Windows.Media.Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));
    }

    private sealed record CursorReadoutRow(TextBlock Value);
}
