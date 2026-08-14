using System.Globalization;
using MH.Core.Models;

namespace MH.Client.Api;

public static class MarketApiUris
{
    public static Uri Catalog(CatalogKind catalogKind = CatalogKind.Demo)
        => Build("/api/v1/catalog", [Query("kind", catalogKind.ToString().ToLowerInvariant())]);

    public static Uri Series(
        string serverId,
        string itemId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null)
    {
        var query = new List<string>(2);
        if (fromUtc.HasValue)
        {
            query.Add(Query("fromUtc", FormatUtc(fromUtc.Value)));
        }

        if (toUtc.HasValue)
        {
            query.Add(Query("toUtc", FormatUtc(toUtc.Value)));
        }

        return Build($"/api/v1/markets/{Segment(serverId)}/{Segment(itemId)}/series", query);
    }

    public static Uri Indicators(string serverId, string itemId, DateTimeOffset asOfUtc)
        => Build(
            $"/api/v1/markets/{Segment(serverId)}/{Segment(itemId)}/indicators",
            [Query("asOfUtc", FormatUtc(asOfUtc))]);

    public static Uri Recommendation(string serverId, string itemId, DateTimeOffset asOfUtc)
        => Build(
            $"/api/v1/markets/{Segment(serverId)}/{Segment(itemId)}/recommendation",
            [Query("asOfUtc", FormatUtc(asOfUtc))]);

    private static string Segment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Uri.EscapeDataString(value);
    }

    private static string FormatUtc(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string Query(string name, string value)
        => $"{name}={Uri.EscapeDataString(value)}";

    private static Uri Build(string path, IReadOnlyList<string> query)
        => new(
            query.Count == 0 ? path : $"{path}?{string.Join('&', query)}",
            UriKind.Relative);
}
