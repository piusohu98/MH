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
