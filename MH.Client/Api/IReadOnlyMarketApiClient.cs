using MH.Core;
using MH.Core.Contracts;
using MH.Core.Models;

namespace MH.Client.Api;

public interface IReadOnlyMarketApiClient
{
    Task<CatalogResponse> GetCatalogAsync(
        CatalogKind catalogKind = CatalogKind.Demo,
        CancellationToken cancellationToken = default);

    Task<MarketSeriesResponse> GetSeriesAsync(
        string serverId,
        string itemId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default);

    Task<MarketIndicatorsResponse> GetIndicatorsAsync(
        string serverId,
        string itemId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default);

    Task<RecommendationPreviewResponse> GetRecommendationAsync(
        string serverId,
        string itemId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketEventDto>> GetEventsAsync(
        string serverId,
        string itemId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        MarketEventType? type = null,
        CancellationToken cancellationToken = default);

    Task<EventImpactResponse> GetEventImpactAsync(
        string serverId,
        string itemId,
        string eventId,
        DateTimeOffset asOfUtc,
        int windowDays = EventImpactAnalyzer.DefaultWindowDays,
        CancellationToken cancellationToken = default);

    Task<EventPatternSummaryResponse> GetEventPatternSummaryAsync(
        string serverId,
        string itemId,
        MarketEventType eventType,
        DateTimeOffset asOfUtc,
        int windowDays = EventPatternSummaryAnalyzer.DefaultWindowDays,
        int historyDays = EventPatternSummaryAnalyzer.DefaultHistoryDays,
        int maxEvents = EventPatternSummaryAnalyzer.DefaultMaxEvents,
        CancellationToken cancellationToken = default);

    Task<CrossServerEventStandardizationResponse> GetCrossServerEventSummaryAsync(
        string itemId,
        MarketEventType eventType,
        DateTimeOffset asOfUtc,
        int windowDays = CrossServerEventStandardizationAnalyzer.DefaultWindowDays,
        int historyDays = CrossServerEventStandardizationAnalyzer.DefaultHistoryDays,
        int maxServers = CrossServerEventStandardizationAnalyzer.DefaultMaxServers,
        int maxEventsPerServer = CrossServerEventStandardizationAnalyzer.DefaultMaxEventsPerServer,
        CancellationToken cancellationToken = default);
}
