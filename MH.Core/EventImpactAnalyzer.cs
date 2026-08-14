using System.Text.Json.Serialization;
using MH.Core.Models;

namespace MH.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventImpactPhase
{
    Before = 0,
    During = 1,
    After = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventImpactAvailability
{
    NotStarted = 0,
    Partial = 1,
    Available = 2,
    InsufficientData = 3
}

public sealed record EventImpactPhaseResult(
    EventImpactPhase Phase,
    DateTimeOffset RequestedStartUtc,
    DateTimeOffset RequestedEndUtc,
    DateTimeOffset? EffectiveObservedStartUtc,
    DateTimeOffset? EffectiveObservedEndUtc,
    bool WindowComplete,
    EventImpactAvailability Availability,
    string? UnavailableReason,
    int RawBarCount,
    int OcrExcludedCount,
    int PriceInlierCount,
    decimal? PriceMad,
    decimal? RobustMedianPrice,
    int VolumeSampleCount,
    decimal? VisibleSupplyMedian,
    decimal? PriceChangeVsBefore,
    decimal? VisibleSupplyChangeVsBefore,
    string? PriceComparisonUnavailableReason,
    string? VisibleSupplyComparisonUnavailableReason);

public sealed record EventImpactAnalysis(
    Event Event,
    DateTimeOffset AsOfUtc,
    int WindowDays,
    EventImpactPhaseResult Before,
    EventImpactPhaseResult During,
    EventImpactPhaseResult After);

public static class EventImpactAnalyzer
{
    public const int DefaultWindowDays = 7;
    public const int MinimumWindowDays = 3;
    public const int MaximumWindowDays = 30;
    private const int MinimumSampleCount = 3;
    private const decimal MadMultiplier = 3m;

    public static EventImpactAnalysis Analyze(
        Event marketEvent,
        IEnumerable<PriceBar> dailyBars,
        DateTimeOffset asOfUtc,
        int windowDays = DefaultWindowDays)
    {
        ArgumentNullException.ThrowIfNull(marketEvent);
        ArgumentNullException.ThrowIfNull(dailyBars);
        ValidateWindowDays(windowDays);

        var normalizedEvent = NormalizeEvent(marketEvent);
        if (normalizedEvent.EndsAtUtc <= normalizedEvent.StartsAtUtc)
        {
            throw new ArgumentException("Event EndsAtUtc must be later than StartsAtUtc.", nameof(marketEvent));
        }

        var cutoffUtc = asOfUtc.ToUniversalTime();
        var completedBars = NormalizeBars(dailyBars, cutoffUtc);
        var before = BuildPhase(
            EventImpactPhase.Before,
            normalizedEvent.StartsAtUtc.AddDays(-windowDays),
            normalizedEvent.StartsAtUtc,
            cutoffUtc,
            completedBars);
        var during = BuildPhase(
            EventImpactPhase.During,
            normalizedEvent.StartsAtUtc,
            normalizedEvent.EndsAtUtc,
            cutoffUtc,
            completedBars);
        var after = BuildPhase(
            EventImpactPhase.After,
            normalizedEvent.EndsAtUtc,
            normalizedEvent.EndsAtUtc.AddDays(windowDays),
            cutoffUtc,
            completedBars);

        return new EventImpactAnalysis(
            normalizedEvent,
            cutoffUtc,
            windowDays,
            before,
            ApplyComparison(during, before),
            ApplyComparison(after, before));
    }

