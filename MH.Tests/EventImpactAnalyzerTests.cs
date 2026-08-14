using MH.Core;
using MH.Core.Models;

namespace MH.Tests;

public sealed class EventImpactAnalyzerTests
{
    private static readonly DateTimeOffset EventStartUtc = new(2025, 1, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EventEndUtc = new(2025, 1, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompleteEventReturnsBeforeDuringAfterFactsAndComparisons()
    {
        var analysis = EventImpactAnalyzer.Analyze(
            CreateEvent(),
            [
                Bar(7, 100, 10), Bar(8, 100, 20), Bar(9, 100, 30),
                Bar(10, 120, 20), Bar(11, 120, 40), Bar(12, 120, 60),
                Bar(13, 80, 5), Bar(14, 80, 10), Bar(15, 80, 15)
            ],
            new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero),
            windowDays: 3);

        Assert.Equal(EventImpactAvailability.Available, analysis.Before.Availability);
        Assert.Equal(EventImpactAvailability.Available, analysis.During.Availability);
        Assert.Equal(EventImpactAvailability.Available, analysis.After.Availability);
        Assert.All(new[] { analysis.Before, analysis.During, analysis.After }, phase =>
        {
            Assert.True(phase.WindowComplete);
            Assert.Equal(3, phase.RawBarCount);
            Assert.Equal(3, phase.PriceInlierCount);
            Assert.Equal(3, phase.VolumeSampleCount);
        });
        Assert.Equal(100m, analysis.Before.RobustMedianPrice);
        Assert.Equal(20m, analysis.Before.VisibleSupplyMedian);
        Assert.Equal(120m, analysis.During.RobustMedianPrice);
        Assert.Equal(40m, analysis.During.VisibleSupplyMedian);
        Assert.Equal(0.2m, analysis.During.PriceChangeVsBefore);
        Assert.Equal(1m, analysis.During.VisibleSupplyChangeVsBefore);
        Assert.Equal(-0.2m, analysis.After.PriceChangeVsBefore);
        Assert.Equal(-0.5m, analysis.After.VisibleSupplyChangeVsBefore);
    }

    [Fact]
    public void OcrAnomalyIsExcludedFromPriceButRetainedForVisibleSupply()
    {
        var analysis = EventImpactAnalyzer.Analyze(
            CreateEvent(EventStartUtc, EventStartUtc.AddDays(4)),
            [
                Bar(7, 100, 10), Bar(8, 100, 20), Bar(9, 100, 30),
                Bar(10, 9999, 10, anomaly: true), Bar(11, 120, 20), Bar(12, 122, 30), Bar(13, 121, 40)
            ],
            new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero),
            windowDays: 3);

        Assert.Equal(4, analysis.During.RawBarCount);
        Assert.Equal(1, analysis.During.OcrExcludedCount);
        Assert.Equal(3, analysis.During.PriceInlierCount);
        Assert.Equal(121m, analysis.During.RobustMedianPrice);
        Assert.Equal(4, analysis.During.VolumeSampleCount);
        Assert.Equal(25m, analysis.During.VisibleSupplyMedian);
    }

    [Fact]
    public void PriceAndSupplyComparisonsRemainIndependentWhenPriceBaselineIsUnavailable()
    {
        var analysis = EventImpactAnalyzer.Analyze(
            CreateEvent(),
            [
                Bar(7, 100, 10, anomaly: true), Bar(8, 101, 20, anomaly: true), Bar(9, 102, 30, anomaly: true),
                Bar(10, 120, 20), Bar(11, 120, 40), Bar(12, 120, 60),
                Bar(13, 80, 5), Bar(14, 80, 10), Bar(15, 80, 15)
            ],
            new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero),
            windowDays: 3);

