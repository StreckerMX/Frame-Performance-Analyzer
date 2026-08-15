using System.Text.RegularExpressions;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Library;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Exports;

/// <summary>
/// Pure export report helpers ported from the Python reference: report
/// metric selection, file stems, session labels, and the statistics /
/// benchmark-package documents.
/// </summary>
public static class ExportReport
{
    public const int MaxReportMetrics = 8;

    public const int PackageVersion = 1;

    private static readonly string[][] ReportMetricGroups =
    [
        ["fps"],
        ["frametime"],
        ["latency", "render_present_latency", "until_displayed"],
        ["gpu0_util"],
        ["gpu0_temp"],
        ["nv_power", "gpu_only_power", "pcat_power"],
        ["cpu_util"],
        ["cpu_temp", "cpu_power"],
    ];

    /// <summary>Useful complementary metrics for the PNG report.</summary>
    public static IReadOnlyList<string> SelectReportMetricIds(
        IReadOnlyList<MetricDefinition> catalog,
        string visibleMetricId,
        int maximum = MaxReportMetrics)
    {
        var available = catalog.Select(metric => metric.Id).ToHashSet(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var alternatives in ReportMetricGroups)
        {
            var selected = alternatives.FirstOrDefault(available.Contains);
            if (selected is not null && !result.Contains(selected))
            {
                result.Add(selected);
            }
        }

        if (available.Contains(visibleMetricId) && !result.Contains(visibleMetricId))
        {
            result.Insert(result.Count > 0 && result[0] == "fps" ? 1 : 0, visibleMetricId);
        }

        return result.Take(maximum).ToList();
    }

    /// <summary>Suggested file stem for the PNG report, like the Python reference.</summary>
    public static string BuildFileStem(SessionAnalysis session, IReadOnlyList<string> metricIds)
    {
        var application = session.Metadata?.Application ?? string.Empty;
        var game = DisplayText.CleanGameName(
            string.IsNullOrWhiteSpace(application) || application == "--"
                ? session.Capture.DisplayName
                : application);
        var sanitized = Regex.Replace(game, "[^A-Za-z0-9._-]+", "_").Trim('_', '.');
        if (sanitized.Length == 0)
        {
            sanitized = "benchmark";
        }

        var metrics = string.Join("_", metricIds.Take(4));
        if (metrics.Length == 0)
        {
            metrics = "chart";
        }

        return $"FrameView_{sanitized}_{metrics}";
    }

    /// <summary>Human-readable export label, e.g. "GTA5 Enhanced — 3840x2160".</summary>
    public static string SessionExportLabel(SessionAnalysis session)
    {
        var application = session.Metadata?.Application ?? string.Empty;
        var game = DisplayText.CleanGameName(
            string.IsNullOrWhiteSpace(application) || application == "--"
                ? session.Capture.DisplayName
                : application);
        var resolution = session.Metadata?.Resolution;
        return string.IsNullOrEmpty(resolution) || resolution == "--"
            ? game
            : $"{game} — {resolution}";
    }

    /// <summary>The statistics rows reusing the comparison service output.</summary>
    public static IReadOnlyList<ComparisonRow> BuildStatisticsRows(
        SessionAnalysis baseSession,
        SessionAnalysis? comparisonSession = null) =>
        new ComparisonService().Compare(baseSession, comparisonSession);

    public static ExportStatisticsDto BuildStatisticsPayload(
        SessionAnalysis baseSession,
        SessionAnalysis? comparisonSession = null,
        ManualMetadata? baseManual = null,
        ManualMetadata? comparisonManual = null)
    {
        var sessions = new List<ExportSessionDto>
        {
            SessionDto(baseSession, "base", baseManual),
        };
        if (comparisonSession is not null)
        {
            sessions.Add(SessionDto(comparisonSession, "comparison", comparisonManual));
        }

        return new ExportStatisticsDto(
            1,
            [.. sessions],
            BuildStatisticsRows(baseSession, comparisonSession));
    }

    public static BenchmarkPackageDto BuildBenchmarkPackage(
        LibraryModel library,
        IReadOnlyDictionary<string, ManualMetadata>? manualLookup = null,
        IEnumerable<LibraryRecord>? records = null)
    {
        var manual = manualLookup ?? new Dictionary<string, ManualMetadata>();
        var selected = (records ?? library.Records.Values)
            .OrderBy(record => record.SourceName, StringComparer.Ordinal)
            .ToList();
        var captures = new List<PackageCaptureDto>();
        foreach (var record in selected)
        {
            captures.Add(new PackageCaptureDto(
                record.Identity,
                record.SourcePath,
                record.SourceName,
                record.Available,
                new PackageDetectedDto(
                    record.Game,
                    record.Resolution,
                    record.Gpu,
                    record.Cpu,
                    record.DurationSeconds),
                manual.TryGetValue(record.Identity, out var value) ? value : null,
                record.StatsSummary,
                record.AnalysisOptions));
        }

        return new BenchmarkPackageDto(
            PackageVersion,
            LibraryUpdater.NowIso(),
            [.. captures]);
    }

    private static ExportSessionDto SessionDto(
        SessionAnalysis session,
        string role,
        ManualMetadata? manual) =>
        new(
            role,
            session.Capture.DisplayName,
            Path.GetFileName(session.Capture.Path),
            new AnalysisOptionsDto(
                session.EffectiveOptions.GpuThreshold,
                session.EffectiveOptions.TrimBufferSeconds,
                session.EffectiveOptions.AutoGpuThreshold,
                session.EffectiveOptions.ExcludeTransitions),
            session.Metadata is { } metadata
                ? new SessionMetadataDto(
                    metadata.Application,
                    metadata.Resolution,
                    metadata.Gpu,
                    metadata.Cpu,
                    metadata.Runtime,
                    metadata.Duration,
                    metadata.CaptureDuration,
                    metadata.FrameCount,
                    metadata.MetricCount)
                : null,
            manual);
}

/// <summary>Structured statistics document (Benchmark JSON).</summary>
public sealed record ExportStatisticsDto(
    int FormatVersion,
    IReadOnlyList<ExportSessionDto> Sessions,
    IReadOnlyList<ComparisonRow> Statistics);

public sealed record ExportSessionDto(
    string Role,
    string Name,
    string Source,
    AnalysisOptionsDto? Options,
    SessionMetadataDto? Metadata,
    ManualMetadata? ManualMetadata);

public sealed record AnalysisOptionsDto(
    double GpuThreshold,
    double TrimBufferSeconds,
    bool AutoGpuThreshold,
    bool ExcludeTransitions);

public sealed record SessionMetadataDto(
    string Application,
    string Resolution,
    string Gpu,
    string Cpu,
    string Runtime,
    string Duration,
    string CaptureDuration,
    int FrameCount,
    int MetricCount);

/// <summary>Portable benchmark package (raw CSV contents are never embedded).</summary>
public sealed record BenchmarkPackageDto(
    int PackageVersion,
    string ExportedAt,
    IReadOnlyList<PackageCaptureDto> Captures);

public sealed record PackageCaptureDto(
    string Identity,
    string SourcePath,
    string SourceName,
    bool SourceAvailable,
    PackageDetectedDto Detected,
    ManualMetadata? ManualMetadata,
    IReadOnlyDictionary<string, double> StatsSummary,
    IReadOnlyDictionary<string, string>? AnalysisOptions = null);

public sealed record PackageDetectedDto(
    string Game,
    string Resolution,
    string Gpu,
    string Cpu,
    double? DurationSeconds);
