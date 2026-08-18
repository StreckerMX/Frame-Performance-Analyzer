using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Infrastructure.Csv;

/// <summary>
/// Default reader for supported performance CSVs. Encodings: strict UTF-8
/// (BOM-aware) → Windows-1252 with exception fallback (undefined bytes fail,
/// like Python's cp1252 codec) → ISO-8859-1 (maps every byte). Cells are
/// trimmed and missing fields become empty strings.
/// </summary>
public sealed class FrameViewCsvReader : IFrameViewCsvReader
{
    private static readonly CsvConfiguration Configuration = new(CultureInfo.InvariantCulture)
    {
        BadDataFound = null,
        MissingFieldFound = null,
        TrimOptions = TrimOptions.Trim,
    };

    private static readonly CsvConfiguration RawConfiguration = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = false,
        BadDataFound = null,
        MissingFieldFound = null,
        TrimOptions = TrimOptions.Trim,
    };

    // Initialized in the static constructor AFTER the code-pages provider
    // is registered; field initializers would run before it and throw.
    private static readonly Encoding[] Encodings;

    static FrameViewCsvReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encodings = CreateEncodings();
    }

    private static Encoding[] CreateEncodings() =>
    [
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
        Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ExceptionFallback),
        Encoding.GetEncoding(28591, EncoderFallback.ReplacementFallback, DecoderFallback.ExceptionFallback),
    ];

    public CsvKind DetectKind(IReadOnlyList<string> headers, string fileName) =>
        DetectKindCore(headers, fileName);

    public async Task<CaptureData> LoadCaptureAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var fileName = Path.GetFileName(fullPath);
        Exception? lastError = null;

        foreach (var encoding in Encodings)
        {
            try
            {
                using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1 << 16,
                    FileOptions.SequentialScan);
                using var text = new StreamReader(
                    stream,
                    encoding,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1 << 16);
                using var csv = new CsvReader(text, Configuration);

                if (!await csv.ReadAsync().ConfigureAwait(false))
                {
                    return EmptyCapture(fullPath, fileName, Array.Empty<string>());
                }

                csv.ReadHeader();
                var headers = (IReadOnlyList<string>)(csv.HeaderRecord ?? Array.Empty<string>());

                var columnLists = new List<string>[headers.Count];
                for (var i = 0; i < columnLists.Length; i++)
                {
                    columnLists[i] = [];
                }

                while (await csv.ReadAsync().ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var i = 0; i < headers.Count; i++)
                    {
                        columnLists[i].Add(csv.GetField(i) ?? string.Empty);
                    }
                }

                var columns = new string[headers.Count][];
                for (var i = 0; i < headers.Count; i++)
                {
                    columns[i] = columnLists[i].ToArray();
                }

                return new CaptureData
                {
                    Path = fullPath,
                    DisplayName = CaptureFileNaming.SanitizeDisplayName(fileName),
                    Kind = DetectKind(headers, fileName),
                    Headers = headers,
                    Columns = columns,
                };
            }
            catch (DecoderFallbackException error)
            {
                lastError = error;
            }
        }

        throw new InvalidDataException(
            $"Could not decode CSV '{fullPath}' with any supported encoding.",
            lastError);
    }

    public async Task<CaptureInfo?> ReadCaptureInfoAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);

        foreach (var encoding in Encodings)
        {
            try
            {
                using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1 << 16,
                    FileOptions.SequentialScan);
                using var text = new StreamReader(
                    stream,
                    encoding,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1 << 16);
                using var csv = new CsvReader(text, RawConfiguration);

                if (!await csv.ReadAsync().ConfigureAwait(false))
                {
                    return null;
                }

                var headers = RowFields(csv);
                var timeIndex = Array.IndexOf(headers, "TimeInSeconds");
                if (timeIndex < 0)
                {
                    timeIndex = Array.IndexOf(headers, CaptureSourceDetector.NvidiaAppTimeHeader);
                }

                var presentsIndex = Array.IndexOf(headers, "MsBetweenPresents");
                if (timeIndex < 0 && presentsIndex < 0)
                {
                    return null;
                }

                var applicationIndex = Array.IndexOf(headers, "Application");
                var resolutionIndex = Array.IndexOf(headers, "Resolution");
                var gpuIndices = new[] { Array.IndexOf(headers, "GPU"), Array.IndexOf(headers, "GPU0") };
                var cpuIndex = Array.IndexOf(headers, "CPU");

                var sampleRows = new List<string[]>();
                double? lastTime = null;

                while (await csv.ReadAsync().ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = RowFields(csv);
                    if (sampleRows.Count < 10)
                    {
                        sampleRows.Add(row);
                    }

                    if (timeIndex >= 0)
                    {
                        var rawTime = timeIndex < row.Length ? row[timeIndex] : string.Empty;
                        if (!CsvValues.IsNa(rawTime) && CsvValues.TryParseNumber(rawTime, out var value))
                        {
                            lastTime = value;
                        }
                    }
                }

                if (sampleRows.Count == 0)
                {
                    return null;
                }

                var application = FirstNonEmpty(sampleRows, applicationIndex) ?? "--";
                return new CaptureInfo(
                    Path: fullPath,
                    Name: CaptureFileNaming.SanitizeDisplayName(Path.GetFileName(fullPath)),
                    Application: DisplayText.CleanGameName(application),
                    Resolution: FirstNonEmpty(sampleRows, resolutionIndex) ?? "--",
                    Gpu: FirstNonEmpty(sampleRows, gpuIndices) ?? "--",
                    Cpu: FirstNonEmpty(sampleRows, cpuIndex) ?? "--",
                    DurationSeconds: lastTime);
            }
            catch (DecoderFallbackException)
            {
                // Try the next encoding.
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static string[] RowFields(CsvReader csv)
    {
        var count = csv.ColumnCount;
        var fields = new string[count];
        for (var i = 0; i < count; i++)
        {
            fields[i] = csv.GetField(i) ?? string.Empty;
        }

        return fields;
    }

    private static string? FirstNonEmpty(List<string[]> rows, params int[] indices)
    {
        foreach (var row in rows)
        {
            foreach (var index in indices)
            {
                if (index < 0 || index >= row.Length)
                {
                    continue;
                }

                var value = row[index];
                if (!CsvValues.IsMissing(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static CaptureData EmptyCapture(string fullPath, string fileName, IReadOnlyList<string> headers) =>
        new()
        {
            Path = fullPath,
            DisplayName = CaptureFileNaming.SanitizeDisplayName(fileName),
            Kind = DetectKindCore(headers, fileName),
            Headers = headers,
            Columns = Array.Empty<string[]>(),
        };

    private static CsvKind DetectKindCore(IReadOnlyList<string> headers, string fileName)
    {
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            normalized.Add(header.Trim());
        }

        if (normalized.Contains("TimeInSeconds")
            || normalized.Contains("MsBetweenPresents")
            || CaptureSourceDetector.IsNvidiaAppPerformanceLog(headers))
        {
            return CsvKind.Log;
        }

        if (normalized.Contains("Avg FPS") && normalized.Contains("Log Name"))
        {
            return CsvKind.Summary;
        }

        if (!string.IsNullOrEmpty(fileName)
            && fileName.Contains("summary", StringComparison.OrdinalIgnoreCase))
        {
            return CsvKind.Summary;
        }

        return CsvKind.Unknown;
    }
}
