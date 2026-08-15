using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Analytics.Comparison;

/// <summary>One statistic row of a Base/Comparison comparison.</summary>
public sealed record ComparisonRow(
    string MetricId,
    string MetricLabel,
    string Category,
    string Unit,
    string StatisticKey,
    string StatisticLabel,
    string BaseSession,
    double? BaseValue,
    string ComparisonSession,
    double? ComparisonValue,
    double? Delta,
    double? DeltaPercent,
    ImprovementKind Kind);

/// <summary>Builds per-statistic comparison rows for two sessions.</summary>
public interface IComparisonService
{
    IReadOnlyList<ComparisonRow> Compare(
        SessionAnalysis baseSession,
        SessionAnalysis? comparisonSession = null);
}