    public static void ValidateWindowDays(int windowDays)
    {
        if (windowDays is < MinimumWindowDays or > MaximumWindowDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowDays),
                windowDays,
                $"windowDays must be between {MinimumWindowDays} and {MaximumWindowDays}.");
        }
    }

    private static Event NormalizeEvent(Event marketEvent)
        => new()
        {
            Id = marketEvent.Id,
            ServerId = marketEvent.ServerId,
            ItemId = marketEvent.ItemId,
            Type = marketEvent.Type,
            Label = marketEvent.Label,
            StartsAtUtc = marketEvent.StartsAtUtc.ToUniversalTime(),
            EndsAtUtc = marketEvent.EndsAtUtc.ToUniversalTime(),
            CatalogKind = marketEvent.CatalogKind
        };

    private static IReadOnlyList<NormalizedBar> NormalizeBars(
        IEnumerable<PriceBar> dailyBars,
        DateTimeOffset cutoffUtc)
    {
        var normalized = new List<NormalizedBar>();
        var seen = new HashSet<(DateTimeOffset StartUtc, DateTimeOffset EndUtc)>();
        foreach (var bar in dailyBars)
        {
            var startUtc = bar.StartUtc.ToUniversalTime();
            var endUtc = bar.EndUtc.ToUniversalTime();
            if (endUtc <= startUtc || endUtc - startUtc != TimeSpan.FromDays(1))
            {
                throw new ArgumentException("Event impact input must contain complete one-day bars.", nameof(dailyBars));
            }

            if (bar.Open <= 0 || bar.High <= 0 || bar.Low <= 0 || bar.Close <= 0
                || bar.Volume < 0
                || bar.High < Math.Max(bar.Open, Math.Max(bar.Low, bar.Close))
                || bar.Low > Math.Min(bar.Open, Math.Min(bar.High, bar.Close)))
            {
                throw new ArgumentException("Event impact input contains an invalid price bar.", nameof(dailyBars));
            }

            if (!seen.Add((startUtc, endUtc)))
            {
                throw new ArgumentException("Event impact input contains duplicate daily bars.", nameof(dailyBars));
            }

            if (endUtc <= cutoffUtc)
            {
                normalized.Add(new NormalizedBar(startUtc, endUtc, bar.Close, bar.Volume, bar.HasOcrAnomaly));
            }
        }

        return normalized.OrderBy(bar => bar.StartUtc).ToArray();
    }

    private static EventImpactPhaseResult BuildPhase(
        EventImpactPhase phase,
        DateTimeOffset requestedStartUtc,
        DateTimeOffset requestedEndUtc,
        DateTimeOffset cutoffUtc,
        IReadOnlyList<NormalizedBar> completedBars)
    {
        var selectedBars = completedBars
            .Where(bar => bar.StartUtc >= requestedStartUtc
                && bar.EndUtc <= requestedEndUtc
                && bar.EndUtc <= cutoffUtc)
            .OrderBy(bar => bar.StartUtc)
            .ToArray();
        var statistics = CalculateStatistics(selectedBars);
        var windowComplete = cutoffUtc >= requestedEndUtc;
        var availability = !windowComplete
            ? cutoffUtc < requestedStartUtc ? EventImpactAvailability.NotStarted : EventImpactAvailability.Partial
            : statistics.RobustMedianPrice.HasValue && statistics.VisibleSupplyMedian.HasValue
                ? EventImpactAvailability.Available
                : EventImpactAvailability.InsufficientData;
        var reason = availability switch
        {
            EventImpactAvailability.NotStarted => "phase-not-started",
            EventImpactAvailability.Partial => "phase-in-progress",
            EventImpactAvailability.InsufficientData => BuildInsufficientReason(statistics),
            _ => null
        };

        return new EventImpactPhaseResult(
            phase,
            requestedStartUtc,
            requestedEndUtc,
            selectedBars.Length == 0 ? null : selectedBars[0].StartUtc,
            selectedBars.Length == 0 ? null : selectedBars[^1].EndUtc,
            windowComplete,
            availability,
            reason,
            selectedBars.Length,
            selectedBars.Count(bar => bar.HasOcrAnomaly),
            statistics.PriceInlierCount,
            statistics.PriceMad,
            statistics.RobustMedianPrice,
            statistics.VolumeSampleCount,
            statistics.VisibleSupplyMedian,
            null,
            null,
            null,
            null);
    }

    private static EventImpactPhaseResult ApplyComparison(
        EventImpactPhaseResult phase,
        EventImpactPhaseResult before)
    {
        decimal? priceChange = null;
        string? priceReason = null;
        if (before.RobustMedianPrice is not decimal beforePrice)
        {
            priceReason = "baseline-price-unavailable";
        }
        else if (phase.RobustMedianPrice is not decimal phasePrice)
        {
            priceReason = "phase-price-unavailable";
        }
        else
        {
            priceChange = phasePrice / beforePrice - 1m;
        }

        decimal? supplyChange = null;
        string? supplyReason = null;
        if (before.VisibleSupplyMedian is not decimal beforeSupply || beforeSupply == 0m)
        {
            supplyReason = "baseline-visible-supply-unavailable";
        }
        else if (phase.VisibleSupplyMedian is not decimal phaseSupply)
        {
            supplyReason = "phase-visible-supply-unavailable";
        }
        else
        {
            supplyChange = phaseSupply / beforeSupply - 1m;
        }

        return phase with
        {
            PriceChangeVsBefore = priceChange,
            VisibleSupplyChangeVsBefore = supplyChange,
            PriceComparisonUnavailableReason = priceReason,
            VisibleSupplyComparisonUnavailableReason = supplyReason
        };
    }

    private static PhaseStatistics CalculateStatistics(IReadOnlyList<NormalizedBar> bars)
    {
        var priceBars = bars.Where(bar => !bar.HasOcrAnomaly).ToArray();
        var priceValues = priceBars.Select(bar => (decimal)bar.Close).ToArray();
        decimal? priceMad = null;
        var priceInlierCount = 0;
        decimal? robustMedianPrice = null;
        if (priceValues.Length >= MinimumSampleCount)
        {
            var median = Median(priceValues);
            priceMad = Median(priceValues.Select(value => Math.Abs(value - median)));
            var inlierValues = priceMad == 0m
                ? priceValues.Where(value => value == median).ToArray()
                : priceValues.Where(value => Math.Abs(value - median) <= MadMultiplier * priceMad.Value).ToArray();
            priceInlierCount = inlierValues.Length;
            if (priceInlierCount >= MinimumSampleCount)
            {
                robustMedianPrice = Median(inlierValues);
            }
        }

        var volumeSampleCount = bars.Count;
        var visibleSupplyMedian = volumeSampleCount >= MinimumSampleCount
            ? Median(bars.Select(bar => (decimal)bar.Volume))
            : (decimal?)null;
        return new PhaseStatistics(
            priceInlierCount,
            priceMad,
            robustMedianPrice,
            volumeSampleCount,
            visibleSupplyMedian);
    }

    private static string BuildInsufficientReason(PhaseStatistics statistics)
    {
        var reasons = new List<string>(2);
        if (!statistics.RobustMedianPrice.HasValue)
        {
            reasons.Add("price-inliers<3");
        }

        if (!statistics.VisibleSupplyMedian.HasValue)
        {
            reasons.Add("visible-supply-samples<3");
        }

        return $"insufficient:{string.Join(",", reasons)}";
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2m
            : ordered[middle];
    }

    private sealed record NormalizedBar(
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        int Close,
        int Volume,
        bool HasOcrAnomaly);

    private sealed record PhaseStatistics(
        int PriceInlierCount,
        decimal? PriceMad,
        decimal? RobustMedianPrice,
        int VolumeSampleCount,
        decimal? VisibleSupplyMedian);
}
