using MH.Core.Contracts;
using MH.Core.Models;

namespace MH.Core;

public sealed record CrossServerEventInput(
    string ServerId,
    IReadOnlyList<Event> Events,
    IReadOnlyList<PriceBar> DailyBars);

public static class CrossServerEventStandardizationAnalyzer
{
    public const string StatisticsVersion = "cross-server-event-standardization-v1";
    public const string StandardizationMethod = "per-server-event-median-equal-weight-v1";
    public const decimal NeutralThreshold = EventPatternSummaryAnalyzer.NeutralThreshold;
    public const int DefaultWindowDays = EventPatternSummaryAnalyzer.DefaultWindowDays;
    public const int MinimumWindowDays = EventPatternSummaryAnalyzer.MinimumWindowDays;
    public const int MaximumWindowDays = EventPatternSummaryAnalyzer.MaximumWindowDays;
    public const int DefaultHistoryDays = EventPatternSummaryAnalyzer.DefaultHistoryDays;
    public const int MinimumHistoryDays = EventPatternSummaryAnalyzer.MinimumHistoryDays;
    public const int MaximumHistoryDays = EventPatternSummaryAnalyzer.MaximumHistoryDays;
    public const int DefaultMaxServers = 20;
    public const int MinimumMaxServers = 1;
    public const int MaximumMaxServers = 50;
    public const int DefaultMaxEventsPerServer = 20;
    public const int MinimumMaxEventsPerServer = 1;
    public const int MaximumMaxEventsPerServer = 100;
    private const int MinimumComparableServers = 2;

