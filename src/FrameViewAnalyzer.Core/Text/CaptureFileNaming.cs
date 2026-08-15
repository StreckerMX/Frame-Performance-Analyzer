using System.Globalization;
using System.Text.RegularExpressions;

namespace FrameViewAnalyzer.Core.Text;

/// <summary>
/// Pure helpers for FrameView file names: display-name sanitization and the
/// capture timestamp embedded in names like "…_2026_08_13T033633_Log.csv".
/// </summary>
public static partial class CaptureFileNaming
{
    public const string LogSuffix = "_Log.csv";

    [GeneratedRegex(@"^FrameView_")]
    private static partial Regex FrameViewPrefix();

    [GeneratedRegex(@"_Log$")]
    private static partial Regex LogSuffixTail();

    [GeneratedRegex(@"(\d{4})_(\d{2})_(\d{2})T(\d{2})(\d{2})(\d{2})")]
    private static partial Regex StampPattern();

    /// <summary>
    /// Removes the "FrameView_" prefix and "_Log" suffix; names longer than
    /// 40 characters are ellipsized to exactly 40.
    /// </summary>
    public static string SanitizeDisplayName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        name = FrameViewPrefix().Replace(name, string.Empty);
        name = LogSuffixTail().Replace(name, string.Empty);
        return name.Length > 40 ? name[..39] + "…" : name;
    }

    /// <summary>
    /// Extracts the capture timestamp embedded in a FrameView file name.
    /// Returns false when the pattern is absent.
    /// </summary>
    public static bool TryParseCaptureStamp(string fileName, out DateTime stamp)
    {
        var match = StampPattern().Match(Path.GetFileName(fileName));
        if (!match.Success)
        {
            stamp = default;
            return false;
        }

        stamp = new DateTime(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture));
        return true;
    }

    public static string FormatStamp(DateTime stamp) =>
        stamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
