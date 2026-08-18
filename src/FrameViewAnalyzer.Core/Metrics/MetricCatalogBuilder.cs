using System.Text;
using System.Text.RegularExpressions;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Core.Metrics;

/// <summary>
/// Builds the metric catalog for one capture: core definitions that resolve
/// to numeric columns, plus dynamically discovered metrics for remaining
/// unknown numeric columns with stable, collision-resistant IDs.
/// </summary>
public static partial class MetricCatalogBuilder
{
    [GeneratedRegex(@"[^a-zA-Z0-9]+")]
    private static partial Regex NonAlphanumeric();

    public static IReadOnlyList<MetricDefinition> Build(CaptureData capture)
    {
        if (capture.Kind != CsvKind.Log)
        {
            return [];
        }

        var catalog = new List<MetricDefinition>();
        var usedColumns = new HashSet<string>(StringComparer.Ordinal);
        var isNvidiaAppLog = CaptureSourceDetector.IsNvidiaAppPerformanceLog(capture);

        foreach (var metric in CoreMetricCatalog.CoreMetrics)
        {
            if (metric.Computed)
            {
                catalog.Add(metric);
                continue;
            }

            var column = metric.ResolveColumn(capture.Headers);
            if (column is not null
                && ColumnInspector.IsNumericColumn(capture, capture.IndexOfHeader(column)))
            {
                catalog.Add(metric);
                usedColumns.Add(column);
            }
        }

        foreach (var header in capture.Headers)
        {
            if (CoreMetricCatalog.SkipColumns.Contains(header)
                || CoreMetricCatalog.TimeColumnKeys.Contains(header)
                || usedColumns.Contains(header)
                || (isNvidiaAppLog && (header == "PID" || header == "FPS")))
            {
                continue;
            }

            if (!ColumnInspector.IsNumericColumn(capture, capture.IndexOfHeader(header)))
            {
                continue;
            }

            catalog.Add(new MetricDefinition(
                Id: ColumnMetricId(header),
                Label: header,
                Unit: isNvidiaAppLog ? GuessNvidiaUnit(header) : GuessUnit(header),
                Category: isNvidiaAppLog ? GuessNvidiaCategory(header) : GuessCategory(header),
                ColumnKeys: [header],
                Direction: isNvidiaAppLog ? GuessNvidiaDirection(header) : MetricDirection.Undefined));
        }

        return catalog;
    }

    /// <summary>
    /// Stable ID for a dynamic column. Distinct FrameView headers can
    /// normalize to the same slug ("Metric A" vs "Metric-A"); a 32-bit
    /// digest suffix keeps IDs unique while preserving the same ID across
    /// comparison sessions. (The Python reference uses BLAKE2s-4 for the
    /// digest; FNV-1a is used here because .NET has no BLAKE2s and the IDs
    /// only need to be stable within this application.)
    /// </summary>
    public static string ColumnMetricId(string column)
    {
        var slug = NonAlphanumeric().Replace(column, "_").Trim('_').ToLowerInvariant();
        if (slug.Length == 0)
        {
            slug = "metric";
        }

        var digest = Fnv1a32(Encoding.UTF8.GetBytes(column));
        var maxSlugLength = 64 - "col__".Length - 8;
        if (slug.Length > maxSlugLength)
        {
            slug = slug[..maxSlugLength];
        }

        return $"col_{slug}_{digest:x8}";
    }

    // Keep the established FrameView heuristics unchanged so existing
    // dynamic-column parity and stable display behavior are unaffected.
    public static string GuessUnit(string column)
    {
        if (Regex.IsMatch(column, @"\(%\)|Util%", RegexOptions.IgnoreCase))
        {
            return "%";
        }

        if (Regex.IsMatch(column, @"\(MHz\)|Clk", RegexOptions.IgnoreCase))
        {
            return "MHz";
        }

        if (Regex.IsMatch(column, @"Temp|\(C\)|celsius", RegexOptions.IgnoreCase))
        {
            return "°C";
        }

        if (Regex.IsMatch(column, @"\(W\)|Power|Watts|Pwr", RegexOptions.IgnoreCase))
        {
            return "W";
        }

        if (Regex.IsMatch(column, @"\(ms\)|^Ms"))
        {
            return "ms";
        }

        if (column.Contains("F/J", StringComparison.OrdinalIgnoreCase))
        {
            return "F/J";
        }

        if (column.Contains("Wh", StringComparison.OrdinalIgnoreCase))
        {
            return "Wh";
        }

        return string.Empty;
    }

    public static string GuessCategory(string column)
    {
        var upper = column.ToUpperInvariant();
        if (upper.Contains("CPU"))
        {
            return "CPU";
        }

        if (upper.Contains("GPU") || upper.Contains("NV") || upper.Contains("PCAT") || upper.Contains("PERF/W"))
        {
            return upper.Contains("PWR") || upper.Contains("POWER") || upper.Contains("PERF")
                ? "Power"
                : "GPU";
        }

        if (upper.Contains("BATTERY"))
        {
            return "Power";
        }

        if (upper.Contains("LATENCY") || column.StartsWith("Ms", StringComparison.Ordinal))
        {
            return "Latency";
        }

        return "Other";
    }

    private static string GuessNvidiaUnit(string column)
    {
        if (column.StartsWith("FPS", StringComparison.OrdinalIgnoreCase))
        {
            return "FPS";
        }

        if (Regex.IsMatch(column, @"Milli\s*Volts|\bmV\b", RegexOptions.IgnoreCase))
        {
            return "mV";
        }

        if (Regex.IsMatch(column, @"\bRPM\b", RegexOptions.IgnoreCase))
        {
            return "RPM";
        }

        if (Regex.IsMatch(column, @"\(msec\)", RegexOptions.IgnoreCase))
        {
            return "ms";
        }

        if (column.Contains("Frequency", StringComparison.OrdinalIgnoreCase))
        {
            return "MHz";
        }

        return GuessUnit(column);
    }

    private static string GuessNvidiaCategory(string column)
    {
        if (column.StartsWith("FPS", StringComparison.OrdinalIgnoreCase))
        {
            return "Performance";
        }

        return GuessCategory(column);
    }

    private static MetricDirection GuessNvidiaDirection(string column)
    {
        var upper = column.ToUpperInvariant();
        if (upper.StartsWith("FPS", StringComparison.Ordinal))
        {
            return MetricDirection.HigherIsBetter;
        }

        if (upper.Contains("LATENCY")
            || upper.Contains("TEMP")
            || upper.Contains("POWER")
            || upper.Contains("PWR"))
        {
            return MetricDirection.LowerIsBetter;
        }

        return MetricDirection.Undefined;
    }

    private static uint Fnv1a32(byte[] bytes)
    {
        var hash = 2166136261u;
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= 16777619u;
        }

        return hash;
    }
}
