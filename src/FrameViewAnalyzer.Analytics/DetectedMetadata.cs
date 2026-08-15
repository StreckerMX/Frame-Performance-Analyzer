using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core.Formatting;
using FrameViewAnalyzer.Core.Text;

namespace FrameViewAnalyzer.Analytics;

/// <summary>
/// Context values already known for one capture, used to prefill the manual
/// metadata editor so users never retype what FrameView recorded: game,
/// resolution, driver version, and the detected upscaler / frame generation
/// / ray-reconstruction technologies. Mirrors the Python reference's
/// detected_field_values exactly; fields without a source are omitted.
/// </summary>
public static class DetectedMetadata
{
    private static readonly HashSet<string> DisabledValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "disabled",
        "not recorded",
    };

    private static readonly HashSet<string> InvalidQualityModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "0",
        "off",
        "disabled",
        "none",
        "n/a",
    };

    public static IReadOnlyDictionary<string, string> DetectFieldValues(SessionAnalysis session)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        var application = session.Metadata?.Application ?? string.Empty;
        var gameSource = string.IsNullOrWhiteSpace(application) || application == "--"
            ? session.Capture.DisplayName
            : application;
        values["game"] = DisplayText.CleanGameName(gameSource);

        var resolution = session.Metadata?.Resolution ?? string.Empty;
        if (resolution.Length > 0 && resolution != "--")
        {
            values["resolution"] = resolution;
        }

        var driver = FirstValue(session, "GPU Base Driver", "GPU Driver Package");
        if (driver is not null)
        {
            values["driver_version"] = driver;
        }

        var dlss = TechnologyValue(FirstValue(session, "DLSS", "DLSS Mode", "DLSS Quality", "DLSS Preset"));
        if (dlss.Available && !DisabledValues.Contains(dlss.Value))
        {
            values["upscaler"] = "DLSS";
            var mode = FirstValue(session, "DLSS Mode", "DLSS Quality", "DLSS Preset");
            if (mode is not null && !InvalidQualityModes.Contains(mode))
            {
                values["upscaler_quality"] = mode;
            }
        }

        var frameGeneration = FrameGenerationValue(session);
        if (frameGeneration is not null
            && !DisabledValues.Contains(frameGeneration))
        {
            values["frame_generation"] = frameGeneration;
        }

        var rayReconstruction = TechnologyValue(
            FirstValue(session, "Ray Reconstruction", "RayReconstruction", "DLSS Ray Reconstruction"));
        if (rayReconstruction.Available && !DisabledValues.Contains(rayReconstruction.Value))
        {
            values["ray_tracing"] = "Ray Reconstruction";
        }

        return values;
    }

    private readonly record struct TechnologyBadge(string Value, bool Available);

    /// <summary>First non-empty recorded value across the candidate headers.</summary>
    private static string? FirstValue(SessionAnalysis session, params string[] headers)
    {
        var columns = headers
            .Select(header => session.Capture.IndexOfHeader(header))
            .ToArray();
        for (var row = 0; row < session.Capture.RowCount; row++)
        {
            foreach (var column in columns)
            {
                if (column < 0)
                {
                    continue;
                }

                var raw = session.Capture.Cell(column, row).Trim();
                if (!CsvValues.IsMissing(raw))
                {
                    return raw;
                }
            }
        }

        return null;
    }

    private static TechnologyBadge TechnologyValue(string? value)
    {
        if (value is null)
        {
            return new TechnologyBadge("Not recorded", Available: false);
        }

        var normalized = value.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "0" or "off" or "false" or "disabled" or "disable" or "no"
                => new TechnologyBadge("Disabled", Available: true),
            "1" or "on" or "true" or "enabled" or "enable" or "yes"
                => new TechnologyBadge("Enabled", Available: true),
            _ => new TechnologyBadge(normalized, Available: true),
        };
    }

    /// <summary>
    /// Frame Generation value from the trimmed frame-gen multiplier series
    /// (falling back to the raw column): "x2", "Disabled", or null when the
    /// capture records nothing about frame generation.
    /// </summary>
    private static string? FrameGenerationValue(SessionAnalysis session)
    {
        var values = SeriesBuilder.Build(session, "fg_multiplier").Y;
        if (values.Length == 0)
        {
            var raw = FirstValue(session, "Frame Gen Multiplier");
            if (raw is null)
            {
                return null;
            }

            if (!double.TryParse(
                    raw,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return raw;
            }

            values = [parsed];
        }

        var multiplier = values.Max();
        if (multiplier <= 1.0)
        {
            return "Disabled";
        }

        var rounded = (int)System.Math.Round(multiplier, System.MidpointRounding.ToEven);
        return $"x{rounded}";
    }
}
