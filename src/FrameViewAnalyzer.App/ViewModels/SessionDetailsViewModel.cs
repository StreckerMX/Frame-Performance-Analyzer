using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Filtering;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>One labeled row of the details view.</summary>
public sealed record DetailRow(string Label, string Value);

/// <summary>A titled group of detail rows.</summary>
public sealed record DetailSection(string Title, IReadOnlyList<DetailRow> Rows);

/// <summary>
/// Read-only presentation of one analyzed session, mirroring the Python
/// "Complete data" window: how the results were obtained, benchmark identity,
/// system information, frame presentation, additional recorded data, and one
/// telemetry card per metric. Built once from a SessionAnalysis snapshot; it
/// never mutates session state.
/// </summary>
public sealed partial class SessionDetailsViewModel : ObservableObject
{
    private static readonly HashSet<string> KnownColumns = new(StringComparer.Ordinal)
    {
        "TimeInSeconds",
        "Timestamp (Elapsed time in seconds)",
        "MsBetweenPresents",
        "MsBetweenDisplayChange",
        "FPS",
        "Application",
        "Resolution",
        "Runtime",
        "Timestamp",
        "TimeStamp",
        "GPU",
        "GPU0",
        "GPU1",
        "CPU",
        "Operating system",
        "System memory",
        "Motherboard",
        "GPU base driver",
        "GPU driver package",
        "Presentation mode",
        "Tearing allowed",
        "Synchronization interval",
        "Present flags",
        "Process ID",
        "Swap-chain address",
        "Flip token",
    };

    [ObservableProperty]
    private string _title = "Complete data";

    public ObservableCollection<DetailSection> Sections { get; } = [];

    public SessionDetailsViewModel(SessionAnalysis session)
    {
        Title = $"Complete data · {session.Capture.DisplayName}";
        BuildSections(session);
    }

    private void BuildSections(SessionAnalysis session)
    {
        Sections.Add(HowResultsObtained(session));
        Sections.Add(Identity(session));
        Sections.Add(SystemUsed(session));
        Sections.Add(FramePresentation(session));
        AdditionalData(session);
        Telemetry(session);
    }

    private DetailSection HowResultsObtained(SessionAnalysis session)
    {
        var metadata = session.Metadata;
        var diagnostics = session.Diagnostics;
        var options = session.EffectiveOptions;
        var rows = new List<DetailRow>
        {
            new("Analyzed file", session.Capture.DisplayName),
            new(
                "Capture duration",
                metadata?.CaptureDuration ?? DisplayText.FormatDuration(0)),
            new(
                "Analyzed duration",
                metadata?.Duration ?? DisplayText.FormatDuration(0)),
            new("Recorded frames", $"{metadata?.FrameCount ?? 0:N0} frames"),
            new("Available telemetry", $"{metadata?.MetricCount ?? 0:N0} metrics"),
            new("Time used for statistics", StatisticsTime(diagnostics)),
            new("GPU activity filter", GpuFilterText(options)),
            new("Edge adjustment", EdgeText(options)),
            new("Loads and transitions", TransitionsText(diagnostics)),
        };
        if (diagnostics.FpsUpperBound is { } bound)
        {
            rows.Add(new("Abnormal-FPS detection limit", $"{bound:F1} FPS · bins above this value are treated as transitions"));
        }

        return new DetailSection("How the results were obtained", rows);
    }

    private static string StatisticsTime(FilterDiagnostics diagnostics)
    {
        var total = diagnostics.TotalBins;
        var visible = diagnostics.VisibleBins;
        var percent = total > 0 ? visible * 100.0 / total : 0.0;
        return $"{visible:N0} valid seconds out of {total:N0} captured seconds ({percent:F1}%)";
    }

    private static string GpuFilterText(AnalysisOptions options) =>
        options.AutoGpuThreshold
            ? $"Automatic · kept seconds with at least {options.GpuThreshold:F0}% average GPU utilization"
            : $"Manual · minimum {options.GpuThreshold:F0}% average GPU utilization";

    private static string EdgeText(AnalysisOptions options) =>
        options.TrimBufferSeconds > 0
            ? $"Removed {options.TrimBufferSeconds:F1} s from the start and end of the detected segment"
            : "No edge adjustment";

    private static string TransitionsText(FilterDiagnostics diagnostics)
    {
        var parts = new List<string>();
        if (diagnostics.FpsOutlierBins > 0)
        {
            parts.Add($"{diagnostics.FpsOutlierBins:N0} s of loading screens / abnormal FPS excluded");
        }

        if (diagnostics.BelowGpuBins > 0)
        {
            parts.Add($"{diagnostics.BelowGpuBins:N0} s below the GPU utilization threshold");
        }

        return parts.Count == 0
            ? "Included in statistics and the chart"
            : string.Join(" · ", parts);
    }

    private static DetailSection Identity(SessionAnalysis session) =>
        new(
            "Benchmark identity",
            [
                new("Application", Clean(session.Metadata?.Application)),
                new("Resolution", Clean(session.Metadata?.Resolution)),
                new("Runtime", RuntimeText(FirstRow(session, "Runtime"))),
                new("Timestamp", FirstRow(session, "Timestamp") ?? FirstRow(session, "TimeStamp") ?? "—"),
            ]);

