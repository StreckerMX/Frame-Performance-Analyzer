namespace FrameViewAnalyzer.Core.Models;

/// <summary>
/// Optional human-readable benchmark context for one capture. Stored by the
/// application; the FrameView CSV files are never modified. Mirrors the
/// Python reference's ManualMetadata dataclass.
/// </summary>
public sealed record ManualMetadata(
    string BenchmarkName = "",
    string Game = "",
    string Resolution = "",
    string GraphicsPreset = "",
    string Upscaler = "",
    string UpscalerQuality = "",
    string FrameGeneration = "",
    string RayTracing = "",
    string DriverVersion = "",
    string Notes = "",
    IReadOnlyList<string>? Tags = null)
{
    public IReadOnlyList<string> Tags { get; init; } = Tags ?? [];

    public bool IsEmpty => !StringFields.Any(value => value.Length > 0) && Tags.Count == 0;

    /// <summary>
    /// Compact one-line configuration summary for the session card, e.g.
    /// "4K · Very High · DLSS Quality · FG x2 · RT". Null when no manual
    /// configuration fields are set.
    /// </summary>
    public string? ConfigLine
    {
        get
        {
            var parts = new List<string>();
            foreach (var value in new[] { Resolution, GraphicsPreset })
            {
                if (value.Length > 0)
                {
                    parts.Add(value);
                }
            }

            var upscaler = Upscaler;
            if (upscaler.Length > 0)
            {
                if (UpscalerQuality.Length > 0)
                {
                    upscaler = $"{upscaler} {UpscalerQuality}";
                }

                parts.Add(upscaler);
            }

            if (FrameGeneration.Length > 0)
            {
                parts.Add(FrameGeneration);
            }

            if (RayTracing.Length > 0)
            {
                parts.Add(RayTracing);
            }

            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
    }

    private IEnumerable<string> StringFields =>
    [
        BenchmarkName,
        Game,
        Resolution,
        GraphicsPreset,
        Upscaler,
        UpscalerQuality,
        FrameGeneration,
        RayTracing,
        DriverVersion,
        Notes,
    ];

    /// <summary>Value equality: string fields plus tag sequences, not references.</summary>
    public bool Equals(ManualMetadata? other) =>
        other is not null
        && BenchmarkName == other.BenchmarkName
        && Game == other.Game
        && Resolution == other.Resolution
        && GraphicsPreset == other.GraphicsPreset
        && Upscaler == other.Upscaler
        && UpscalerQuality == other.UpscalerQuality
        && FrameGeneration == other.FrameGeneration
        && RayTracing == other.RayTracing
        && DriverVersion == other.DriverVersion
        && Notes == other.Notes
        && Tags.SequenceEqual(other.Tags);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BenchmarkName);
        hash.Add(Game);
        hash.Add(Resolution);
        hash.Add(GraphicsPreset);
        hash.Add(Upscaler);
        hash.Add(UpscalerQuality);
        hash.Add(FrameGeneration);
        hash.Add(RayTracing);
        hash.Add(DriverVersion);
        hash.Add(Notes);
        foreach (var tag in Tags)
        {
            hash.Add(tag);
        }

        return hash.ToHashCode();
    }
}
