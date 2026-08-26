using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Filtering;
using FrameViewAnalyzer.Analytics.Samples;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Analytics.Statistics;
using FrameViewAnalyzer.Core.Metrics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Exports;

/// <summary>
/// Round-trippable analyzed-data snapshot used by the main-window CSV/JSON
/// exports. Unlike a raw FrameView capture, this format intentionally stores
/// the already-analyzed one-point-per-bin series so importing it reproduces
/// exactly the data and time window the user exported.
/// </summary>
public static class PortableAnalysisExport
{
    public const int FormatVersion = 2;

    public static PortableAnalysisDocument Build(
        IReadOnlyList<ExportSessionOption> sessions,
        bool isMultiWorkspace,
        double? rangeStartSeconds = null,
        double? rangeEndSeconds = null,
        IReadOnlyDictionary<string, ManualMetadata?>? manualMetadataByPath = null)
    {
        if (sessions.Count == 0)
        {
            throw new ArgumentException("At least one session is required.", nameof(sessions));
        }

        var range = NormalizeRange(rangeStartSeconds, rangeEndSeconds);
        var portableSessions = sessions
            .Select((option, index) => SessionDto(
                option,
                index,
                range,
                manualMetadataByPath is not null
                    && manualMetadataByPath.TryGetValue(option.Session.Capture.Path, out var manual)
                    ? manual
                    : null))
            .ToList();

        IReadOnlyList<ComparisonRow> statistics = [];
        if (!isMultiWorkspace)
        {
            var baseOption = sessions.FirstOrDefault(option => option.Role == SessionRole.Base)
                ?? sessions[0];
            var comparisonOption = sessions.FirstOrDefault(option => option.Role == SessionRole.Comparison);
            statistics = BuildStatisticsRows(
                baseOption.Session,
                comparisonOption?.Session,
                range);
        }

        return new PortableAnalysisDocument(
            FormatVersion,
            isMultiWorkspace ? "multi" : "pair",
            range,
            portableSessions,
            statistics);
    }