    private static DetailSection SystemUsed(SessionAnalysis session) =>
        new(
            "System used",
            [
                new("GPU", FirstRow(session, "GPU") ?? "—"),
                new("GPU 0", FirstRow(session, "GPU0") ?? "—"),
                new("GPU 1", FirstRow(session, "GPU1") ?? "—"),
                new("CPU", FirstRow(session, "CPU") ?? "—"),
                new("Operating system", FirstRow(session, "Operating system") ?? "—"),
                new("System memory", FirstRow(session, "System memory") ?? "—"),
                new("Motherboard", FirstRow(session, "Motherboard") ?? "—"),
                new("GPU base driver", FirstRow(session, "GPU base driver") ?? "—"),
                new("GPU driver package", FirstRow(session, "GPU driver package") ?? "—"),
            ]);

    private static DetailSection FramePresentation(SessionAnalysis session) =>
        new(
            "Frame presentation",
            [
                new("Presentation mode", PresentationModeText(FirstRow(session, "Presentation mode"))),
                new("Tearing allowed", TearingText(FirstRow(session, "Tearing allowed"))),
                new("Synchronization interval", SyncIntervalText(FirstRow(session, "Synchronization interval"))),
                new("Present flags", SuffixText(FirstRow(session, "Present flags"), "technical presentation-API value")),
                new("Process ID", FirstRow(session, "Process ID") ?? "—"),
                new("Swap-chain address", SuffixText(FirstRow(session, "Swap-chain address"), "internal swap-chain identifier")),
                new("Flip token", SuffixText(FirstRow(session, "Flip token"), "internal presentation identifier")),
            ]);

    private void AdditionalData(SessionAnalysis session)
    {
        var metricColumns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var metric in session.Catalog)
        {
            foreach (var key in metric.ColumnKeys)
            {
                metricColumns.Add(key);
            }
        }

        var rows = new List<DetailRow>();
        foreach (var header in session.Capture.Headers)
        {
            if (KnownColumns.Contains(header) || metricColumns.Contains(header))
            {
                continue;
            }

            var value = FirstRow(session, header);
            if (value is not null)
            {
                rows.Add(new DetailRow(header, value));
            }
        }

        if (rows.Count > 0)
        {
            Sections.Add(new DetailSection("Additional data recorded by FrameView", rows));
        }
    }

    private void Telemetry(SessionAnalysis session)
    {
        var byCategory = session.Catalog
            .GroupBy(metric => metric.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in byCategory)
        {
            var rows = new List<DetailRow>();
            foreach (var metric in group.OrderBy(metric => metric.Label, StringComparer.Ordinal))
            {
                var values = SeriesBuilder.Values(session, metric.Id);
                var stats = StatisticsCalculator.Compute(metric, values);
                var statisticsParts = new List<string>();
                foreach (var (key, label) in CoreMetricCatalog.StatFields(metric.Id))
                {
                    var value = ValueFor(stats, key);
                    if (value is not null)
                    {
                        statisticsParts.Add($"{label}: {DisplayText.FormatStat(value.Value, metric.Unit)}");
                    }
                }

                rows.Add(new DetailRow(
                    metric.Label,
                    statisticsParts.Count > 0
                        ? $"{string.Join("   ·   ", statisticsParts)}"
                        : "No analyzable data"));
                rows.Add(new DetailRow(
                    $"{metric.Label} — analysis",
                    $"{session.ValidBins.Count:N0} analyzed seconds · 1 aggregated value per second"));
                rows.Add(new DetailRow(
                    $"{metric.Label} — description",
                    CoreMetricCatalog.DescriptionFor(metric)));
                rows.Add(new DetailRow(
                    $"{metric.Label} — direction",
                    CoreMetricCatalog.DirectionLabelFor(metric)));
            }

            Sections.Add(new DetailSection($"Telemetry · {group.Key}", rows));
        }
    }

    private static double? ValueFor(MetricStatistics stats, string key) => key switch
    {
        "avg" => stats.Avg,
        "min" => stats.Min,
        "max" => stats.Max,
        "p1" => stats.P1,
        "p01" => stats.P01,
        _ => null,
    };

    private static string? FirstRow(SessionAnalysis session, string header)
    {
        var capture = session.Capture;
        var index = capture.IndexOfHeader(header);
        if (index < 0)
        {
            return null;
        }

        for (var row = 0; row < capture.RowCount; row++)
        {
            var value = capture.Cell(index, row).Trim();
            if (!CsvValues.IsNa(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) || value == "--" ? "—" : value!;

    private static string RuntimeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "other")
        {
            return "Not identified by FrameView";
        }

        return value!;
    }

    private static string PresentationModeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "—";
        }

        return value!
            .Replace("Hardware Composed:", "Hardware composition ·")
            .Replace("Composed:", "Windows composition ·");
    }

    private static string TearingText(string? value) => value switch
    {
        null or "" => "—",
        "0" => "Not allowed",
        "1" => "Allowed",
        _ => value!,
    };

    private static string SyncIntervalText(string? value) => value switch
    {
        null or "" => "—",
        "0" => "0 · no mandatory V-SYNC wait",
        _ => value!,
    };

    private static string SuffixText(string? value, string suffix) =>
        value is null or "" ? "—" : $"{value} · {suffix}";
}
