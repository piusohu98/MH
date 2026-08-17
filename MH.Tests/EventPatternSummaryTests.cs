using MH.Core;
using MH.Core.Models;

namespace MH.Tests;

public sealed class EventPatternSummaryTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2025, 2, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SummaryUsesMedianCountsAndVersionedNeutralThreshold()
    {
        var events = CreateEvents();
        var result = EventPatternSummaryAnalyzer.Analyze(
            "server-1",
            "item-1",
            MarketEventType.Holiday,
            events,
            CreateBars(events),
            AsOfUtc,
            windowDays: 3,
            historyDays: 60,
            maxEvents: 10);

        Assert.Equal("event-pattern-summary-v1", result.StatisticsVersion);
        Assert.Equal(0.03m, result.NeutralThreshold);
        Assert.Equal(3, result.SampleEventCount);
        Assert.True(result.DuringPrice.Available);
        Assert.Equal(3, result.DuringPrice.ComparableEventCount);
        Assert.Equal(0.2m, result.DuringPrice.MedianChange);
        Assert.Equal(3, result.DuringPrice.IncreaseCount);
        Assert.Equal(0, result.DuringPrice.DecreaseCount);
        Assert.Equal(0, result.DuringPrice.StableCount);
        Assert.Equal(1m, result.DuringPrice.DirectionConsistency);
        Assert.Equal(-0.2m, result.AfterPrice.MedianChange);
        Assert.Equal(0, result.AfterPrice.IncreaseCount);
        Assert.Equal(3, result.AfterPrice.DecreaseCount);
        Assert.Equal(3, result.DuringVisibleSupply.ComparableEventCount);
        Assert.Equal(0.4m, result.AfterVisibleSupply.MedianChange);
    }

    [Fact]
    public void NeutralThresholdSeparatesStableFromDirectionalChanges()
    {
        var events = CreateEvents();
        var bars = CreateBars(events);
        ReplaceDuringClose(bars, events[0], 100);
        ReplaceDuringClose(bars, events[1], 90);
        ReplaceDuringClose(bars, events[2], 110);

        var result = EventPatternSummaryAnalyzer.Analyze(
            "server-1", "item-1", MarketEventType.Holiday, events, bars, AsOfUtc, 3, 60, 10);

        Assert.Equal(0m, result.DuringPrice.MedianChange);
        Assert.Equal(1, result.DuringPrice.IncreaseCount);
        Assert.Equal(1, result.DuringPrice.DecreaseCount);
        Assert.Equal(1, result.DuringPrice.StableCount);
        Assert.Equal(1m / 3m, result.DuringPrice.DirectionConsistency);
    }

    [Fact]
    public void EachMetricRequiresThreeComparableEventsIndependently()
    {
        var events = CreateEvents();
        var bars = CreateBars(events);
        foreach (var bar in bars.Where(bar =>
                     bar.StartUtc >= events[0].StartsAtUtc.AddDays(-3)
                     && bar.EndUtc <= events[0].StartsAtUtc).ToArray())
        {
            var index = bars.IndexOf(bar);
            bars[index] = bar with { HasOcrAnomaly = true };
        }

        var result = EventPatternSummaryAnalyzer.Analyze(
            "server-1", "item-1", MarketEventType.Holiday, events, bars, AsOfUtc, 3, 60, 10);

        Assert.False(result.DuringPrice.Available);
        Assert.Equal(2, result.DuringPrice.ComparableEventCount);
        Assert.Equal("comparable-events<3", result.DuringPrice.UnavailableReason);
        Assert.True(result.DuringVisibleSupply.Available);
        Assert.Equal(3, result.DuringVisibleSupply.ComparableEventCount);
    }

    [Fact]
    public void FutureEventsAndBarsCannotChangeHistoricalSummaryAndUtcOffsetsMatch()
    {
        var events = CreateEvents();
        var future = Event("future", MarketEventType.Holiday, AsOfUtc.AddDays(5));
        var bars = CreateBars(events).Concat(CreateEventBars(future, 999, 999)).ToArray();
        var baseline = EventPatternSummaryAnalyzer.Analyze(
            "server-1", "item-1", MarketEventType.Holiday, events, CreateBars(events), AsOfUtc, 3, 60, 10);
        var withFuture = EventPatternSummaryAnalyzer.Analyze(
            "server-1", "item-1", MarketEventType.Holiday, events.Append(future), bars, AsOfUtc, 3, 60, 10);
        var equivalentOffset = EventPatternSummaryAnalyzer.Analyze(
            "server-1", "item-1", MarketEventType.Holiday, events, CreateBars(events), AsOfUtc.ToOffset(TimeSpan.FromHours(8)), 3, 60, 10);

        Assert.Equal(baseline, withFuture);
        Assert.Equal(baseline, equivalentOffset);
    }

    [Fact]
    public void SummaryReportsInsufficientSamplesAndRejectsTechnicalTypesAndInvalidBounds()
    {
        var events = CreateEvents().Take(2).ToArray();
        var insufficient = EventPatternSummaryAnalyzer.Analyze(
            "server-1", "item-1", MarketEventType.Holiday, events, CreateBars(events), AsOfUtc, 3, 60, 10);

        Assert.False(insufficient.DuringPrice.Available);
        Assert.Equal(2, insufficient.DuringPrice.ComparableEventCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => EventPatternSummaryAnalyzer.Analyze(
            "server-1", "item-1", MarketEventType.DayNight, [], [], AsOfUtc));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventPatternSummaryAnalyzer.Analyze(
            "server-1", "item-1", MarketEventType.Holiday, [], [], AsOfUtc, historyDays: 29));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventPatternSummaryAnalyzer.Analyze(
            "server-1", "item-1", MarketEventType.Holiday, [], [], AsOfUtc, maxEvents: 101));
    }

    private static Event[] CreateEvents()
        =>
        [
            Event("event-1", MarketEventType.Holiday, new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero)),
            Event("event-2", MarketEventType.Holiday, new DateTimeOffset(2025, 1, 22, 0, 0, 0, TimeSpan.Zero)),
            Event("event-3", MarketEventType.Holiday, new DateTimeOffset(2025, 2, 3, 0, 0, 0, TimeSpan.Zero))
        ];

    private static List<PriceBar> CreateBars(IEnumerable<Event> events)
        => events.SelectMany(eventItem => CreateEventBars(
                eventItem,
                eventItem.Id switch
                {
                    "event-1" => 110,
                    "event-2" => 120,
                    _ => 130
                },
                eventItem.Id switch
                {
                    "event-1" => 90,
                    "event-2" => 80,
                    _ => 70
                }))
            .ToList();

    private static IEnumerable<PriceBar> CreateEventBars(Event eventItem, int duringClose, int afterClose)
    {
        for (var offset = -3; offset < 0; offset++)
        {
            yield return Bar(eventItem.StartsAtUtc.AddDays(offset), 100, 10);
        }

        for (var offset = 0; offset < 3; offset++)
        {
            yield return Bar(eventItem.StartsAtUtc.AddDays(offset), duringClose, 12);
        }

        for (var offset = 3; offset < 6; offset++)
        {
            yield return Bar(eventItem.StartsAtUtc.AddDays(offset), afterClose, 14);
        }
    }

    private static void ReplaceDuringClose(List<PriceBar> bars, Event eventItem, int close)
    {
        for (var offset = 0; offset < 3; offset++)
        {
            var start = eventItem.StartsAtUtc.AddDays(offset);
            var index = bars.FindIndex(bar => bar.StartUtc == start);
            bars[index] = Bar(start, close, 12);
        }
    }

    private static PriceBar Bar(DateTimeOffset startUtc, int close, int volume, bool anomaly = false)
        => new(startUtc, startUtc.AddDays(1), close, close, close, close, volume, anomaly);

    private static Event Event(string id, MarketEventType type, DateTimeOffset startsAtUtc)
        => new()
        {
            Id = id,
            ServerId = "server-1",
            ItemId = "item-1",
            Type = type,
            Label = id,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = startsAtUtc.AddDays(3),
            CatalogKind = CatalogKind.Demo
        };
}
