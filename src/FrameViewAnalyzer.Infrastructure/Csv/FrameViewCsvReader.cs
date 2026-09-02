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
    private const int StreamBufferSize = 1 << 18;
    private const int CaptureInfoSampleRows = 10;
    private const int CaptureInfoTailBytes = 1 << 18;

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

    // Capture-folder, Library, and Multi-selection views repeatedly request
    // the same lightweight metadata. Cache it by immutable file stamp so those
    // views never rescan a long CSV unless the file actually changed.
    private readonly object _captureInfoCacheGate = new();
    private readonly Dictionary<string, CaptureInfoCacheEntry> _captureInfoCache =
        new(StringComparer.OrdinalIgnoreCase);

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
                    bufferSize: StreamBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var text = new StreamReader(
                    stream,
                    encoding,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: StreamBufferSize);
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
        var stamp = TryGetFileStamp(fullPath);
        if (stamp is null)
        {
            return null;
        }

        lock (_captureInfoCacheGate)
        {
            if (_captureInfoCache.TryGetValue(fullPath, out var cached)
                && cached.Length == stamp.Value.Length
                && cached.LastWriteTicks == stamp.Value.LastWriteTicks)
            {
                return cached.Info;
            }
        }

        var info = await ReadCaptureInfoCoreAsync(fullPath, cancellationToken).ConfigureAwait(false);
        lock (_captureInfoCacheGate)
        {
            _captureInfoCache[fullPath] = new CaptureInfoCacheEntry(
                stamp.Value.Length,
                stamp.Value.LastWriteTicks,
                info);
        }

        return info;
    }

    private static async Task<CaptureInfo?> ReadCaptureInfoCoreAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        foreach (var encoding in Encodings)
        {
            try
            {
                using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: StreamBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var text = new StreamReader(
                    stream,
                    encoding,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: StreamBufferSize);
                using var csv = new CsvReader(text, RawConfiguration);

                if (!await csv.ReadAsync().ConfigureAwait(false))
                {
                    return null;
                }

                var headers = RowFields(csv);
                var frameViewTimeIndex = Array.IndexOf(headers, "TimeInSeconds");
                var nvidiaAppTimeIndex = Array.IndexOf(
                    headers,
                    CaptureSourceDetector.NvidiaAppTimeHeader);

                var timeIndex = frameViewTimeIndex >= 0
                    ? frameViewTimeIndex
                    : nvidiaAppTimeIndex;

                var presentsIndex = Array.IndexOf(headers, "MsBetweenPresents");
                if (timeIndex < 0 && presentsIndex < 0)
                {
                    return null;
                }

                var applicationIndex = Array.IndexOf(headers, "Application");
                var resolutionIndex = Array.IndexOf(headers, "Resolution");
                var gpuIndices = new[] { Array.IndexOf(headers, "GPU"), Array.IndexOf(headers, "GPU0") };
                var cpuIndex = Array.IndexOf(headers, "CPU");

                var sampleRows = new List<string[]>(CaptureInfoSampleRows);
                while (sampleRows.Count < CaptureInfoSampleRows
                       && await csv.ReadAsync().ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sampleRows.Add(RowFields(csv));
                }

                if (sampleRows.Count == 0)
                {
                    return null;
                }

                // Duration is the span of the capture timeline, not the absolute final
                // TimeInSeconds value. FrameView timestamps can begin well above zero when
                // recording starts after the monitored process has already been running.
                //
                // The first timestamp is already available from the small metadata sample at
                // the beginning of the file, while the last timestamp comes from the existing
                // tail read. This preserves the lightweight metadata path for long captures.
                var firstTime = frameViewTimeIndex >= 0
                    ? FirstNumericValue(sampleRows, frameViewTimeIndex)
                    : null;

                var lastTime = timeIndex >= 0
                    ? await ReadLastTimeFromTailAsync(
                        fullPath,
                        encoding,
                        timeIndex,
                        cancellationToken).ConfigureAwait(false)
                    : null;

                double? durationSeconds = null;

                if (lastTime.HasValue && double.IsFinite(lastTime.Value))
                {
                    if (frameViewTimeIndex >= 0)
                    {
                        // FrameView TimeInSeconds follows the monitored process timeline and
                        // may therefore begin well above zero. Capture duration is the span
                        // between the first and final recorded timestamps.
                        if (firstTime.HasValue
                            && double.IsFinite(firstTime.Value)
                            && lastTime.Value >= firstTime.Value)
                        {
                            durationSeconds = lastTime.Value - firstTime.Value;
                        }
                    }
                    else if (nvidiaAppTimeIndex >= 0 && lastTime.Value >= 0)
                    {
                        // NVIDIA App explicitly reports elapsed capture time. Its final
                        // timestamp already represents the capture duration even when the
                        // first sampled row occurs after zero.
                        durationSeconds = lastTime.Value;
                    }
                }
                var application = FirstNonEmpty(sampleRows, applicationIndex) ?? "--";
                return new CaptureInfo(
                    Path: fullPath,
                    Name: CaptureFileNaming.SanitizeDisplayName(Path.GetFileName(fullPath)),
                    Application: DisplayText.CleanGameName(application),
                    Resolution: FirstNonEmpty(sampleRows, resolutionIndex) ?? "--",
                    Gpu: FirstNonEmpty(sampleRows, gpuIndices) ?? "--",
                    Cpu: FirstNonEmpty(sampleRows, cpuIndex) ?? "--",
                    DurationSeconds: durationSeconds);
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

    private static async Task<double?> ReadLastTimeFromTailAsync(
        string path,
        Encoding encoding,
        int timeIndex,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: StreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length == 0)
        {
            return null;
        }

        var start = Math.Max(0, stream.Length - CaptureInfoTailBytes);
        stream.Seek(start, SeekOrigin.Begin);

        // A tail seek may begin in the middle of a UTF-8 character. A cloned
        // replacement-fallback decoder is used only for this tail window; the
        // normal strict-decoder pass above still validates the actual file.
        var tailEncoding = (Encoding)encoding.Clone();
        tailEncoding.DecoderFallback = DecoderFallback.ReplacementFallback;
        using var text = new StreamReader(
            stream,
            tailEncoding,
            detectEncodingFromByteOrderMarks: start == 0,
            bufferSize: StreamBufferSize);

        if (start > 0)
        {
            _ = await text.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }

        var lines = new List<string>();
        string? line;
        while ((line = await text.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        for (var index = lines.Count - 1; index >= 0; index--)
        {
            using var lineReader = new StringReader(lines[index]);
            using var csv = new CsvReader(lineReader, RawConfiguration);
            if (!csv.Read())
            {
                continue;
            }

            var fields = RowFields(csv);
            if (timeIndex >= fields.Length)
            {
                continue;
            }

            var rawTime = fields[timeIndex];
            if (!CsvValues.IsNa(rawTime) && CsvValues.TryParseNumber(rawTime, out var value))
            {
                return value;
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

    private static double? FirstNumericValue(
        IReadOnlyList<string[]> rows,
        int index)
    {
        if (index < 0)
        {
            return null;
        }

        foreach (var row in rows)
        {
            if (index >= row.Length)
            {
                continue;
            }

            var raw = row[index];
            if (CsvValues.IsNa(raw)
                || !CsvValues.TryParseNumber(raw, out var value)
                || !double.IsFinite(value))
            {
                continue;
            }

            return value;
        }

        return null;
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

    private static (long Length, long LastWriteTicks)? TryGetFileStamp(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? (file.Length, file.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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

    private sealed record CaptureInfoCacheEntry(
        long Length,
        long LastWriteTicks,
        CaptureInfo? Info);
}
