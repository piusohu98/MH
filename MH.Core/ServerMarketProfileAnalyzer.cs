using MH.Core.Contracts;
using MH.Core.Models;

namespace MH.Core;

public static class ServerMarketProfileAnalyzer
{
    public const string StatisticsVersion = "server-market-profile-v1";
    public const int WindowDays = 7;
    public const decimal MaximumDataAgeHours = 48m;
    public const string ScopeNotice = "区服活跃度和高价值需求均为可见行情代理，不代表真实在线人数、成交量或高消费玩家人数。";

    private const int MinimumObservedItems = 3;
    private const int MinimumHighValueItems = 3;
    private const int MinimumCapturePoints = 3;
    private const int MinimumTransitions = 6;
    private const decimal HighValuePercentile = 0.75m;

    public static ServerMarketProfileResponse Analyze(
        string serverId,
        IEnumerable<ListingObservation> observations,
        int catalogItemCount,
        DateTimeOffset asOfUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentOutOfRangeException.ThrowIfNegative(catalogItemCount);

        var cutoffUtc = asOfUtc.ToUniversalTime();
        var startUtc = cutoffUtc.AddDays(-WindowDays);
        var selected = observations
            .Where(item => string.Equals(item.ServerId, serverId, StringComparison.Ordinal)
                && item.ObservedAtUtc.ToUniversalTime() >= startUtc
                && item.ObservedAtUtc.ToUniversalTime() <= cutoffUtc
                && item.Price > 0
                && item.Quantity > 0)
            .Select(item => new Observation(
                item.ItemId,
                item.ObservedAtUtc.ToUniversalTime(),
                item.Price,
                item.Quantity,
                item.IsOcrAnomaly))
            .OrderBy(item => item.ObservedAtUtc)
            .ThenBy(item => item.ItemId, StringComparer.Ordinal)
            .ToArray();

        decimal? dataAgeHours = selected.Length == 0
            ? null
            : (decimal)(cutoffUtc - selected.Max(item => item.ObservedAtUtc)).Ticks / TimeSpan.TicksPerHour;
        var activity = AnalyzeActivity(selected, catalogItemCount, dataAgeHours);
        var highValueDemand = AnalyzeHighValueDemand(selected, dataAgeHours);

        return new ServerMarketProfileResponse(
            serverId,
            cutoffUtc,
            WindowDays,
            StatisticsVersion,
            activity,
            highValueDemand,
            ScopeNotice);
    }

