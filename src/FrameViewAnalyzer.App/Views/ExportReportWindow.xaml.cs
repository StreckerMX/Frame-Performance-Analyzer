using System.Windows;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.App.Charting;
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

    public bool IsMultiPeer => Option.IsMultiPeer;

    public string? ColorHex => IsMultiPeer
        ? MultiSeriesPalette.HexAt(Option.WorkspaceIndex)
        : null;
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
/// Checklist-based PNG report picker. Pair and Multi both carry explicit
/// session and metric collections; Multi items also expose the same stable
/// colors used by the interactive chart and final PNG report. The last
/// successfully exported metric set may be supplied so repeated exports open
/// with the previous checklist restored instead of resetting to FPS only.
/// </summary>
public partial class ExportReportWindow : Window
{
    private readonly IReadOnlyList<ExportSessionChecklistItem> _sessions;
    private readonly IReadOnlyList<ExportMetricChecklistItem> _metrics;

    public ExportReportWindow(
        IReadOnlyList<ExportSessionOption> sessions,
        IReadOnlyList<MetricDefinition> metrics,
        IReadOnlyCollection<string>? preferredMetricIds = null)
    {
        InitializeComponent();

        _sessions = sessions
            .Select(option => new ExportSessionChecklistItem(option))
            .ToList();

        var initialMetricIds = ResolveInitialMetricIds(metrics, preferredMetricIds);
        _metrics = metrics
            .Select(metric => new ExportMetricChecklistItem(
                metric,
                initialMetricIds.Contains(metric.Id)))
            .ToList();

        var isMultiReport = sessions.Count > 0 && sessions.All(option => option.IsMultiPeer);
        ReportTitleTextBox.Text = ExportReportTitles.DefaultTitle(isMultiReport);
        SessionChecklist.ItemsSource = _sessions;
        MetricChecklist.ItemsSource = _metrics;
        UpdateExportEnabled();
    }

    public event Action<ExportReportSelection>? ExportRequested;

    public static bool CanExport(int selectedSessions, int selectedMetrics) =>
        selectedSessions > 0
        && selectedMetrics > 0
        && selectedMetrics <= ExportReport.MaxReportMetrics;

    /// <summary>
    /// Restores the previous export's metrics when they still exist in the
    /// current captures. If none are available, FPS remains the default; if a
    /// capture has no FPS metric, the first available metric is selected.
    /// </summary>
    internal static IReadOnlySet<string> ResolveInitialMetricIds(
        IReadOnlyList<MetricDefinition> metrics,
        IReadOnlyCollection<string>? preferredMetricIds)
    {
        var availableIds = metrics
            .Select(metric => metric.Id)
            .ToHashSet(StringComparer.Ordinal);
        var selected = new HashSet<string>(StringComparer.Ordinal);

        if (preferredMetricIds is not null)
        {
            foreach (var id in preferredMetricIds)
            {
                if (selected.Count >= ExportReport.MaxReportMetrics)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(id) && availableIds.Contains(id))
                {
                    selected.Add(id);
                }
            }
        }

        if (selected.Count == 0 && availableIds.Contains("fps"))
        {
            selected.Add("fps");
        }
        else if (selected.Count == 0 && metrics.Count > 0)
        {
            selected.Add(metrics[0].Id);
        }

        return selected;
    }

    public static ExportReportSelection BuildSelection(
        IEnumerable<ExportSessionChecklistItem> sessions,
        IEnumerable<ExportMetricChecklistItem> metrics,
        string? reportTitle = null)
    {
        var selectedSessions = sessions
            .Where(item => item.IsSelected)
            .Select(item => item.Option)
            .ToList();
        var selectedMetrics = metrics
            .Where(item => item.IsSelected)
            .Select(item => item.Metric.Id)
            .ToList();
        var isMultiReport = selectedSessions.Count > 0
            && selectedSessions.All(option => option.IsMultiPeer);

        return new ExportReportSelection(
            selectedSessions,
            selectedMetrics,
            ExportReportTitles.NormalizeTitle(reportTitle, isMultiReport));
    }

    private ExportReportSelection CurrentSelection() =>
        BuildSelection(_sessions, _metrics, ReportTitleTextBox.Text);

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