        Assert.Null(analysis.Before.RobustMedianPrice);
        Assert.Equal(20m, analysis.Before.VisibleSupplyMedian);
        Assert.Null(analysis.During.PriceChangeVsBefore);
        Assert.Equal("baseline-price-unavailable", analysis.During.PriceComparisonUnavailableReason);
        Assert.Equal(1m, analysis.During.VisibleSupplyChangeVsBefore);
        Assert.Null(analysis.During.VisibleSupplyComparisonUnavailableReason);
    }

    [Fact]
    public void PriceComparisonRemainsAvailableWhenVisibleSupplyBaselineIsZero()
    {
        var analysis = EventImpactAnalyzer.Analyze(
            CreateEvent(),
            [
                Bar(7, 100, 0), Bar(8, 100, 0), Bar(9, 100, 0),
                Bar(10, 120, 20), Bar(11, 120, 40), Bar(12, 120, 60),
                Bar(13, 80, 5), Bar(14, 80, 10), Bar(15, 80, 15)
            ],
            new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero),
            windowDays: 3);

        Assert.Equal(100m, analysis.Before.RobustMedianPrice);
        Assert.Equal(0m, analysis.Before.VisibleSupplyMedian);
        Assert.Equal(0.2m, analysis.During.PriceChangeVsBefore);
        Assert.Null(analysis.During.PriceComparisonUnavailableReason);
        Assert.Null(analysis.During.VisibleSupplyChangeVsBefore);
        Assert.Equal("baseline-visible-supply-unavailable", analysis.During.VisibleSupplyComparisonUnavailableReason);
    }

    [Fact]
    public void MadZeroUsesExactMedianValueAsTheZeroDeviationRule()
    {
        var analysis = EventImpactAnalyzer.Analyze(
            CreateEvent(EventStartUtc, EventStartUtc.AddDays(4)),
            [
                Bar(10, 100, 10), Bar(11, 100, 20), Bar(12, 100, 30), Bar(13, 1000, 40),
                Bar(7, 90, 10), Bar(8, 90, 20), Bar(9, 90, 30),
                Bar(14, 110, 10), Bar(15, 110, 20), Bar(16, 110, 30)
            ],
            new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero),
            windowDays: 3);

        Assert.Equal(0m, analysis.During.PriceMad);
        Assert.Equal(3, analysis.During.PriceInlierCount);
        Assert.Equal(100m, analysis.During.RobustMedianPrice);
    }

    [Fact]
    public void NonOcrExtremePriceIsRemovedByThreeMadFilter()
    {
        var analysis = EventImpactAnalyzer.Analyze(
            CreateEvent(EventStartUtc, EventStartUtc.AddDays(4)),
            [
                Bar(7, 100, 10), Bar(8, 100, 20), Bar(9, 100, 30),
                Bar(10, 100, 10), Bar(11, 101, 20), Bar(12, 102, 30), Bar(13, 10000, 40),
                Bar(14, 90, 10), Bar(15, 90, 20), Bar(16, 90, 30)
            ],
            new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero),
            windowDays: 3);

        Assert.Equal(4, analysis.During.RawBarCount);
        Assert.Equal(3, analysis.During.PriceInlierCount);
        Assert.Equal(101m, analysis.During.RobustMedianPrice);
    }

    [Fact]
    public void OngoingEventIsPartialAndAfterIsNotStarted()
    {
        var analysis = EventImpactAnalyzer.Analyze(
            CreateEvent(),
            [Bar(7, 100, 10), Bar(8, 100, 20), Bar(9, 100, 30), Bar(10, 120, 10)],
            new DateTimeOffset(2025, 1, 11, 12, 0, 0, TimeSpan.Zero),
            windowDays: 3);

        Assert.Equal(EventImpactAvailability.Available, analysis.Before.Availability);
        Assert.Equal(EventImpactAvailability.Partial, analysis.During.Availability);
        Assert.False(analysis.During.WindowComplete);
        Assert.Equal(EventImpactAvailability.NotStarted, analysis.After.Availability);
        Assert.Contains("phase-in-progress", analysis.During.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void FutureEventIsNotStarted()
    {
        var analysis = EventImpactAnalyzer.Analyze(
            CreateEvent(EventStartUtc.AddDays(10), EventEndUtc.AddDays(10)),
            [],
            EventStartUtc,
            windowDays: 3);

        Assert.Equal(EventImpactAvailability.NotStarted, analysis.Before.Availability);
        Assert.Equal(EventImpactAvailability.NotStarted, analysis.During.Availability);
        Assert.Equal(EventImpactAvailability.NotStarted, analysis.After.Availability);
        Assert.False(analysis.Before.WindowComplete);
    }

    [Fact]
    public void CompletePhaseWithTooFewSamplesIsInsufficientData()
    {
        var analysis = EventImpactAnalyzer.Analyze(
            CreateEvent(),
            [Bar(7, 100, 10), Bar(8, 100, 20), Bar(10, 120, 10), Bar(13, 80, 10)],
            new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero),
            windowDays: 3);

        Assert.Equal(EventImpactAvailability.InsufficientData, analysis.Before.Availability);
        Assert.Equal(EventImpactAvailability.InsufficientData, analysis.During.Availability);
        Assert.Equal(EventImpactAvailability.InsufficientData, analysis.After.Availability);
        Assert.Contains("insufficient", analysis.Before.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnorderedBarsAndFutureExtremeBarsCannotChangeHistoricalResult()
    {
        var asOfUtc = new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero);
        var bars = new[]
        {
            Bar(7, 100, 10), Bar(8, 100, 20), Bar(9, 100, 30),
            Bar(10, 120, 10), Bar(11, 120, 20), Bar(12, 120, 30),
            Bar(13, 80, 10), Bar(14, 80, 20), Bar(15, 80, 30)
        };

        var baseline = EventImpactAnalyzer.Analyze(CreateEvent(), bars, asOfUtc, 3);
        var withFuture = EventImpactAnalyzer.Analyze(
            CreateEvent(),
            bars.Reverse().Append(Bar(20, 999999, 999999)),
            asOfUtc,
            3);
        var equivalentOffset = EventImpactAnalyzer.Analyze(
            CreateEvent(),
            bars.Reverse(),
            new DateTimeOffset(2025, 1, 20, 8, 0, 0, TimeSpan.FromHours(8)),
            3);

        Assert.Equal(baseline.Before, withFuture.Before);
        Assert.Equal(baseline.During, withFuture.During);
        Assert.Equal(baseline.After, withFuture.After);
        Assert.Equal(baseline.Before, equivalentOffset.Before);
        Assert.Equal(baseline.During, equivalentOffset.During);
        Assert.Equal(baseline.After, equivalentOffset.After);
    }

    [Fact]
    public void MissingBaselineLeavesComparisonsNullWithReason()
    {
        var analysis = EventImpactAnalyzer.Analyze(
            CreateEvent(),
            [
                Bar(10, 120, 10), Bar(11, 120, 20), Bar(12, 120, 30),
                Bar(13, 80, 10), Bar(14, 80, 20), Bar(15, 80, 30)
            ],
            new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero),
            3);

        Assert.Null(analysis.During.PriceChangeVsBefore);
        Assert.Null(analysis.During.VisibleSupplyChangeVsBefore);
        Assert.Equal("baseline-price-unavailable", analysis.During.PriceComparisonUnavailableReason);
        Assert.Equal("baseline-visible-supply-unavailable", analysis.During.VisibleSupplyComparisonUnavailableReason);
    }

    [Fact]
    public void InvalidEventWindowDuplicateAndNonDailyBarsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => EventImpactAnalyzer.Analyze(
            CreateEvent(EventStartUtc, EventStartUtc), [], EventStartUtc, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventImpactAnalyzer.Analyze(
            CreateEvent(), [], EventStartUtc, 2));
        Assert.Throws<ArgumentException>(() => EventImpactAnalyzer.Analyze(
            CreateEvent(), [Bar(7, 100, 10), Bar(7, 101, 20)], EventStartUtc.AddDays(20), 3));
        Assert.Throws<ArgumentException>(() => EventImpactAnalyzer.Analyze(
            CreateEvent(), [new PriceBar(EventStartUtc.AddDays(1), EventStartUtc.AddDays(1).AddHours(2), 1, 1, 1, 1, 1, false)], EventStartUtc.AddDays(20), 3));
    }

    private static Event CreateEvent(
        DateTimeOffset? startsAtUtc = null,
        DateTimeOffset? endsAtUtc = null)
        => new()
        {
            Id = "event-1",
            ServerId = "server-1",
            ItemId = "item-1",
            Type = MarketEventType.Holiday,
            Label = "Test event",
            StartsAtUtc = startsAtUtc ?? EventStartUtc,
            EndsAtUtc = endsAtUtc ?? EventEndUtc,
            CatalogKind = CatalogKind.Demo
        };

    private static PriceBar Bar(int dayOffset, int close, int volume, bool anomaly = false)
    {
        var start = EventStartUtc.AddDays(dayOffset - 10);
        return new PriceBar(start, start.AddDays(1), close, close, close, close, volume, anomaly);
    }
}