    private static ServerProxyMetric AnalyzeActivity(
        IReadOnlyList<Observation> observations,
        int catalogItemCount,
        decimal? dataAgeHours)
    {
        var observedItemCount = observations.Select(item => item.ItemId).Distinct(StringComparer.Ordinal).Count();
        var capturePointCount = observations.Select(item => item.ObservedAtUtc).Distinct().Count();
        if (observations.Count == 0
            || observedItemCount < MinimumObservedItems
            || capturePointCount < MinimumCapturePoints)
        {
            return Unavailable(
                ServerProxyAvailability.InsufficientData,
                observations.Count,
                observedItemCount,
                0,
                dataAgeHours,
                "insufficient-coverage");
        }

        if (dataAgeHours is > MaximumDataAgeHours)
        {
            return Unavailable(
                ServerProxyAvailability.Stale,
                observations.Count,
                observedItemCount,
                0,
                dataAgeHours,
                "stale-data");
        }

        var transitions = BuildTransitions(observations);
        var quantityTransitions = transitions.Count;
        var validPriceTransitions = transitions.Where(item => !item.Previous.IsOcrAnomaly && !item.Current.IsOcrAnomaly).ToArray();
        decimal? quantityChangeRate = quantityTransitions >= MinimumTransitions
            ? (decimal)transitions.Count(item => item.Previous.Quantity != item.Current.Quantity) / quantityTransitions
            : null;
        decimal? priceChangeRate = validPriceTransitions.Length >= MinimumTransitions
            ? (decimal)validPriceTransitions.Count(item => item.Previous.Price != item.Current.Price) / validPriceTransitions.Length
            : null;
        if (!quantityChangeRate.HasValue && !priceChangeRate.HasValue)
        {
            return Unavailable(
                ServerProxyAvailability.InsufficientData,
                observations.Count,
                observedItemCount,
                Math.Max(quantityTransitions, validPriceTransitions.Length),
                dataAgeHours,
                "insufficient-transitions");
        }

        var itemCoverage = catalogItemCount == 0
            ? 0m
            : Math.Min(1m, (decimal)observedItemCount / catalogItemCount);
        var dayCoverage = Math.Min(1m, (decimal)observations.Select(item => DateOnly.FromDateTime(item.ObservedAtUtc.UtcDateTime)).Distinct().Count() / WindowDays);
        var components = new List<(decimal Value, decimal Weight)>
        {
            (itemCoverage, 0.20m),
            (dayCoverage, 0.10m)
        };
        if (quantityChangeRate.HasValue)
        {
            components.Add((quantityChangeRate.Value, 0.35m));
        }

        if (priceChangeRate.HasValue)
        {
            components.Add((priceChangeRate.Value, 0.35m));
        }

        var score = Score(components);
        var confidence = decimal.Round(
            Math.Min(1m, Math.Max(quantityTransitions, validPriceTransitions.Length) / 100m) * 0.4m
                + itemCoverage * 0.3m
                + dayCoverage * 0.3m,
            3);
        var evidence = new List<ServerProxyEvidence>
        {
            new("catalog-item-coverage", itemCoverage, "ratio"),
            new("capture-day-coverage", dayCoverage, "ratio")
        };
        if (quantityChangeRate.HasValue)
        {
            evidence.Add(new("visible-quantity-change-rate", quantityChangeRate.Value, "ratio"));
        }

        if (priceChangeRate.HasValue)
        {
            evidence.Add(new("price-change-rate", priceChangeRate.Value, "ratio"));
        }

        return Available(
            score,
            confidence,
            observations.Count,
            observedItemCount,
            Math.Max(quantityTransitions, validPriceTransitions.Length),
            dataAgeHours,
            evidence);
    }

    private static ServerProxyMetric AnalyzeHighValueDemand(
        IReadOnlyList<Observation> observations,
        decimal? dataAgeHours)
    {
        var observedItemCount = observations.Select(item => item.ItemId).Distinct(StringComparer.Ordinal).Count();
        if (observations.Count == 0 || observedItemCount < MinimumObservedItems)
        {
            return Unavailable(
                ServerProxyAvailability.InsufficientData,
                observations.Count,
                observedItemCount,
                0,
                dataAgeHours,
                "insufficient-high-value-items");
        }

        if (dataAgeHours is > MaximumDataAgeHours)
        {
            return Unavailable(
                ServerProxyAvailability.Stale,
                observations.Count,
                observedItemCount,
                0,
                dataAgeHours,
                "stale-data");
        }

        var itemMedians = observations
            .Where(item => !item.IsOcrAnomaly)
            .GroupBy(item => item.ItemId, StringComparer.Ordinal)
            .Where(group => group.Count() >= MinimumCapturePoints)
            .Select(group => new ItemMedian(group.Key, Median(group.Select(item => (decimal)item.Price))))
            .OrderBy(item => item.Value)
            .ToArray();
        if (itemMedians.Length < MinimumObservedItems)
        {
            return Unavailable(
                ServerProxyAvailability.InsufficientData,
                observations.Count,
                itemMedians.Length,
                0,
                dataAgeHours,
                "insufficient-high-value-items");
        }

        var threshold = Percentile(itemMedians.Select(item => item.Value), HighValuePercentile);
        var highValueIds = itemMedians
            .Where(item => item.Value >= threshold)
            .Select(item => item.ItemId)
            .ToHashSet(StringComparer.Ordinal);
        if (highValueIds.Count < MinimumHighValueItems)
        {
            return Unavailable(
                ServerProxyAvailability.InsufficientData,
                observations.Count,
                highValueIds.Count,
                0,
                dataAgeHours,
                "insufficient-high-value-items");
        }

        var highValueObservations = observations.Where(item => highValueIds.Contains(item.ItemId)).ToArray();
        var transitions = BuildTransitions(highValueObservations);
        if (transitions.Count < MinimumTransitions)
        {
            return Unavailable(
                ServerProxyAvailability.InsufficientData,
                highValueObservations.Length,
                highValueIds.Count,
                transitions.Count,
                dataAgeHours,
                "insufficient-high-value-transitions");
        }

        var declines = transitions.Where(item => item.Current.Quantity < item.Previous.Quantity).ToArray();
        var declineRate = (decimal)declines.Length / transitions.Count;
        var validDeclines = declines
            .Where(item => !item.Previous.IsOcrAnomaly && !item.Current.IsOcrAnomaly)
            .ToArray();
        decimal? priceResilienceRate = validDeclines.Length >= MinimumCapturePoints
            ? (decimal)validDeclines.Count(item => item.Current.Price >= item.Previous.Price * 0.98m) / validDeclines.Length
            : null;
        var components = new List<(decimal Value, decimal Weight)> { (declineRate, 0.65m) };
        if (priceResilienceRate.HasValue)
        {
            components.Add((priceResilienceRate.Value, 0.35m));
        }

        var score = Score(components);
        var confidence = decimal.Round(
            Math.Min(1m, transitions.Count / 50m) * 0.6m
                + Math.Min(1m, highValueIds.Count / 4m) * 0.25m
                + (priceResilienceRate.HasValue ? 0.15m : 0m),
            3);
        var evidence = new List<ServerProxyEvidence>
        {
            new("high-value-price-threshold", threshold, "price"),
            new("visible-quantity-decline-rate", declineRate, "ratio")
        };
        if (priceResilienceRate.HasValue)
        {
            evidence.Add(new("price-resilience-after-quantity-decline", priceResilienceRate.Value, "ratio"));
        }

        return Available(
            score,
            confidence,
            highValueObservations.Length,
            highValueIds.Count,
            transitions.Count,
            dataAgeHours,
            evidence);
    }

