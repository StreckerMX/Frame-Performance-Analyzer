using System.Windows;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Infrastructure.Exports;
using ScottPlot;

namespace FrameViewAnalyzer.App;

public partial class MainWindow
{
    private async void ImportPortable_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.IsBusy)
        {
            return;
        }

        var path = _dialogs.PickOpenFile(
            "Frame Performance Analyzer data (*.json;*.csv)|*.json;*.csv|JSON (*.json)|*.json|CSV (*.csv)|*.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            var document = await _busy.RunOnThreadPoolAsync(
                "Importing analyzed data",
                () => PortableAnalysisFile.Read(path));
            _viewModel.LoadPortableAnalysis(document, path);
            _dialogs.ShowInfo(
                "Import",
                $"Imported {document.Sessions.Count} benchmark(s) from:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Import", error.Message);
        }
    }

    private async void ExportPortableCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPortableExportContext(out var options, out var isMulti))
        {
            return;
        }

        var stem = PortableFileStem(options, isMulti);
        var path = _dialogs.PickSaveFile(
            $"{stem}.csv",
            "CSV (*.csv)|*.csv",
            ".csv");
        if (path is null)
        {
            return;
        }

        try
        {
            var range = _viewModel.Chart.VisibleBounds;
            var document = BuildPortableDocument(options, isMulti, range);
            var points = await _busy.RunOnThreadPoolAsync(
                "Exporting analyzed CSV",
                () => PortableAnalysisFile.WriteCsv(path, document));
            _dialogs.ShowInfo(
                "Export",
                $"Analyzed data saved with {points:N0} metric point(s) to:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Export", error.Message);
        }
    }

    private async void ExportPortableJson_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPortableExportContext(out var options, out var isMulti))
        {
            return;
        }

        var stem = PortableFileStem(options, isMulti);
        var path = _dialogs.PickSaveFile(
            $"{stem}.json",
            "JSON (*.json)|*.json",
            ".json");
        if (path is null)
        {
            return;
        }

        try
        {
            var range = _viewModel.Chart.VisibleBounds;
            var document = BuildPortableDocument(options, isMulti, range);
            await _busy.RunOnThreadPoolAsync(
                "Exporting analyzed JSON",
                () => PortableAnalysisFile.WriteJson(path, document));
            _dialogs.ShowInfo(
                "Export",
                $"Analyzed data saved with {document.Sessions.Count} benchmark(s) to:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Export", error.Message);
        }
    }

    /// <summary>
    /// Range-aware replacement for the existing PNG entry point. The report
    /// picker is unchanged, but every selected metric is clipped to the chart's
    /// current horizontal window before rendering.
    /// </summary>
    private void ExportVisiblePng_Click(object sender, RoutedEventArgs e)
    {
        if (_busy.IsBusy)
        {
            return;
        }

        if (!TryGetPngExportContext(out var options, out var metrics, out _))
        {
            return;
        }

        var range = _viewModel.Chart.VisibleBounds;
        var previousMetricIds = _settings.Load().LastPngReportMetricIds;
        var dialog = new ExportReportWindow(options, metrics, previousMetricIds) { Owner = this };
        WindowThemeBootstrap.Attach(dialog, _themes);
        dialog.ExportRequested += selection => PerformVisiblePngExport(selection, range);
        dialog.ShowDialog();
    }

    private async void PerformVisiblePngExport(
        ExportReportSelection selection,
        AxisLimits? range)
    {
        if (selection.Sessions.Count == 0 || selection.MetricIds.Count == 0)
        {
            return;
        }

        var isMultiReport = selection.Sessions.All(option => option.IsMultiPeer);
        var byId = selection.Sessions
            .SelectMany(option => option.Session.Catalog)
            .GroupBy(metric => metric.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var groups = await _busy.RunOnThreadPoolAsync(
            "Preparing report",
            () => BuildVisibleReportGroups(selection, byId, isMultiReport, range));
        if (groups.Count == 0)
        {
            _dialogs.ShowInfo("Export", "No selected metrics have data in the visible time range.");
            return;
        }

        var stemSession = selection.Sessions[0].Session;
        var initialFile = ExportReport.BuildPngFileName(
            stemSession,
            selection.MetricIds,
            isMultiReport,
            DateTime.Now);
        var path = _dialogs.PickSaveFile(initialFile, "PNG (*.png)|*.png", ".png");
        if (path is null)
        {
            return;
        }

        try
        {
            var style = ChartStyle.FromApplicationResources();
            var header = BuildReportHeader(selection);
            if (range is { } visible)
            {
                header = header with
                {
                    Lines = header.Lines
                        .Append($"Visible range: {visible.Left:F1}–{visible.Right:F1} s")
                        .ToList(),
                };
            }

            await _busy.RunOnThreadPoolAsync("Exporting report", () =>
            {
                var multiplot = ReportPlotBuilder.Build(groups, style);
                var height = groups.Count * 520 + ReportPlotBuilder.MeasureHeaderHeight(header);
                ReportPlotBuilder.SavePng(multiplot, style, header, path, 1600, height);
            });
            PersistPngReportMetricSelection(selection.MetricIds);
            var rangeText = range is { } selectedRange
                ? $" for {selectedRange.Left:F1}–{selectedRange.Right:F1} s"
                : string.Empty;
            _dialogs.ShowInfo(
                "Export",
                $"Report saved with {selection.Sessions.Count} benchmark(s) and {groups.Count} chart(s){rangeText} to:\n{path}");
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Export", error.Message);
        }
    }

    private static List<ReportPlotBuilder.ReportGroup> BuildVisibleReportGroups(
        ExportReportSelection selection,
        IReadOnlyDictionary<string, MetricDefinition> byId,
        bool isMultiReport,
        AxisLimits? range)
    {
        var groups = new List<ReportPlotBuilder.ReportGroup>();
        foreach (var metricId in selection.MetricIds)
        {
            if (!byId.TryGetValue(metricId, out var metric))
            {
                continue;
            }

            var seriesList = new List<MetricSeries>();
            foreach (var option in selection.Sessions)
            {
                var series = SeriesBuilder.Build(option.Session, metricId);
                if (range is { } visible)
                {
                    series = SelectVisibleSeries(series, visible.Left, visible.Right);
                }

                if (series.Y.Length == 0)
                {
                    continue;
                }

                seriesList.Add(series with
                {
                    Label = option.Label,
                    Role = option.Role,
                    WorkspaceIndex = option.WorkspaceIndex,
                    IsReference = !option.IsMultiPeer && option.Role == SessionRole.Base,
                });
            }

            if (seriesList.Count > 0)
            {
                groups.Add(new ReportPlotBuilder.ReportGroup(
                    metric,
                    seriesList,
                    IsMultiWorkspace: isMultiReport));
            }
        }

        return groups;
    }

    private static MetricSeries SelectVisibleSeries(MetricSeries series, double left, double right)
    {
        var minimum = Math.Min(left, right);
        var maximum = Math.Max(left, right);
        var xs = new List<double>();
        var ys = new List<double>();
        var count = Math.Min(series.X.Length, series.Y.Length);
        for (var index = 0; index < count; index++)
        {
            if (series.X[index] < minimum || series.X[index] > maximum)
            {
                continue;
            }

            xs.Add(series.X[index]);
            ys.Add(series.Y[index]);
        }

        return series with { X = xs.ToArray(), Y = ys.ToArray() };
    }

    private PortableAnalysisDocument BuildPortableDocument(
        IReadOnlyList<ExportSessionOption> options,
        bool isMulti,
        AxisLimits? range)
    {
        var manuals = options
            .GroupBy(option => option.Session.Capture.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => _viewModel.ManualMetadataFor(group.First().Session),
                StringComparer.OrdinalIgnoreCase);
        return PortableAnalysisExport.Build(
            options,
            isMulti,
            range?.Left,
            range?.Right,
            manuals);
    }

    private bool TryGetPortableExportContext(
        out List<ExportSessionOption> options,
        out bool isMulti)
    {
        options = [];
        isMulti = _viewModel.IsMultiMode;

        if (_busy.IsBusy)
        {
            return false;
        }

        if (isMulti)
        {
            if (_viewModel.MultiSessions.Count < 2)
            {
                _dialogs.ShowInfo("Export", "Select at least two Multi benchmarks first.");
                return false;
            }

            options = _viewModel.MultiSessions
                .Select((item, index) => new ExportSessionOption(
                    SessionRole.Comparison,
                    item.Label,
                    item.Session,
                    WorkspaceIndex: index,
                    IsMultiPeer: true))
                .ToList();
            return true;
        }

        if (_viewModel.BaseSession is not { } baseSession)
        {
            _dialogs.ShowInfo("Export", "Load at least one base session.");
            return false;
        }

        options.Add(new ExportSessionOption(
            SessionRole.Base,
            SessionPickerLabel(baseSession),
            baseSession));
        if (_viewModel.ComparisonSession is { } comparison)
        {
            options.Add(new ExportSessionOption(
                SessionRole.Comparison,
                SessionPickerLabel(comparison),
                comparison));
        }

        return true;
    }

    private bool TryGetPngExportContext(
        out List<ExportSessionOption> options,
        out IReadOnlyList<MetricDefinition> metrics,
        out bool isMulti)
    {
        metrics = [];
        if (!TryGetPortableExportContext(out options, out isMulti))
        {
            return false;
        }

        metrics = isMulti
            ? _viewModel.Chart.Metrics.ToList()
            : FrameViewAnalyzer.Analytics.Comparison.ComparisonService.MetricUnion(
                _viewModel.BaseSession!,
                _viewModel.ComparisonSession);
        return true;
    }

    private static string PortableFileStem(
        IReadOnlyList<ExportSessionOption> options,
        bool isMulti)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        if (isMulti)
        {
            return $"MULTI_BENCHMARK_ANALYSIS_{timestamp}";
        }

        var session = options[0].Session;
        var metricIds = session.Catalog.Select(metric => metric.Id).Take(4).ToList();
        return $"{ExportReport.BuildFileStem(session, metricIds)}_{timestamp}";
    }
}
