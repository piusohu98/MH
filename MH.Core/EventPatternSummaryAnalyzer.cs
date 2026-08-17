using MH.Core.Contracts;
using MH.Core.Models;

namespace MH.Core;

public static class EventPatternSummaryAnalyzer
{
    public const string StatisticsVersion = "event-pattern-summary-v1";
    public const decimal NeutralThreshold = 0.03m;
    public const int DefaultWindowDays = EventImpactAnalyzer.DefaultWindowDays;
    public const int MinimumWindowDays = EventImpactAnalyzer.MinimumWindowDays;
    public const int MaximumWindowDays = EventImpactAnalyzer.MaximumWindowDays;
    public const int DefaultHistoryDays = 180;
    public const int MinimumHistoryDays = 30;
    public const int MaximumHistoryDays = 366;
    public const int DefaultMaxEvents = 50;
    public const int MinimumMaxEvents = 1;
    public const int MaximumMaxEvents = 100;
    private const int MinimumComparableEvents = 3;

    public static EventPatternSummaryResponse Analyze(
        string serverId,
        string itemId,
        MarketEventType eventType,
        IEnumerable<Event> marketEvents,
        IEnumerable<PriceBar> dailyBars,
        DateTimeOffset asOfUtc,
        int windowDays = DefaultWindowDays,
        int historyDays = DefaultHistoryDays,
        int maxEvents = DefaultMaxEvents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(marketEvents);
        ArgumentNullException.ThrowIfNull(dailyBars);
        ValidateEventType(eventType);
        EventImpactAnalyzer.ValidateWindowDays(windowDays);
        ValidateHistoryDays(historyDays);
        ValidateMaxEvents(maxEvents);

        var cutoffUtc = asOfUtc.ToUniversalTime();
        var inputStartUtc = cutoffUtc.AddDays(-historyDays);
        var eligibleEvents = marketEvents
            .Where(x => x.Type == eventType
                && x.ServerId == serverId
                && (x.ItemId is null || x.ItemId == itemId)
                && x.StartsAtUtc.ToUniversalTime() >= inputStartUtc
                && x.EndsAtUtc.ToUniversalTime() > x.StartsAtUtc.ToUniversalTime()
                && x.EndsAtUtc.ToUniversalTime() <= cutoffUtc)
            .OrderByDescending(x => x.EndsAtUtc.ToUniversalTime())
            .ThenByDescending(x => x.StartsAtUtc.ToUniversalTime())
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Take(maxEvents)
            .Select(NormalizeEvent)
            .OrderBy(x => x.EndsAtUtc)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        var boundedBars = dailyBars
            .Where(x => x.EndUtc.ToUniversalTime() >= inputStartUtc.AddDays(-windowDays)
                && x.EndUtc.ToUniversalTime() <= cutoffUtc)
            .ToArray();
        var analyses = eligibleEvents
            .Select(eventItem => EventImpactAnalyzer.Analyze(eventItem, boundedBars, cutoffUtc, windowDays))
            .ToArray();

        return new EventPatternSummaryResponse(
            serverId,
            itemId,
            eventType,
            cutoffUtc,
            windowDays,
            historyDays,
            maxEvents,
            StatisticsVersion,
            NeutralThreshold,
            eligibleEvents.Length,
            inputStartUtc,
            cutoffUtc,
            Summarize(analyses, x => x.During.PriceChangeVsBefore, x => x.During.WindowComplete),
            Summarize(analyses, x => x.After.PriceChangeVsBefore, x => x.After.WindowComplete),
            Summarize(analyses, x => x.During.VisibleSupplyChangeVsBefore, x => x.During.WindowComplete),
            Summarize(analyses, x => x.After.VisibleSupplyChangeVsBefore, x => x.After.WindowComplete));
    }

    public static void ValidateEventType(MarketEventType eventType)
    {
        if (eventType is not (MarketEventType.Holiday or MarketEventType.SupplyChange))
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Only Holiday and SupplyChange can be summarized.");
        }
    }

    public static void ValidateHistoryDays(int historyDays)
    {
        if (historyDays is < MinimumHistoryDays or > MaximumHistoryDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(historyDays),
                historyDays,
                $"historyDays must be between {MinimumHistoryDays} and {MaximumHistoryDays}.");
        }
    }

    public static void ValidateMaxEvents(int maxEvents)
    {
        if (maxEvents is < MinimumMaxEvents or > MaximumMaxEvents)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEvents),
                maxEvents,
                $"maxEvents must be between {MinimumMaxEvents} and {MaximumMaxEvents}.");
        }
    }

    private static EventPatternMetricSummary Summarize(
        IEnumerable<EventImpactAnalysis> analyses,
        Func<EventImpactAnalysis, decimal?> valueSelector,
        Func<EventImpactAnalysis, bool> completenessSelector)
    {
        var values = analyses
            .Where(analysis => completenessSelector(analysis))
            .Select(valueSelector)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        var comparableCount = values.Length;
        if (comparableCount < MinimumComparableEvents)
        {
            return new EventPatternMetricSummary(
                false,
                comparableCount,
                null,
                0,
                0,
                0,
                null,
                "comparable-events<3");
        }

        var increaseCount = values.Count(value => value >= NeutralThreshold);
        var decreaseCount = values.Count(value => value <= -NeutralThreshold);
        var stableCount = comparableCount - increaseCount - decreaseCount;
        var dominantCount = Math.Max(increaseCount, Math.Max(decreaseCount, stableCount));
        return new EventPatternMetricSummary(
            true,
            comparableCount,
            Median(values),
            increaseCount,
            decreaseCount,
            stableCount,
            (decimal)dominantCount / comparableCount,
            null);
    }

    private static Event NormalizeEvent(Event value)
        => new()
        {
            Id = value.Id,
            ServerId = value.ServerId,
            ItemId = value.ItemId,
            Type = value.Type,
            Label = value.Label,
            StartsAtUtc = value.StartsAtUtc.ToUniversalTime(),
            EndsAtUtc = value.EndsAtUtc.ToUniversalTime(),
            CatalogKind = value.CatalogKind
        };

    private static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2m
            : ordered[middle];
    }
}