    private static IReadOnlyList<Transition> BuildTransitions(IEnumerable<Observation> observations)
    {
        var transitions = new List<Transition>();
        foreach (var group in observations.GroupBy(item => item.ItemId, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(item => item.ObservedAtUtc).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index - 1].ObservedAtUtc < ordered[index].ObservedAtUtc)
                {
                    transitions.Add(new Transition(ordered[index - 1], ordered[index]));
                }
            }
        }

        return transitions;
    }

    private static ServerProxyMetric Available(
        decimal score,
        decimal confidence,
        int observationCount,
        int observedItemCount,
        int transitionCount,
        decimal? dataAgeHours,
        IReadOnlyList<ServerProxyEvidence> evidence)
        => new(
            ServerProxyAvailability.Available,
            score,
            GetLevel(score),
            confidence,
            observationCount,
            observedItemCount,
            transitionCount,
            dataAgeHours,
            evidence,
            null);

    private static ServerProxyMetric Unavailable(
        ServerProxyAvailability availability,
        int observationCount,
        int observedItemCount,
        int transitionCount,
        decimal? dataAgeHours,
        string reason)
        => new(
            availability,
            null,
            ServerProxyLevel.Unknown,
            0m,
            observationCount,
            observedItemCount,
            transitionCount,
            dataAgeHours,
            [],
            reason);

    private static decimal Score(IReadOnlyList<(decimal Value, decimal Weight)> components)
    {
        var weight = components.Sum(component => component.Weight);
        return decimal.Round(100m * components.Sum(component => component.Value * component.Weight) / weight, 1);
    }

    private static ServerProxyLevel GetLevel(decimal score)
        => score < 40m
            ? ServerProxyLevel.Low
            : score < 70m
                ? ServerProxyLevel.Medium
                : ServerProxyLevel.High;

    private static decimal Median(IEnumerable<decimal> values)
        => Percentile(values, 0.5m);

    private static decimal Percentile(IEnumerable<decimal> values, decimal percentile)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var position = (ordered.Length - 1) * percentile;
        var lower = (int)decimal.Floor(position);
        var upper = (int)decimal.Ceiling(position);
        if (lower == upper)
        {
            return ordered[lower];
        }

        var fraction = position - lower;
        return ordered[lower] + (ordered[upper] - ordered[lower]) * fraction;
    }

    private sealed record Observation(
        string ItemId,
        DateTimeOffset ObservedAtUtc,
        int Price,
        int Quantity,
        bool IsOcrAnomaly);

    private sealed record Transition(Observation Previous, Observation Current);

    private sealed record ItemMedian(string ItemId, decimal Value);
}
