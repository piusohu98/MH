using MH.Core.Models;

namespace MH.Core;

public static class PriceBarAggregator
{
    public static IReadOnlyList<PriceBar> Aggregate(
        IEnumerable<ListingObservation> observations,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var from = fromUtc?.ToUniversalTime();
        var to = toUtc?.ToUniversalTime();

        return observations
            .Where(x => (!from.HasValue || x.ObservedAtUtc >= from.Value)
                && (!to.HasValue || x.ObservedAtUtc <= to.Value))
            .GroupBy(x => x.ObservedAtUtc.UtcDateTime.Date)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var ordered = group.OrderBy(x => x.ObservedAtUtc).ToArray();
                var startUtc = new DateTimeOffset(group.Key, TimeSpan.Zero);
                return new PriceBar(
                    startUtc,
                    startUtc.AddDays(1),
                    ordered[0].Price,
                    ordered.Max(x => x.Price),
                    ordered.Min(x => x.Price),
                    ordered[^1].Price,
                    ordered.Sum(x => x.Quantity),
                    ordered.Any(x => x.IsOcrAnomaly));
            })
            .ToArray();
    }
}
