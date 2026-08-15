namespace FrameViewAnalyzer.Infrastructure.Csv;

/// <summary>
/// Classification of a FrameView CSV file: detailed per-frame log,
/// FrameView_Summary.csv aggregate table, or unrecognized content.
/// </summary>
public enum CsvKind
{
    Unknown,
    Log,
    Summary,
}
