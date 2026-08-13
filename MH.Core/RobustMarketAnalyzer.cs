using MH.Core.Models;

namespace MH.Core;

public sealed record RobustMarketIndicators(
    DateTimeOffset CutoffUtc,
    decimal? RobustMedian7Days,
    decimal? RobustMedian30Days,
    decimal? Mad7Days,
    decimal? Mad30Days,
    int SampleCount7Days,
    int SampleCount30Days,
    int InlierCount7Days,
    int InlierCount30Days,
    decimal? Return7Days,
    decimal? Return30Days,
    decimal? Ewma7Days,
    decimal? Ewma30Days,
    decimal? Volatility7Days,
    decimal? Volatility30Days,
    decimal? VisibleSupplyChange7Days,
    decimal? VisibleSupplyChange30Days,
    decimal? DataAgeHours);

public static class RobustMarketAnalyzer
{
    private const int MinimumSampleCount = 3;
    private const decimal MadMultiplier = 3m;

    public static RobustMarketIndicators Analyze(
        IEnumerable<PriceBar> dailyBars,
        DateTimeOffset decisionAtUtc)
    {
        ArgumentNullException.ThrowIfNull(dailyBars);

        var cutoffUtc = decisionAtUtc.ToUniversalTime();
        var cutoffDate = DateOnly.FromDateTime(cutoffUtc.UtcDateTime);
        var completedBars = dailyBars
            .Select(bar =>
            {
                var startUtc = bar.StartUtc.ToUniversalTime();
                return new CompletedBar(
                    startUtc,
                    DateOnly.FromDateTime(startUtc.UtcDateTime),
                    bar.EndUtc.ToUniversalTime(),
                    bar.Close,
                    bar.Volume);
            })
            .Where(bar => bar.EndUtc <= cutoffUtc)
            .ToArray();

        var sevenDays = AnalyzeWindow(completedBars, cutoffDate, 7);
        var thirtyDays = AnalyzeWindow(completedBars, cutoffDate, 30);
        decimal? dataAgeHours = completedBars.Length == 0
            ? null
            : (decimal)(cutoffUtc - completedBars.Max(bar => bar.EndUtc)).Ticks / TimeSpan.TicksPerHour;

        return new RobustMarketIndicators(
            cutoffUtc,
            sevenDays.Median,
            thirtyDays.Median,
            sevenDays.Mad,
            thirtyDays.Mad,
            sevenDays.SampleCount,
            thirtyDays.SampleCount,
            sevenDays.InlierCount,
            thirtyDays.InlierCount,
            sevenDays.Return,
            thirtyDays.Return,
            sevenDays.Ewma,
            thirtyDays.Ewma,
            sevenDays.Volatility,
            thirtyDays.Volatility,
            sevenDays.VisibleSupplyChange,
            thirtyDays.VisibleSupplyChange,
            dataAgeHours);
    }

    private static WindowResult AnalyzeWindow(
        IReadOnlyList<CompletedBar> bars,
        DateOnly cutoffDate,
        int windowDays)
    {
        var firstDate = cutoffDate.AddDays(-windowDays);
        var selectedBars = bars
            .Where(bar => bar.StartDate >= firstDate && bar.StartDate < cutoffDate)
            .ToArray();
        var visibleSupplyChange = CalculateVisibleSupplyChange(selectedBars);
        var values = selectedBars.Select(bar => (decimal)bar.Close).ToArray();

        if (values.Length < MinimumSampleCount)
        {
            return new WindowResult(null, null, values.Length, 0, null, null, null, visibleSupplyChange);
        }

        var median = Median(values);
        var mad = Median(values.Select(value => Math.Abs(value - median)));
        var inliers = mad == 0m
            ? selectedBars.Where(bar => bar.Close == median).ToArray()
            : selectedBars.Where(bar => Math.Abs(bar.Close - median) <= MadMultiplier * mad).ToArray();
        var inlierValues = inliers.Select(bar => (decimal)bar.Close).ToArray();
        decimal? robustMedian = inlierValues.Length >= MinimumSampleCount ? Median(inlierValues) : null;
        if (inliers.Length < MinimumSampleCount)
        {
            return new WindowResult(robustMedian, mad, values.Length, inliers.Length, null, null, null, visibleSupplyChange);
        }

        var metrics = CalculateMetrics(inliers, windowDays);
        return new WindowResult(
            robustMedian,
            mad,
            values.Length,
            inliers.Length,
            metrics.Return,
            metrics.Ewma,
            metrics.Volatility,
            visibleSupplyChange);
    }

    private static decimal? CalculateVisibleSupplyChange(IReadOnlyList<CompletedBar> bars)
    {
        if (bars.Count < MinimumSampleCount)
        {
            return null;
        }

        var orderedBars = bars.OrderBy(bar => bar.StartUtc).ToArray();
        if (orderedBars[0].Volume <= 0)
        {
            return null;
        }

        return (decimal)orderedBars[^1].Volume / orderedBars[0].Volume - 1m;
    }

    private static TrendMetrics CalculateMetrics(
        IReadOnlyList<CompletedBar> inliers,
        int span)
    {
        var orderedInliers = inliers
            .OrderBy(bar => bar.StartUtc)
            .ToArray();
        var firstClose = (decimal)orderedInliers[0].Close;
        var lastClose = (decimal)orderedInliers[^1].Close;
        var simpleReturns = new decimal[orderedInliers.Length - 1];
        for (var index = 1; index < orderedInliers.Length; index++)
        {
            simpleReturns[index - 1] = (decimal)orderedInliers[index].Close / orderedInliers[index - 1].Close - 1m;
        }

        var alpha = 2m / (span + 1);
        var ewma = firstClose;
        for (var index = 1; index < orderedInliers.Length; index++)
        {
            ewma = alpha * orderedInliers[index].Close + (1m - alpha) * ewma;
        }

        var mean = simpleReturns.Sum() / simpleReturns.Length;
        var squaredDeviationSum = 0m;
        foreach (var simpleReturn in simpleReturns)
        {
            var deviation = simpleReturn - mean;
            squaredDeviationSum += deviation * deviation;
        }

        var sampleVariance = squaredDeviationSum / (simpleReturns.Length - 1);
        return new TrendMetrics(
            lastClose / firstClose - 1m,
            ewma,
            SquareRoot(sampleVariance));
    }

    private static decimal SquareRoot(decimal value)
    {
        if (value == 0m)
        {
            return 0m;
        }

        var estimate = value < 1m ? 1m : value;
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var next = (estimate + value / estimate) / 2m;
            if (next == estimate)
            {
                return next;
            }

            estimate = next;
        }

        return estimate;
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2m
            : ordered[middle];
    }

    private sealed record CompletedBar(
        DateTimeOffset StartUtc,
        DateOnly StartDate,
        DateTimeOffset EndUtc,
        int Close,
        int Volume);

    private sealed record WindowResult(
        decimal? Median,
        decimal? Mad,
        int SampleCount,
        int InlierCount,
        decimal? Return,
        decimal? Ewma,
        decimal? Volatility,
        decimal? VisibleSupplyChange);

    private sealed record TrendMetrics(
        decimal Return,
        decimal Ewma,
        decimal Volatility);
}
