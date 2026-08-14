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
}
