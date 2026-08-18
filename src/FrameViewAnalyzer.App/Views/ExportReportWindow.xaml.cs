using System.Windows;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Core.Metrics;

namespace FrameViewAnalyzer.App.Views;

public sealed class ExportSessionChecklistItem
{
    public ExportSessionChecklistItem(ExportSessionOption option, bool isSelected = true)
    {
        Option = option;
        IsSelected = isSelected;
    }

    public ExportSessionOption Option { get; }

    public string Label => Option.Label;

    public bool IsSelected { get; set; }
}

public sealed class ExportMetricChecklistItem
{
    public ExportMetricChecklistItem(MetricDefinition metric, bool isSelected = false)
    {
        Metric = metric;
        IsSelected = isSelected;
    }

    public MetricDefinition Metric { get; }

    public string Label => string.IsNullOrWhiteSpace(Metric.Unit)
        ? Metric.Label
        : $"{Metric.Label}  ({Metric.Unit})";

    public bool IsSelected { get; set; }
}

/// <summary>
/// Checklist-based PNG report picker. The request already carries explicit
/// session and metric collections so the same contract can later be used by
/// the Multi workspace without another pair-only export API.
/// </summary>
public partial class ExportReportWindow : Window
{
    private readonly IReadOnlyList<ExportSessionChecklistItem> _sessions;
    private readonly IReadOnlyList<ExportMetricChecklistItem> _metrics;

    public ExportReportWindow(
        IReadOnlyList<ExportSessionOption> sessions,
        IReadOnlyList<MetricDefinition> metrics)
    {
        InitializeComponent();

        _sessions = sessions
            .Select(option => new ExportSessionChecklistItem(option))
            .ToList();
        _metrics = metrics
            .Select(metric => new ExportMetricChecklistItem(metric, metric.Id == "fps"))
            .ToList();

        // A capture with no explicit FPS metric should still have a usable
        // default rather than opening a dialog with no metric selected.
        if (_metrics.Count > 0 && !_metrics.Any(item => item.IsSelected))
        {
            _metrics[0].IsSelected = true;
        }

        SessionChecklist.ItemsSource = _sessions;
        MetricChecklist.ItemsSource = _metrics;
        UpdateExportEnabled();
    }

    public event Action<ExportReportSelection>? ExportRequested;

    public static bool CanExport(int selectedSessions, int selectedMetrics) =>
        selectedSessions > 0
        && selectedMetrics > 0
        && selectedMetrics <= ExportReport.MaxReportMetrics;

    public static ExportReportSelection BuildSelection(
        IEnumerable<ExportSessionChecklistItem> sessions,
        IEnumerable<ExportMetricChecklistItem> metrics) =>
        new(
            sessions.Where(item => item.IsSelected).Select(item => item.Option).ToList(),
            metrics.Where(item => item.IsSelected).Select(item => item.Metric.Id).ToList());

    private ExportReportSelection CurrentSelection() => BuildSelection(_sessions, _metrics);

    private void UpdateExportEnabled()
    {
        var selection = CurrentSelection();
        ExportButton.IsEnabled = CanExport(selection.Sessions.Count, selection.MetricIds.Count);

        SelectionStatus.Text = selection.MetricIds.Count > ExportReport.MaxReportMetrics
            ? $"Choose up to {ExportReport.MaxReportMetrics} metrics to keep the PNG report readable."
            : $"{selection.Sessions.Count} benchmark(s) · {selection.MetricIds.Count} metric(s) selected · "
              + $"maximum {ExportReport.MaxReportMetrics} metrics";
    }

    private void Selection_CheckedChanged(object sender, RoutedEventArgs e) => UpdateExportEnabled();

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var selection = CurrentSelection();
        if (!CanExport(selection.Sessions.Count, selection.MetricIds.Count))
        {
            return;
        }

        ExportRequested?.Invoke(selection);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
