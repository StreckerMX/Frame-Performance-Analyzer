using System.Globalization;

namespace FrameViewAnalyzer.Core.Text;

/// <summary>
/// Cell-level parsing rules shared by every reader. Mirrors the Python
/// reference: an exact NA set, a broader case-insensitive missing check for
/// metadata columns, and locale-tolerant numeric parsing that accepts
/// decimal commas and rejects non-finite values.
/// </summary>
public static class CsvValues
{
    public static readonly IReadOnlySet<string> NaValues = new HashSet<string>(StringComparer.Ordinal)
    {
        string.Empty,
        "NA",
        "N/A",
        "n/a",
        "null",
        "NULL",
    };

    private static readonly HashSet<string> MissingUpper = new(StringComparer.Ordinal)
    {
        "NA",
        "N/A",
        "NULL",
    };

    /// <summary>Exact match against the NA set (already-trimmed cells).</summary>
    public static bool IsNa(string? value) => value is not null && NaValues.Contains(value);

    /// <summary>
    /// Lenient missing check used for capture metadata: trimmed, exact NA
    /// match, or a case-insensitive "NA"/"N/A"/"NULL" marker.
    /// </summary>
    public static bool IsMissing(string? raw)
    {
        if (raw is null)
        {
            return true;
        }

        var value = raw.Trim();
        return NaValues.Contains(value) || MissingUpper.Contains(value.ToUpperInvariant());
    }

    /// <summary>
    /// Parses a numeric cell like Python's float(): accepts decimal commas
    /// and the "inf"/"infinity"/"nan" tokens (which may be non-finite).
    /// </summary>
    public static bool TryParseAnyNumber(string? raw, out double value)
    {
        value = 0.0;
        if (raw is null || NaValues.Contains(raw))
        {
            return false;
        }

        var normalized = raw.Replace(',', '.');
        // Fast path: numeric cells (the overwhelmingly common case) parse
        // directly. NumberStyles.Float accepts the inf/nan tokens the Python
        // float() would accept, so the fallback switch only runs for
        // genuinely non-numeric cells and allocates no strings here.
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        switch (normalized.Trim().ToLowerInvariant())
        {
            case "inf":
            case "+inf":
            case "infinity":
            case "+infinity":
                value = double.PositiveInfinity;
                return true;
            case "-inf":
            case "-infinity":
                value = double.NegativeInfinity;
                return true;
            case "nan":
            case "+nan":
            case "-nan":
                value = double.NaN;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Parses a numeric cell. Accepts decimal commas; rejects NA values and
    /// non-finite results exactly like the Python reference.
    /// </summary>
    public static bool TryParseNumber(string? raw, out double value) =>
        TryParseAnyNumber(raw, out value) && double.IsFinite(value);
}
