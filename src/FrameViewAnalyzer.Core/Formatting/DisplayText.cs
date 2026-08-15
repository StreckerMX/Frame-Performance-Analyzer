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

        var total = (long)Math.Round(seconds.Value, MidpointRounding.ToEven);
        var minutes = total / 60;
        var secs = total % 60;
        return minutes == 0 ? $"{secs} s" : $"{minutes} min {secs} s";
    }
}
