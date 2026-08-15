using System.Text.RegularExpressions;

namespace FrameViewAnalyzer.Core.Formatting;

/// <summary>
/// Display-only text transformations shared by the dashboard and library:
/// game-name cleanup, hardware-name compaction, and capture durations.
/// </summary>
public static partial class DisplayText
{
    [GeneratedRegex(@"\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex ExeSuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"\s+\d+\s*-\s*Core\b")]
    private static partial Regex CoreCountSuffix();

    /// <summary>"GTA5_Enhanced.exe" → "GTA5 Enhanced"; empty → fallback name.</summary>
    public static string CleanGameName(string application)
    {
        var name = application.Trim();
        if (name.Length == 0)
        {
            return "Unnamed benchmark";
        }

        name = ExeSuffix().Replace(name, string.Empty);
        name = name.Replace("_", " ", StringComparison.Ordinal);
        return WhitespaceRun().Replace(name, " ").Trim();
    }

    /// <summary>Shortens GPU/CPU names so they fit compact labels.</summary>
    public static string CompactHardware(string value)
    {
        var text = value.Trim();
        text = text.Replace("NVIDIA GeForce ", string.Empty, StringComparison.Ordinal);
        text = text.Replace("AMD Radeon ", string.Empty, StringComparison.Ordinal);
        text = text.Replace("AMD Ryzen ", "Ryzen ", StringComparison.Ordinal);
        text = CoreCountSuffix().Replace(text, string.Empty);
        text = text.Replace(" Processor", string.Empty, StringComparison.Ordinal);
        return WhitespaceRun().Replace(text, " ").Trim();
    }

    /// <summary>Compact duration ("45 s", "5 min 37 s"); invalid → "--".</summary>
    public static string FormatDuration(double? seconds)
    {
        if (seconds is null || !double.IsFinite(seconds.Value) || seconds.Value <= 0)
        {
            return "--";
        }

        var total = (long)System.Math.Round(seconds.Value, MidpointRounding.ToEven);
        var minutes = total / 60;
        var secs = total % 60;
        return minutes == 0 ? $"{secs} s" : $"{minutes} min {secs} s";
    }

    /// <summary>
    /// Human-readable duration with hours/minutes/seconds and no zero units:
    /// 45 → "45 s", 300 → "5 min", 4385 → "1 h 13 min 5 s". Non-finite or
    /// negative input renders as "0 s".
    /// </summary>
    public static string FormatDurationHuman(double seconds)
    {
        var rounded = double.IsFinite(seconds)
            ? (long)System.Math.Round(seconds, MidpointRounding.ToEven)
            : 0;
        var total = System.Math.Max(0, rounded);

        var hours = total / 3600;
        var remainder = total % 3600;
        var minutes = remainder / 60;
        var secs = remainder % 60;

        var parts = new List<string>();
        if (hours > 0)
        {
            parts.Add($"{hours.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} h");
        }

        if (minutes > 0)
        {
            parts.Add($"{minutes} min");
        }

        if (secs > 0 || parts.Count == 0)
        {
            parts.Add($"{secs} s");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Statistic value formatting like the Python reference: missing → "--",
    /// ≥1000 no decimals, ≥100 no decimals, otherwise one decimal.
    /// </summary>
    public static string FormatStat(double? value, string unit = "")
    {
        if (value is null)
        {
            return "--";
        }

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var suffix = unit.Length > 0 ? $" {unit}" : string.Empty;
        var absolute = System.Math.Abs(value.Value);
        if (absolute >= 1000)
        {
            return $"{value.Value.ToString("N0", culture)}{suffix}";
        }

        if (absolute >= 100)
        {
            return $"{value.Value.ToString("F0", culture)}{suffix}";
        }

        return $"{value.Value.ToString("F1", culture)}{suffix}";
    }
}
