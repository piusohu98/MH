using MH.Core.Models;

namespace MH.Core.Contracts;

public sealed record MarketEventDto(
    string Id,
    string ServerId,
    string? ItemId,
    MarketEventType Type,
    string Label,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    CatalogKind CatalogKind);

public sealed record EventImpactResponse(
    MarketEventDto Event,
    DateTimeOffset AsOfUtc,
    int WindowDays,
    EventImpactPhaseResult Before,
    EventImpactPhaseResult During,
    EventImpactPhaseResult After);

public sealed record EventPatternMetricSummary(
    bool Available,
    int ComparableEventCount,
    decimal? MedianChange,
    int IncreaseCount,
    int DecreaseCount,
    int StableCount,
    decimal? DirectionConsistency,
    string? UnavailableReason);

public sealed record EventPatternSummaryResponse(
    string ServerId,
    string ItemId,
    MarketEventType EventType,
    DateTimeOffset AsOfUtc,
    int WindowDays,
    int HistoryDays,
    int MaxEvents,
    string StatisticsVersion,
    decimal NeutralThreshold,
    int SampleEventCount,
    DateTimeOffset InputStartUtc,
    DateTimeOffset InputEndUtc,
    EventPatternMetricSummary DuringPrice,
    EventPatternMetricSummary AfterPrice,
    EventPatternMetricSummary DuringVisibleSupply,
    EventPatternMetricSummary AfterVisibleSupply);
