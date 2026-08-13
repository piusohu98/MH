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
    int InlierCount30Days);

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
            .Select(bar => new CompletedBar(
                DateOnly.FromDateTime(bar.StartUtc.ToUniversalTime().UtcDateTime),
                bar.EndUtc.ToUniversalTime(),
                bar.Close))
            .Where(bar => bar.EndUtc <= cutoffUtc)
            .ToArray();

        var sevenDays = AnalyzeWindow(completedBars, cutoffDate, 7);
        var thirtyDays = AnalyzeWindow(completedBars, cutoffDate, 30);

        return new RobustMarketIndicators(
            cutoffUtc,
            sevenDays.Median,
            thirtyDays.Median,
            sevenDays.Mad,
            thirtyDays.Mad,
            sevenDays.SampleCount,
            thirtyDays.SampleCount,
            sevenDays.InlierCount,
            thirtyDays.InlierCount);
    }

    private static WindowResult AnalyzeWindow(
        IReadOnlyList<CompletedBar> bars,
        DateOnly cutoffDate,
        int windowDays)
    {
        var firstDate = cutoffDate.AddDays(-windowDays);
        var values = bars
            .Where(bar => bar.StartDate >= firstDate && bar.StartDate < cutoffDate)
            .Select(bar => (decimal)bar.Close)
            .ToArray();

        if (values.Length < MinimumSampleCount)
        {
            return new WindowResult(null, null, values.Length, 0);
        }

        var median = Median(values);
        var mad = Median(values.Select(value => Math.Abs(value - median)));
        var inliers = mad == 0m
            ? values.Where(value => value == median).ToArray()
            : values.Where(value => Math.Abs(value - median) <= MadMultiplier * mad).ToArray();
        var robustMedian = inliers.Length >= MinimumSampleCount ? Median(inliers) : null;

        return new WindowResult(robustMedian, mad, values.Length, inliers.Length);
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2m
            : ordered[middle];
    }

    private sealed record CompletedBar(DateOnly StartDate, DateTimeOffset EndUtc, int Close);

    private sealed record WindowResult(
        decimal? Median,
        decimal? Mad,
        int SampleCount,
        int InlierCount);
}
