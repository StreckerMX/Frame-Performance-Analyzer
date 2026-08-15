namespace FrameViewAnalyzer.Core.Models;

/// <summary>
/// One indexed capture in the Benchmark Library: detected context plus
/// availability state, keyed by the stable capture identity. No CSV data is
/// duplicated. Mirrors the Python reference's LibraryRecord.
/// </summary>
public sealed record LibraryRecord(
    string Identity,
    string SourcePath,
    string SourceName,
    string Game,
    string Resolution,
    string Gpu,
    string Cpu,
    double? DurationSeconds,
    string AddedAt,
    string LastSeenAt,
    bool Available = true,
    IReadOnlyDictionary<string, double>? StatsSummary = null)
{
    private static readonly IReadOnlyDictionary<string, double> EmptyStats =
        new Dictionary<string, double>();

    public IReadOnlyDictionary<string, double> StatsSummary { get; init; } =
        StatsSummary ?? EmptyStats;

    /// <summary>Value equality: scalar fields plus ordered stats pairs.</summary>
    public bool Equals(LibraryRecord? other) =>
        other is not null
        && Identity == other.Identity
        && SourcePath == other.SourcePath
        && SourceName == other.SourceName
        && Game == other.Game
        && Resolution == other.Resolution
        && Gpu == other.Gpu
        && Cpu == other.Cpu
        && DurationSeconds == other.DurationSeconds
        && AddedAt == other.AddedAt
        && LastSeenAt == other.LastSeenAt
        && Available == other.Available
        && StatsSummary.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SequenceEqual(other.StatsSummary.OrderBy(pair => pair.Key, StringComparer.Ordinal));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Identity);
        hash.Add(SourcePath);
        hash.Add(SourceName);
        hash.Add(Game);
        hash.Add(Resolution);
        hash.Add(Gpu);
        hash.Add(Cpu);
        hash.Add(DurationSeconds);
        hash.Add(AddedAt);
        hash.Add(LastSeenAt);
        hash.Add(Available);
        foreach (var (key, value) in StatsSummary.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(key);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
