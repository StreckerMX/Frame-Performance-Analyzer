using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Infrastructure.Csv;

/// <summary>
/// Reads NVIDIA FrameView CSV files. Encodings are tried in order
/// (UTF-8 with BOM handling, Windows-1252, ISO-8859-1) exactly like the
/// Python reference; exotic bytes must never crash the loader.
/// </summary>
public interface IFrameViewCsvReader
{
    /// <summary>Classifies a file from its headers and (optionally) its name.</summary>
    CsvKind DetectKind(IReadOnlyList<string> headers, string fileName);

    /// <summary>Full column-major parse of a capture file.</summary>
    Task<CaptureData> LoadCaptureAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Light single-pass scan: headers, up to 10 sample rows, and the last
    /// recorded second. Returns null for summary/unrecognized files.
    /// </summary>
    Task<CaptureInfo?> ReadCaptureInfoAsync(string path, CancellationToken cancellationToken = default);
}
