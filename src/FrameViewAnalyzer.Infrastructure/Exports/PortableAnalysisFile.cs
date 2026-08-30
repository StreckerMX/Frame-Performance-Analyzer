using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Infrastructure.Exports;

/// <summary>
/// Reads and writes Frame Performance Analyzer portable analyzed-data snapshots.
/// JSON is the rich structured representation; CSV is a tidy multi-record
/// representation (document/session/point/statistic rows) carrying the same
/// information and therefore supports the same import round-trip.
/// </summary>
public static class PortableAnalysisFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] CsvFields =
    [
        "record_type",
        "format_version",
        "workspace_mode",
        "range_start_seconds",
        "range_end_seconds",
        "session_index",
        "role",
        "session_name",
        "source",
        "options_json",
        "metadata_json",
        "manual_metadata_json",
        "metric_id",
        "metric",
        "category",
        "unit",
        "direction",
        "time_seconds",
        "value",
        "statistic_key",
        "statistic",
        "base_session",
        "base_value",
        "comparison_session",
        "comparison_value",
        "delta",
        "delta_percent",
        "improvement_kind",
    ];

    public static void WriteJson(string path, PortableAnalysisDocument document)
    {
        PortableAnalysisExport.Validate(document);
        WriteAtomically(path, target =>
            File.WriteAllText(
                target,
                JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine,
                new UTF8Encoding(false)));
    }

    /// <summary>Writes the portable CSV and returns the number of point rows.</summary>
    public static int WriteCsv(string path, PortableAnalysisDocument document)
    {
        PortableAnalysisExport.Validate(document);
        var pointCount = document.Sessions.Sum(session =>
            session.Series.Sum(series => Math.Min(series.TimeSeconds.Count, series.Values.Count)));

        WriteAtomically(path, target =>
        {
            using var writer = new StreamWriter(
                target,
                append: false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            WriteHeader(csv);

            WriteCommon(csv, "document", document);
            csv.NextRecord();

            foreach (var session in document.Sessions.OrderBy(session => session.SessionIndex))
            {
                WriteCommon(csv, "session", document);
                WriteSessionFields(csv, session);
                csv.NextRecord();

                foreach (var series in session.Series)
                {
                    var count = Math.Min(series.TimeSeconds.Count, series.Values.Count);
                    for (var index = 0; index < count; index++)
                    {
                        WriteCommon(csv, "point", document);
                        WriteField(csv, "session_index", session.SessionIndex);
                        WriteField(csv, "metric_id", series.MetricId);
                        WriteField(csv, "metric", series.MetricLabel);
                        WriteField(csv, "category", series.Category);
                        WriteField(csv, "unit", series.Unit);
                        WriteField(csv, "direction", series.Direction);
                        WriteField(csv, "time_seconds", series.TimeSeconds[index]);
                        WriteField(csv, "value", series.Values[index]);
                        csv.NextRecord();
                    }
                }
            }

            foreach (var row in document.Statistics)
            {
                WriteCommon(csv, "statistic", document);
                WriteField(csv, "metric_id", row.MetricId);
                WriteField(csv, "metric", row.MetricLabel);
                WriteField(csv, "category", row.Category);
                WriteField(csv, "unit", row.Unit);
                WriteField(csv, "statistic_key", row.StatisticKey);
                WriteField(csv, "statistic", row.StatisticLabel);
                WriteField(csv, "base_session", row.BaseSession);
                WriteField(csv, "base_value", row.BaseValue);
                WriteField(csv, "comparison_session", row.ComparisonSession);
                WriteField(csv, "comparison_value", row.ComparisonValue);
                WriteField(csv, "delta", row.Delta);
                WriteField(csv, "delta_percent", row.DeltaPercent);
                WriteField(csv, "improvement_kind", row.Kind.ToString());
                csv.NextRecord();
            }
        });

        return pointCount;
    }

    public static PortableAnalysisDocument Read(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? ReadJson(path)
            : extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? ReadCsv(path)
                : throw new InvalidDataException("Only Frame Performance Analyzer .json and .csv data exports can be imported.");
    }

    public static PortableAnalysisDocument ReadJson(string path)
    {
        try
        {
            var document = JsonSerializer.Deserialize<PortableAnalysisDocument>(
                File.ReadAllText(path),
                JsonOptions)
                ?? throw new InvalidDataException("The JSON export is empty.");
            PortableAnalysisExport.Validate(document);
            EnsureSeries(document);
            return document;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The selected file is not a valid Frame Performance Analyzer analyzed-data JSON export.", error);
        }
    }

    public static PortableAnalysisDocument ReadCsv(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        if (!csv.Read() || !csv.ReadHeader())
        {
            throw new InvalidDataException("The CSV export is empty.");
        }

        var header = csv.HeaderRecord ?? [];
        if (!header.Contains("record_type", StringComparer.Ordinal)
            || !header.Contains("format_version", StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "This CSV is not a portable Frame Performance Analyzer analyzed-data export. Older Statistics CSV files cannot recreate chart data.");
        }

        var sessions = new Dictionary<int, SessionAccumulator>();
        var statistics = new List<ComparisonRow>();
        var formatVersion = 0;
        var workspaceMode = string.Empty;
        double? rangeStart = null;
        double? rangeEnd = null;

        while (csv.Read())
        {
            var recordType = Text(csv, "record_type");
            formatVersion = formatVersion == 0 ? Integer(csv, "format_version") ?? 0 : formatVersion;
            if (workspaceMode.Length == 0)
            {
                workspaceMode = Text(csv, "workspace_mode");
            }

            rangeStart ??= Number(csv, "range_start_seconds");
            rangeEnd ??= Number(csv, "range_end_seconds");

            if (recordType.Equals("session", StringComparison.OrdinalIgnoreCase))
            {
                var index = Integer(csv, "session_index")
                    ?? throw new InvalidDataException("A session row is missing session_index.");
                sessions[index] = new SessionAccumulator(
                    index,
                    Text(csv, "role"),
                    Text(csv, "session_name"),
                    Text(csv, "source"),
                    JsonField<AnalysisOptionsDto>(csv, "options_json"),
                    JsonField<SessionMetadataDto>(csv, "metadata_json"),
                    JsonField<ManualMetadata>(csv, "manual_metadata_json"));
                continue;
            }

            if (recordType.Equals("point", StringComparison.OrdinalIgnoreCase))
            {
                var sessionIndex = Integer(csv, "session_index")
                    ?? throw new InvalidDataException("A point row is missing session_index.");
                if (!sessions.TryGetValue(sessionIndex, out var session))
                {
                    throw new InvalidDataException($"Point data references unknown session {sessionIndex}.");
                }

                var metricId = Text(csv, "metric_id");
                if (metricId.Length == 0)
                {
                    throw new InvalidDataException("A point row is missing metric_id.");
                }

                var time = Number(csv, "time_seconds")
                    ?? throw new InvalidDataException($"Metric '{metricId}' has a point without time_seconds.");
                var value = Number(csv, "value")
                    ?? throw new InvalidDataException($"Metric '{metricId}' has a point without value.");
                session.AddPoint(
                    metricId,
                    Text(csv, "metric"),
                    Text(csv, "unit"),
                    Text(csv, "category"),
                    Text(csv, "direction"),
                    time,
                    value);
                continue;
            }

            if (recordType.Equals("statistic", StringComparison.OrdinalIgnoreCase))
            {
                var kind = Enum.TryParse<ImprovementKind>(
                    Text(csv, "improvement_kind"),
                    ignoreCase: true,
                    out var parsedKind)
                    ? parsedKind
                    : ImprovementKind.None;
                statistics.Add(new ComparisonRow(
                    Text(csv, "metric_id"),
                    Text(csv, "metric"),
                    Text(csv, "category"),
                    Text(csv, "unit"),
                    Text(csv, "statistic_key"),
                    Text(csv, "statistic"),
                    Text(csv, "base_session"),
                    Number(csv, "base_value"),
                    Text(csv, "comparison_session"),
                    Number(csv, "comparison_value"),
                    Number(csv, "delta"),
                    Number(csv, "delta_percent"),
                    kind));
            }
        }

        var range = rangeStart is not null && rangeEnd is not null
            ? new PortableRangeDto(rangeStart.Value, rangeEnd.Value)
            : null;
        var document = new PortableAnalysisDocument(
            formatVersion,
            workspaceMode,
            range,
            sessions.Values
                .OrderBy(session => session.SessionIndex)
                .Select(session => session.ToDto())
                .ToList(),
            statistics);
        PortableAnalysisExport.Validate(document);
        EnsureSeries(document);
        return document;
    }

    private static void EnsureSeries(PortableAnalysisDocument document)
    {
        if (document.Sessions.Any(session => session.Series.Count == 0))
        {
            throw new InvalidDataException(
                "This export does not contain portable metric series. Older statistics-only exports cannot recreate the chart workspace.");
        }
    }

    private static void WriteHeader(CsvWriter csv)
    {
        foreach (var field in CsvFields)
        {
            csv.WriteField(field);
        }

        csv.NextRecord();
    }

    private static void WriteCommon(
        CsvWriter csv,
        string recordType,
        PortableAnalysisDocument document)
    {
        WriteField(csv, "record_type", recordType);
        WriteField(csv, "format_version", document.FormatVersion);
        WriteField(csv, "workspace_mode", document.WorkspaceMode);
        WriteField(csv, "range_start_seconds", document.Range?.StartSeconds);
        WriteField(csv, "range_end_seconds", document.Range?.EndSeconds);
    }

    private static void WriteSessionFields(CsvWriter csv, PortableSessionDto session)
    {
        WriteField(csv, "session_index", session.SessionIndex);
        WriteField(csv, "role", session.Role);
        WriteField(csv, "session_name", session.Name);
        WriteField(csv, "source", session.Source);
        WriteField(csv, "options_json", Json(session.Options));
        WriteField(csv, "metadata_json", Json(session.Metadata));
        WriteField(csv, "manual_metadata_json", Json(session.ManualMetadata));
    }

    /// <summary>
    /// CsvHelper writes fields sequentially, so every row must emit every
    /// column in header order. This sparse-row helper writes blank cells until
    /// the requested column and tracks the current writer index.
    /// </summary>
    private static void WriteField(CsvWriter csv, string fieldName, object? value)
    {
        var target = Array.IndexOf(CsvFields, fieldName);
        if (target < 0)
        {
            throw new InvalidOperationException($"Unknown portable CSV field '{fieldName}'.");
        }

        var current = csv.Index;
        while (current < target)
        {
            csv.WriteField(string.Empty);
            current++;
        }

        csv.WriteField(value);
    }

    private static string Json<T>(T? value) => value is null
        ? string.Empty
        : JsonSerializer.Serialize(value, JsonOptions);

    private static T? JsonField<T>(CsvReader csv, string fieldName)
    {
        var text = Text(csv, fieldName);
        if (text.Length == 0)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, JsonOptions);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Invalid {fieldName} in portable CSV.", error);
        }
    }

    private static string Text(CsvReader csv, string fieldName) =>
        csv.TryGetField<string>(fieldName, out var value)
            ? (value ?? string.Empty).Trim()
            : string.Empty;

    private static double? Number(CsvReader csv, string fieldName)
    {
        var text = Text(csv, fieldName);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int? Integer(CsvReader csv, string fieldName)
    {
        var text = Text(csv, fieldName);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static void WriteAtomically(string path, Action<string> write)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        try
        {
            write(temporary);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; preserve the original write exception.
            }

            throw;
        }
    }

    private sealed class SessionAccumulator
    {
        private readonly Dictionary<string, MetricAccumulator> _metrics = new(StringComparer.Ordinal);

        public SessionAccumulator(
            int sessionIndex,
            string role,
            string name,
            string source,
            AnalysisOptionsDto? options,
            SessionMetadataDto? metadata,
            ManualMetadata? manualMetadata)
        {
            SessionIndex = sessionIndex;
            Role = role;
            Name = name;
            Source = source;
            Options = options;
            Metadata = metadata;
            ManualMetadata = manualMetadata;
        }

        public int SessionIndex { get; }
        public string Role { get; }
        public string Name { get; }
        public string Source { get; }
        public AnalysisOptionsDto? Options { get; }
        public SessionMetadataDto? Metadata { get; }
        public ManualMetadata? ManualMetadata { get; }

        public void AddPoint(
            string id,
            string label,
            string unit,
            string category,
            string direction,
            double time,
            double value)
        {
            if (!_metrics.TryGetValue(id, out var metric))
            {
                metric = new MetricAccumulator(id, label, unit, category, direction);
                _metrics[id] = metric;
            }

            metric.Times.Add(time);
            metric.Values.Add(value);
        }

        public PortableSessionDto ToDto() =>
            new(
                SessionIndex,
                Role,
                Name,
                Source,
                Options,
                Metadata,
                ManualMetadata,
                _metrics.Values.Select(metric => metric.ToDto()).ToList());
    }

    private sealed class MetricAccumulator
    {
        public MetricAccumulator(string id, string label, string unit, string category, string direction)
        {
            Id = id;
            Label = label;
            Unit = unit;
            Category = category;
            Direction = direction;
        }

        public string Id { get; }
        public string Label { get; }
        public string Unit { get; }
        public string Category { get; }
        public string Direction { get; }
        public List<double> Times { get; } = [];
        public List<double> Values { get; } = [];

        public PortableMetricSeriesDto ToDto() =>
            new(Id, Label, Unit, Category, Direction, Times, Values);
    }
}
