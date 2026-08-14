using System.Net.Http;
using System.Net.Http.Json;
using MH.Core.Contracts;
using MH.Core.Models;

namespace MH.Client.Api;

public sealed class HttpMarketApiClient(HttpClient httpClient) : IReadOnlyMarketApiClient
{
    public Task<CatalogResponse> GetCatalogAsync(
        CatalogKind catalogKind = CatalogKind.Demo,
        CancellationToken cancellationToken = default)
        => GetAsync<CatalogResponse>(MarketApiUris.Catalog(catalogKind), cancellationToken);

    public Task<MarketSeriesResponse> GetSeriesAsync(
        string serverId,
        string itemId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default)
        => GetAsync<MarketSeriesResponse>(MarketApiUris.Series(serverId, itemId, fromUtc, toUtc), cancellationToken);

    public Task<MarketIndicatorsResponse> GetIndicatorsAsync(
        string serverId,
        string itemId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default)
        => GetAsync<MarketIndicatorsResponse>(MarketApiUris.Indicators(serverId, itemId, asOfUtc), cancellationToken);

    public Task<RecommendationPreviewResponse> GetRecommendationAsync(
        string serverId,
        string itemId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default)
        => GetAsync<RecommendationPreviewResponse>(MarketApiUris.Recommendation(serverId, itemId, asOfUtc), cancellationToken);

    private async Task<TResponse> GetAsync<TResponse>(Uri requestUri, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var response = await httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("市场服务返回了空响应。");
    }
}