    /// <summary>
    /// Restores one or more chart-ready SessionAnalysis snapshots from an
    /// analyzed-data document. Raw CSV structures remain empty by design;
    /// SeriesBuilder reads ImportedSeries directly.
    /// </summary>
    public static IReadOnlyList<PortableImportedSession> RestoreSessions(
        PortableAnalysisDocument document,
        string importPath)
    {
        Validate(document);
        var result = new List<PortableImportedSession>(document.Sessions.Count);

        for (var sessionIndex = 0; sessionIndex < document.Sessions.Count; sessionIndex++)
        {
            var source = document.Sessions[sessionIndex];
            var definitions = new List<MetricDefinition>();
            var imported = new Dictionary<string, ImportedSeriesData>(StringComparer.Ordinal);

            foreach (var series in source.Series)
            {
                if (series.TimeSeconds.Count != series.Values.Count)
                {
                    throw new InvalidDataException(
                        $"Metric '{series.MetricId}' in session '{source.Name}' has mismatched time/value counts.");
                }

                if (series.TimeSeconds.Count == 0)
                {
                    continue;
                }

                var direction = Enum.TryParse<MetricDirection>(series.Direction, ignoreCase: true, out var parsed)
                    ? parsed
                    : MetricDirection.Undefined;
                definitions.Add(new MetricDefinition(
                    series.MetricId,
                    series.MetricLabel,
                    series.Unit,
                    series.Category,
                    [],
                    direction,
                    Computed: true));
                imported[series.MetricId] = new ImportedSeriesData(
                    series.TimeSeconds.ToArray(),
                    series.Values.ToArray());
            }

            if (imported.Count == 0)
            {
                throw new InvalidDataException(
                    $"Session '{source.Name}' does not contain importable metric series.");
            }

            var allXs = imported.Values.SelectMany(series => series.X).ToArray();
            var minimum = allXs.Min();
            var maximum = allXs.Max();
            var visibleBins = imported.TryGetValue("fps", out var fps)
                ? fps.X.Length
                : imported.Values.Max(series => series.X.Length);
            var options = source.Options is null
                ? new AnalysisOptions()
                : new AnalysisOptions(
                    source.Options.GpuThreshold,
                    source.Options.TrimBufferSeconds,
                    source.Options.AutoGpuThreshold,
                    source.Options.ExcludeTransitions);
            var metadata = source.Metadata is null
                ? null
                : new SessionMetadata(
                    source.Metadata.Application,
                    source.Metadata.Resolution,
                    source.Metadata.Gpu,
                    source.Metadata.Cpu,
                    source.Metadata.Runtime,
                    source.Metadata.Duration,
                    source.Metadata.CaptureDuration,
                    source.Metadata.FrameCount,
                    source.Metadata.MetricCount);

            var portableName = Path.GetFileName(importPath);
            var session = new SessionAnalysis
            {
                Capture = new CaptureData
                {
                    Path = $"portable://{portableName}#{sessionIndex}",
                    DisplayName = source.Name,
                    Kind = CsvKind.Log,
                    Headers = [],
                    Columns = [],
                },
                Catalog = definitions
                    .GroupBy(metric => metric.Id, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList(),
                Samples = new ParsedSamples
                {
                    TimeSeconds = [],
                    FrametimeMs = [],
                    Fps = [],
                    GpuUtilPercent = [],
                    RowIndex = [],
                },
                EffectiveOptions = options,
                Bins = [],
                RowsByBin = new Dictionary<int, int[]>(),
                Window = new ActiveWindow(minimum, maximum),
                ValidBins = new HashSet<int>(),
                Diagnostics = new FilterDiagnostics(
                    TotalBins: visibleBins,
                    VisibleBins: visibleBins),
                Metadata = metadata,
                ImportedSeries = imported,
                ImportedManualMetadata = source.ManualMetadata,
            };

            result.Add(new PortableImportedSession(
                session,
                source.Role,
                source.Name,
                source.SessionIndex));
        }

        return result;
    }

    public static void Validate(PortableAnalysisDocument document)
    {
        if (document.FormatVersion != FormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported FrameView Analyzer data format version {document.FormatVersion}. Expected {FormatVersion}.");
        }

        if (document.Sessions.Count == 0)
        {
            throw new InvalidDataException("The export does not contain any sessions.");
        }

        if (!string.Equals(document.WorkspaceMode, "pair", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(document.WorkspaceMode, "multi", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The export has an invalid workspace mode.");
        }
    }

    private static PortableSessionDto SessionDto(
        ExportSessionOption option,
        int sessionIndex,
        PortableRangeDto? range,
        ManualMetadata? manual)
    {
        var session = option.Session;
        var series = new List<PortableMetricSeriesDto>();
        foreach (var metric in session.Catalog)
        {
            var built = SeriesBuilder.Build(session, metric.Id);
            if (built.X.Length == 0)
            {
                continue;
            }

            var (xs, ys) = SelectRange(built.X, built.Y, range);
            if (xs.Length == 0)
            {
                continue;
            }

            series.Add(new PortableMetricSeriesDto(
                metric.Id,
                metric.Label,
                metric.Unit,
                metric.Category,
                metric.Direction.ToString(),
                xs,
                ys));
        }

        var role = option.IsMultiPeer
            ? "multi"
            : option.Role == SessionRole.Base ? "base" : "comparison";
        return new PortableSessionDto(
            sessionIndex,
            role,
            option.Label,
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
            manual,
            series);
    }

    private static IReadOnlyList<ComparisonRow> BuildStatisticsRows(
        SessionAnalysis baseSession,
        SessionAnalysis? comparisonSession,
        PortableRangeDto? range)
    {
        var metrics = ComparisonService.MetricUnion(baseSession, comparisonSession);
        var comparisonName = comparisonSession?.Capture.DisplayName ?? string.Empty;
        var rows = new List<ComparisonRow>();

        foreach (var metric in metrics)
        {
            var baseStats = StatisticsFor(baseSession, metric, range);
            var comparisonStats = comparisonSession is null
                ? null
                : StatisticsFor(comparisonSession, metric, range);

            foreach (var (key, label) in CoreMetricCatalog.StatFields(metric.Id))
            {
                var baseValue = ValueFor(baseStats, key);
                var comparisonValue = comparisonSession is null
                    ? null
                    : ValueFor(comparisonStats, key);
                var (delta, deltaPercent) = ComparisonService.ComputeDelta(baseValue, comparisonValue);
                rows.Add(new ComparisonRow(
                    metric.Id,
                    metric.Label,
                    metric.Category,
                    metric.Unit,
                    key,
                    label,
                    baseSession.Capture.DisplayName,
                    baseValue,
                    comparisonName,
                    comparisonValue,
                    delta,
                    deltaPercent,
                    CoreMetricCatalog.ClassifyImprovement(metric.Direction, baseValue, comparisonValue)));
            }
        }

        return rows;
    }

    private static MetricStatistics? StatisticsFor(
        SessionAnalysis session,
        MetricDefinition metric,
        PortableRangeDto? range)
    {
        var series = SeriesBuilder.Build(session, metric.Id);
        if (series.Y.Length == 0)
        {
            return null;
        }

        if (range is null)
        {
            return StatisticsCalculator.Compute(metric, series.Y);
        }

        var (stats, _) = VisibleRangeCalculator.Compute(
            metric,
            series.X,
            series.Y,
            range.StartSeconds,
            range.EndSeconds);
        return stats;
    }

    private static double? ValueFor(MetricStatistics? stats, string key) => key switch
    {
        "avg" => stats?.Avg,
        "min" => stats?.Min,
        "max" => stats?.Max,
        "p1" => stats?.P1,
        "p01" => stats?.P01,
        _ => null,
    };

    private static PortableRangeDto? NormalizeRange(double? start, double? end)
    {
        if (start is null || end is null
            || !double.IsFinite(start.Value)
            || !double.IsFinite(end.Value))
        {
            return null;
        }

        var left = Math.Min(start.Value, end.Value);
        var right = Math.Max(start.Value, end.Value);
        return right - left > 1e-9 ? new PortableRangeDto(left, right) : null;
    }

    private static (double[] X, double[] Y) SelectRange(
        IReadOnlyList<double> x,
        IReadOnlyList<double> y,
        PortableRangeDto? range)
    {
        if (range is null)
        {
            return (x.ToArray(), y.ToArray());
        }

        var xs = new List<double>();
        var ys = new List<double>();
        var count = Math.Min(x.Count, y.Count);
        for (var index = 0; index < count; index++)
        {
            if (x[index] < range.StartSeconds || x[index] > range.EndSeconds)
            {
                continue;
            }

            xs.Add(x[index]);
            ys.Add(y[index]);
        }

        return (xs.ToArray(), ys.ToArray());
    }
}

public sealed record PortableAnalysisDocument(
    int FormatVersion,
    string WorkspaceMode,
    PortableRangeDto? Range,
    IReadOnlyList<PortableSessionDto> Sessions,
    IReadOnlyList<ComparisonRow> Statistics);

public sealed record PortableRangeDto(
    double StartSeconds,
    double EndSeconds);

public sealed record PortableSessionDto(
    int SessionIndex,
    string Role,
    string Name,
    string Source,
    AnalysisOptionsDto? Options,
    SessionMetadataDto? Metadata,
    ManualMetadata? ManualMetadata,
    IReadOnlyList<PortableMetricSeriesDto> Series);

public sealed record PortableMetricSeriesDto(
    string MetricId,
    string MetricLabel,
    string Unit,
    string Category,
    string Direction,
    IReadOnlyList<double> TimeSeconds,
    IReadOnlyList<double> Values);

public sealed record PortableImportedSession(
    SessionAnalysis Session,
    string Role,
    string Label,
    int SessionIndex);
