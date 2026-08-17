using System.Globalization;
using MH.Core;
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

    public static Uri ServerMarketProfile(string serverId, DateTimeOffset asOfUtc)
        => Build(
            $"/api/v1/servers/{Segment(serverId)}/market-profile",
            [Query("asOfUtc", FormatUtc(asOfUtc))]);

    public static Uri Events(
        string serverId,
        string itemId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        MarketEventType? type = null)
    {
        var query = new List<string>
        {
            Query("fromUtc", FormatUtc(fromUtc)),
            Query("toUtc", FormatUtc(toUtc))
        };
        if (type.HasValue)
        {
            query.Add(Query("type", type.Value.ToString()));
        }

        return Build($"/api/v1/markets/{Segment(serverId)}/{Segment(itemId)}/events", query);
    }

    public static Uri EventImpact(
        string serverId,
        string itemId,
        string eventId,
        DateTimeOffset asOfUtc,
        int windowDays = EventImpactAnalyzer.DefaultWindowDays)
        => Build(
            $"/api/v1/markets/{Segment(serverId)}/{Segment(itemId)}/events/{Segment(eventId)}/impact",
            [
                Query("asOfUtc", FormatUtc(asOfUtc)),
                Query("windowDays", windowDays.ToString(CultureInfo.InvariantCulture))
            ]);

    public static Uri EventPatternSummary(
        string serverId,
        string itemId,
        MarketEventType eventType,
        DateTimeOffset asOfUtc,
        int windowDays = EventPatternSummaryAnalyzer.DefaultWindowDays,
        int historyDays = EventPatternSummaryAnalyzer.DefaultHistoryDays,
        int maxEvents = EventPatternSummaryAnalyzer.DefaultMaxEvents)
        => Build(
            $"/api/v1/markets/{Segment(serverId)}/{Segment(itemId)}/events/summary",
            [
                Query("type", eventType.ToString()),
                Query("asOfUtc", FormatUtc(asOfUtc)),
                Query("windowDays", windowDays.ToString(CultureInfo.InvariantCulture)),
                Query("historyDays", historyDays.ToString(CultureInfo.InvariantCulture)),
                Query("maxEvents", maxEvents.ToString(CultureInfo.InvariantCulture))
            ]);

    public static Uri CrossServerEventSummary(
        string itemId,
        MarketEventType eventType,
        DateTimeOffset asOfUtc,
        int windowDays = CrossServerEventStandardizationAnalyzer.DefaultWindowDays,
        int historyDays = CrossServerEventStandardizationAnalyzer.DefaultHistoryDays,
        int maxServers = CrossServerEventStandardizationAnalyzer.DefaultMaxServers,
        int maxEventsPerServer = CrossServerEventStandardizationAnalyzer.DefaultMaxEventsPerServer)
        => Build(
            $"/api/v1/items/{Segment(itemId)}/events/cross-server-summary",
            [
                Query("type", eventType.ToString()),
                Query("asOfUtc", FormatUtc(asOfUtc)),
                Query("windowDays", windowDays.ToString(CultureInfo.InvariantCulture)),
                Query("historyDays", historyDays.ToString(CultureInfo.InvariantCulture)),
                Query("maxServers", maxServers.ToString(CultureInfo.InvariantCulture)),
                Query("maxEventsPerServer", maxEventsPerServer.ToString(CultureInfo.InvariantCulture))
            ]);

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
