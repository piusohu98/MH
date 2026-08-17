using MH.Core;
using MH.Core.Contracts;
using MH.Core.Models;

namespace MH.Tests;

public sealed class ServerMarketProfileAnalyzerTests
{
    private static readonly DateTimeOffset CutoffUtc = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProducesBoundedDimensionlessProxyMetrics()
    {
        var result = ServerMarketProfileAnalyzer.Analyze(
            "server-a",
            CreateObservations(),
            catalogItemCount: 12,
            CutoffUtc);

        Assert.Equal(ServerMarketProfileAnalyzer.StatisticsVersion, result.StatisticsVersion);
        Assert.Equal(ServerMarketProfileAnalyzer.ScopeNotice, result.ScopeNotice);
        Assert.Equal(ServerProxyAvailability.Available, result.Activity.Availability);
        Assert.Equal(ServerProxyAvailability.Available, result.HighValueDemand.Availability);
        Assert.InRange(result.Activity.Score!.Value, 0m, 100m);
        Assert.InRange(result.HighValueDemand.Score!.Value, 0m, 100m);
        Assert.InRange(result.Activity.Confidence, 0m, 1m);
        Assert.InRange(result.HighValueDemand.Confidence, 0m, 1m);
        Assert.Contains(result.Activity.Evidence, item => item.Code == "visible-quantity-change-rate");
        Assert.Contains(result.HighValueDemand.Evidence, item => item.Code == "visible-quantity-decline-rate");
        Assert.Contains("不代表真实在线人数", result.ScopeNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresObservationsAfterHistoricalCutoffAndNormalizesUtc()
    {
        var observations = CreateObservations().ToList();
        var baseline = ServerMarketProfileAnalyzer.Analyze("server-a", observations, 12, CutoffUtc);
        observations.Add(CreateObservation(
            "item-4",
            CutoffUtc.AddMinutes(1),
            999999,
            1,
            isOcrAnomaly: false));

        var equivalentOffset = CutoffUtc.ToOffset(TimeSpan.FromHours(8));
        var after = ServerMarketProfileAnalyzer.Analyze("server-a", observations, 12, equivalentOffset);

        Assert.Equal(baseline.AsOfUtc, after.AsOfUtc);
        Assert.Equal(baseline.Activity.Score, after.Activity.Score);
        Assert.Equal(baseline.Activity.Confidence, after.Activity.Confidence);
        Assert.True(baseline.Activity.Evidence.SequenceEqual(after.Activity.Evidence));
        Assert.Equal(baseline.HighValueDemand.Score, after.HighValueDemand.Score);
        Assert.Equal(baseline.HighValueDemand.Confidence, after.HighValueDemand.Confidence);
        Assert.True(baseline.HighValueDemand.Evidence.SequenceEqual(after.HighValueDemand.Evidence));
    }

    [Fact]
    public void MarksOldObservationsStaleWithoutReturningScores()
    {
        var result = ServerMarketProfileAnalyzer.Analyze(
            "server-a",
            CreateObservations(),
            12,
            CutoffUtc.AddDays(3));

        Assert.Equal(ServerProxyAvailability.Stale, result.Activity.Availability);
        Assert.Equal(ServerProxyAvailability.Stale, result.HighValueDemand.Availability);
        Assert.Null(result.Activity.Score);
        Assert.Null(result.HighValueDemand.Score);
        Assert.Equal(ServerProxyLevel.Unknown, result.Activity.Level);
        Assert.Equal("stale-data", result.Activity.UnavailableReason);
    }

    [Fact]
    public void OcrAnomaliesDoNotRemoveVisibleQuantitySignal()
    {
        var observations = CreateObservations(isOcrAnomaly: true);

        var result = ServerMarketProfileAnalyzer.Analyze("server-a", observations, 12, CutoffUtc);

        Assert.Equal(ServerProxyAvailability.Available, result.Activity.Availability);
        Assert.Contains(result.Activity.Evidence, item => item.Code == "visible-quantity-change-rate");
        Assert.DoesNotContain(result.Activity.Evidence, item => item.Code == "price-change-rate");
        Assert.Equal(ServerProxyAvailability.InsufficientData, result.HighValueDemand.Availability);
    }

    [Fact]
    public void ReportsInsufficientCoverageInsteadOfGuessing()
    {
        var observations = new[]
        {
            CreateObservation("item-1", CutoffUtc.AddHours(-1), 100, 10, false)
        };

        var result = ServerMarketProfileAnalyzer.Analyze("server-a", observations, 4, CutoffUtc);

        Assert.Equal(ServerProxyAvailability.InsufficientData, result.Activity.Availability);
        Assert.Equal(ServerProxyAvailability.InsufficientData, result.HighValueDemand.Availability);
        Assert.Null(result.Activity.Score);
        Assert.Null(result.HighValueDemand.Score);
        Assert.Equal(0m, result.Activity.Confidence);
    }

    private static IReadOnlyList<ListingObservation> CreateObservations(bool isOcrAnomaly = false)
    {
        var observations = new List<ListingObservation>();
        for (var day = 0; day < 7; day++)
        {
            for (var slot = 0; slot < 2; slot++)
            {
                var observedAtUtc = CutoffUtc.AddDays(day - 7).AddHours(slot * 8 + 2);
                for (var item = 1; item <= 12; item++)
                {
                    observations.Add(CreateObservation(
                        $"item-{item}",
                        observedAtUtc,
                        100 * item + day * item + slot,
                        30 + item * 4 + ((day + slot + item) % 5),
                        isOcrAnomaly));
                }
            }
        }

        return observations;
    }

    private static ListingObservation CreateObservation(
        string itemId,
        DateTimeOffset observedAtUtc,
        int price,
        int quantity,
        bool isOcrAnomaly)
        => new()
        {
            SnapshotBatchId = $"batch-{itemId}-{observedAtUtc:O}",
            ServerId = "server-a",
            ItemId = itemId,
            ObservedAtUtc = observedAtUtc,
            Price = price,
            Quantity = quantity,
            IsOcrAnomaly = isOcrAnomaly
        };
}