    public static CrossServerEventStandardizationResponse Analyze(
        string itemId,
        MarketEventType eventType,
        IEnumerable<CrossServerEventInput> serverInputs,
        DateTimeOffset asOfUtc,
        int windowDays = DefaultWindowDays,
        int historyDays = DefaultHistoryDays,
        int maxServers = DefaultMaxServers,
        int maxEventsPerServer = DefaultMaxEventsPerServer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(serverInputs);
        EventPatternSummaryAnalyzer.ValidateEventType(eventType);
        EventImpactAnalyzer.ValidateWindowDays(windowDays);
        EventPatternSummaryAnalyzer.ValidateHistoryDays(historyDays);
        ValidateMaxServers(maxServers);
        ValidateMaxEventsPerServer(maxEventsPerServer);

        var cutoffUtc = asOfUtc.ToUniversalTime();
        var inputStartUtc = cutoffUtc.AddDays(-historyDays);
        var selectedInputs = serverInputs
            .Where(input => !string.IsNullOrWhiteSpace(input.ServerId))
            .GroupBy(input => input.ServerId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(input => input.ServerId, StringComparer.Ordinal)
            .Take(maxServers)
            .ToArray();

        var perServerAnalyses = selectedInputs
            .Select(input => AnalyzeServer(
                itemId,
                eventType,
                input,
                cutoffUtc,
                inputStartUtc,
                windowDays,
                historyDays,
                maxEventsPerServer))
            .ToArray();

        return new CrossServerEventStandardizationResponse(
            itemId,
            eventType,
            cutoffUtc,
            windowDays,
            historyDays,
            maxServers,
            maxEventsPerServer,
            StatisticsVersion,
            StandardizationMethod,
            NeutralThreshold,
            selectedInputs.Length,
            inputStartUtc,
            cutoffUtc,
            Summarize(perServerAnalyses.Select(x => x.DuringPrice)),
            Summarize(perServerAnalyses.Select(x => x.AfterPrice)),
            Summarize(perServerAnalyses.Select(x => x.DuringVisibleSupply)),
            Summarize(perServerAnalyses.Select(x => x.AfterVisibleSupply)));
    }

    public static void ValidateMaxServers(int maxServers)
    {
        if (maxServers is < MinimumMaxServers or > MaximumMaxServers)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxServers),
                maxServers,
                $"maxServers must be between {MinimumMaxServers} and {MaximumMaxServers}.");
        }
    }

    public static void ValidateMaxEventsPerServer(int maxEventsPerServer)
    {
        if (maxEventsPerServer is < MinimumMaxEventsPerServer or > MaximumMaxEventsPerServer)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEventsPerServer),
                maxEventsPerServer,
                $"maxEventsPerServer must be between {MinimumMaxEventsPerServer} and {MaximumMaxEventsPerServer}.");
        }
    }

    private static ServerMetricValues AnalyzeServer(
        string itemId,
        MarketEventType eventType,
        CrossServerEventInput input,
        DateTimeOffset cutoffUtc,
        DateTimeOffset inputStartUtc,
        int windowDays,
        int historyDays,
        int maxEventsPerServer)
    {
        var events = input.Events
            .Where(eventItem => eventItem.ServerId == input.ServerId
                && eventItem.Type == eventType
                && (eventItem.ItemId is null || eventItem.ItemId == itemId)
                && eventItem.StartsAtUtc.ToUniversalTime() >= inputStartUtc
                && eventItem.EndsAtUtc.ToUniversalTime() > eventItem.StartsAtUtc.ToUniversalTime()
                && eventItem.EndsAtUtc.ToUniversalTime() <= cutoffUtc)
            .OrderByDescending(eventItem => eventItem.EndsAtUtc.ToUniversalTime())
            .ThenByDescending(eventItem => eventItem.StartsAtUtc.ToUniversalTime())
            .ThenBy(eventItem => eventItem.Id, StringComparer.Ordinal)
            .Take(maxEventsPerServer)
            .ToArray();
        var bars = input.DailyBars
            .Where(bar => bar.EndUtc.ToUniversalTime() >= inputStartUtc.AddDays(-windowDays)
                && bar.EndUtc.ToUniversalTime() <= cutoffUtc)
            .ToArray();
        var analyses = events
            .Select(eventItem => EventImpactAnalyzer.Analyze(eventItem, bars, cutoffUtc, windowDays))
            .ToArray();

        return new ServerMetricValues(
            MedianComparableChange(analyses, analysis => analysis.During.PriceChangeVsBefore, analysis => analysis.During.WindowComplete),
            MedianComparableChange(analyses, analysis => analysis.After.PriceChangeVsBefore, analysis => analysis.After.WindowComplete),
            MedianComparableChange(analyses, analysis => analysis.During.VisibleSupplyChangeVsBefore, analysis => analysis.During.WindowComplete),
            MedianComparableChange(analyses, analysis => analysis.After.VisibleSupplyChangeVsBefore, analysis => analysis.After.WindowComplete));
    }

    private static decimal? MedianComparableChange(
        IEnumerable<EventImpactAnalysis> analyses,
        Func<EventImpactAnalysis, decimal?> valueSelector,
        Func<EventImpactAnalysis, bool> completeSelector)
    {
        var values = analyses
            .Where(analysis => completeSelector(analysis))
            .Select(valueSelector)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return values.Length == 0 ? null : Median(values);
    }

    private static CrossServerEventMetricSummary Summarize(IEnumerable<decimal?> serverValues)
    {
        var values = serverValues
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        if (values.Length < MinimumComparableServers)
        {
            return new CrossServerEventMetricSummary(
                false,
                values.Length,
                null,
                null,
                null,
                0,
                0,
                0,
                null,
                "comparable-servers<2");
        }

        var increaseCount = values.Count(value => value >= NeutralThreshold);
        var decreaseCount = values.Count(value => value <= -NeutralThreshold);
        var stableCount = values.Length - increaseCount - decreaseCount;
        var dominantCount = Math.Max(increaseCount, Math.Max(decreaseCount, stableCount));
        return new CrossServerEventMetricSummary(
            true,
            values.Length,
            Median(values),
            Percentile(values, 0.25m),
            Percentile(values, 0.75m),
            increaseCount,
            decreaseCount,
            stableCount,
            (decimal)dominantCount / values.Length,
            null);
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2m
            : ordered[middle];
    }

    private static decimal Percentile(IEnumerable<decimal> values, decimal percentile)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var position = (ordered.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return ordered[lower];
        }

        var fraction = position - lower;
        return ordered[lower] + (ordered[upper] - ordered[lower]) * fraction;
    }

    private sealed record ServerMetricValues(
        decimal? DuringPrice,
        decimal? AfterPrice,
        decimal? DuringVisibleSupply,
        decimal? AfterVisibleSupply);
}
