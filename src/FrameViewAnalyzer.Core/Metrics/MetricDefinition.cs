using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Core.Metrics;

/// <summary>
/// One metric in the FrameView catalog. Core definitions carry column-key
/// aliases because FrameView header names vary between versions; dynamic
/// metrics discovered from unknown numeric columns use a single key.
/// </summary>
public sealed record MetricDefinition(
    string Id,
    string Label,
    string Unit,
    string Category,
    IReadOnlyList<string> ColumnKeys,
    MetricDirection Direction,
    bool Computed = false)
{
    /// <summary>First column key present in the capture headers, or null.</summary>
    public string? ResolveColumn(IReadOnlyList<string> headers)
    {
        var headerSet = new HashSet<string>(headers, StringComparer.Ordinal);
        foreach (var key in ColumnKeys)
        {
            if (headerSet.Contains(key))
            {
                return key;
            }
        }

        return null;
    }
}
