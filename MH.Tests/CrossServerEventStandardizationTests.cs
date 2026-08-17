using MH.Core;
using MH.Core.Models;

namespace MH.Tests;

public sealed class CrossServerEventStandardizationTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2025, 2, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EqualServerWeightUsesServerLevelMediansInsteadOfEventCount()
    {
        var serverA = CreateInput("server-a", [new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero)], [110]);
        var serverB = CreateInput(
            "server-b",
            [
                new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2025, 1, 22, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2025, 2, 3, 0, 0, 0, TimeSpan.Zero)
            ],
            [130, 130, 130]);

        var result = CrossServerEventStandardizationAnalyzer.Analyze(
            "item-1", MarketEventType.Holiday, [serverA, serverB], AsOfUtc, 3, 60, 10, 10);

        Assert.Equal("cross-server-event-standardization-v1", result.StatisticsVersion);
        Assert.Equal("per-server-event-median-equal-weight-v1", result.StandardizationMethod);
        Assert.Equal(2, result.SampleServerCount);
        Assert.True(result.DuringPrice.Available);
        Assert.Equal(2, result.DuringPrice.ComparableServerCount);
        Assert.Equal(0.2m, result.DuringPrice.MedianChange);
        Assert.Equal(0.15m, result.DuringPrice.P25Change);
        Assert.Equal(0.25m, result.DuringPrice.P75Change);
        Assert.Equal(2, result.DuringPrice.IncreaseCount);
        Assert.Equal(1m, result.DuringPrice.DirectionConsistency);
    }

    [Fact]
    public void PriceAndSupplyMissingValuesAreIndependentPerServer()
    {
        var priceMissing = CreateInput(
            "server-a",
            [new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero)],
            [110],
            markBeforePriceAnomaly: true);
        var complete = CreateInput(
            "server-b",
            [new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero)],
            [120]);

        var result = CrossServerEventStandardizationAnalyzer.Analyze(
            "item-1",
            MarketEventType.Holiday,
            [priceMissing, complete],
            AsOfUtc,
            3,
            60,
            10,
            10);

        Assert.False(result.DuringPrice.Available);
        Assert.Equal(1, result.DuringPrice.ComparableServerCount);
        Assert.True(result.DuringVisibleSupply.Available);
        Assert.Equal(2, result.DuringVisibleSupply.ComparableServerCount);
    }

    [Fact]
    public void OneServerAndTechnicalTypeAreSafeUnavailableOrRejected()
    {
        var input = CreateInput(
            "server-a",
            [new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero)],
            [110]);
        var result = CrossServerEventStandardizationAnalyzer.Analyze(
            "item-1", MarketEventType.Holiday, [input], AsOfUtc, 3, 60, 10, 10);

        Assert.Equal(1, result.SampleServerCount);
        Assert.False(result.DuringPrice.Available);
        Assert.Equal("comparable-servers<2", result.DuringPrice.UnavailableReason);
        Assert.Throws<ArgumentOutOfRangeException>(() => CrossServerEventStandardizationAnalyzer.Analyze(
            "item-1", MarketEventType.DayNight, [input], AsOfUtc));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrossServerEventStandardizationAnalyzer.Analyze(
            "item-1", MarketEventType.Holiday, [input], AsOfUtc, maxServers: 51));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrossServerEventStandardizationAnalyzer.Analyze(
            "item-1", MarketEventType.Holiday, [input], AsOfUtc, maxEventsPerServer: 101));
    }

    [Fact]
    public void FutureEventsAndBarsAndEquivalentUtcOffsetsDoNotChangeResult()
    {
        var start = new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var first = CreateInput("server-a", [start], [110]);
        var second = CreateInput("server-b", [start], [120]);
        var future = CreateInput("server-a", [AsOfUtc.AddDays(2)], [999]);
        var firstWithFuture = first with
        {
            Events = first.Events.Concat(future.Events).ToArray(),
            DailyBars = first.DailyBars.Concat(future.DailyBars).ToArray()
        };
        var baseline = CrossServerEventStandardizationAnalyzer.Analyze(
            "item-1", MarketEventType.Holiday, [first, second], AsOfUtc, 3, 60, 10, 10);
        var withFuture = CrossServerEventStandardizationAnalyzer.Analyze(
            "item-1", MarketEventType.Holiday, [firstWithFuture, second], AsOfUtc, 3, 60, 10, 10);
        var offset = CrossServerEventStandardizationAnalyzer.Analyze(
            "item-1", MarketEventType.Holiday, [first, second], AsOfUtc.ToOffset(TimeSpan.FromHours(8)), 3, 60, 10, 10);

        Assert.Equal(baseline, withFuture);
        Assert.Equal(baseline, offset);
    }

    private static CrossServerEventInput CreateInput(
        string serverId,
        IReadOnlyList<DateTimeOffset> starts,
        IReadOnlyList<int> duringCloses,
        bool markBeforePriceAnomaly = false)
    {
        var events = starts.Select((start, index) => new Event
        {
            Id = $"{serverId}-event-{index}",
            ServerId = serverId,
            ItemId = "item-1",
            Type = MarketEventType.Holiday,
            Label = "Test holiday",
            StartsAtUtc = start,
            EndsAtUtc = start.AddDays(3),
            CatalogKind = CatalogKind.Demo
        }).ToArray();
        var bars = new List<PriceBar>();
        foreach (var (eventItem, index) in events.Select((value, index) => (value, index)))
        {
            for (var offset = -3; offset < 0; offset++)
            {
                bars.Add(Bar(eventItem.StartsAtUtc.AddDays(offset), 100, 10, markBeforePriceAnomaly));
            }

            for (var offset = 0; offset < 3; offset++)
            {
                bars.Add(Bar(eventItem.StartsAtUtc.AddDays(offset), duringCloses[index], 12));
            }

            for (var offset = 3; offset < 6; offset++)
            {
                bars.Add(Bar(eventItem.StartsAtUtc.AddDays(offset), 90, 14));
            }
        }

        return new CrossServerEventInput(serverId, events, bars);
    }

    private static PriceBar Bar(DateTimeOffset startUtc, int close, int volume, bool anomaly = false)
        => new(startUtc, startUtc.AddDays(1), close, close, close, close, volume, anomaly);
}
