using MH.Core;
using MH.Core.Models;

namespace MH.Tests;

public sealed class RobustMarketAnalyzerTests
{
    [Fact]
    public void FiltersMadOutlierAndCalculatesSevenAndThirtyDayMedians()
    {
        var cutoff = Utc(2025, 2, 1);
        var bars = new[]
        {
            Bar(Utc(2025, 1, 5), 80),
            Bar(Utc(2025, 1, 10), 90),
            Bar(Utc(2025, 1, 15), 110),
            Bar(Utc(2025, 1, 26), 100),
            Bar(Utc(2025, 1, 27), 101),
            Bar(Utc(2025, 1, 28), 102),
            Bar(Utc(2025, 1, 29), 1000)
        }.Reverse();

        var result = RobustMarketAnalyzer.Analyze(bars, cutoff);

        Assert.Equal(cutoff, result.CutoffUtc);
        Assert.Equal(4, result.SampleCount7Days);
        Assert.Equal(3, result.InlierCount7Days);
        Assert.Equal(1m, result.Mad7Days);
        Assert.Equal(101m, result.RobustMedian7Days);
        Assert.Equal(7, result.SampleCount30Days);
        Assert.Equal(6, result.InlierCount30Days);
        Assert.Equal(9m, result.Mad30Days);
        Assert.Equal(100.5m, result.RobustMedian30Days);
    }

    [Fact]
    public void MadZeroKeepsOnlyValuesEqualToTheMedian()
    {
        var result = RobustMarketAnalyzer.Analyze(
            new[]
            {
                Bar(Utc(2025, 1, 26), 100),
                Bar(Utc(2025, 1, 27), 100),
                Bar(Utc(2025, 1, 28), 100),
                Bar(Utc(2025, 1, 29), 250)
            },
            Utc(2025, 2, 1));

        Assert.Equal(0m, result.Mad7Days);
        Assert.Equal(3, result.InlierCount7Days);
        Assert.Equal(100m, result.RobustMedian7Days);
        Assert.Equal(0m, result.Mad30Days);
        Assert.Equal(3, result.InlierCount30Days);
        Assert.Equal(100m, result.RobustMedian30Days);
    }

    [Fact]
    public void ReturnsNoMedianWhenAWindowHasTooFewSamples()
    {
        var result = RobustMarketAnalyzer.Analyze(
            new[]
            {
                Bar(Utc(2025, 1, 10), 102),
                Bar(Utc(2025, 1, 30), 100),
                Bar(Utc(2025, 1, 31), 101)
            },
            Utc(2025, 2, 1));

        Assert.Equal(2, result.SampleCount7Days);
        Assert.Equal(0, result.InlierCount7Days);
        Assert.Null(result.Mad7Days);
        Assert.Null(result.RobustMedian7Days);
        Assert.Equal(3, result.SampleCount30Days);
        Assert.Equal(3, result.InlierCount30Days);
        Assert.Equal(1m, result.Mad30Days);
        Assert.Equal(101m, result.RobustMedian30Days);
    }

    [Fact]
    public void ReturnsNoMedianWhenMadFilteringLeavesTooFewInliers()
    {
        var result = RobustMarketAnalyzer.Analyze(
            new[]
            {
                Bar(Utc(2025, 1, 28), 100),
                Bar(Utc(2025, 1, 29), 1000),
                Bar(Utc(2025, 1, 30), 1001)
            },
            Utc(2025, 2, 1));

        Assert.Equal(3, result.SampleCount7Days);
        Assert.Equal(1m, result.Mad7Days);
        Assert.Equal(2, result.InlierCount7Days);
        Assert.Null(result.RobustMedian7Days);
        Assert.Equal(3, result.SampleCount30Days);
        Assert.Equal(1m, result.Mad30Days);
        Assert.Equal(2, result.InlierCount30Days);
        Assert.Null(result.RobustMedian30Days);
    }

    [Fact]
    public void NormalizesOffsetsIncludesCutoffAndIgnoresFutureBars()
    {
        var cutoff = new DateTimeOffset(2025, 2, 1, 8, 0, 0, TimeSpan.FromHours(8));
        var bars = new[]
        {
            Bar(new DateTimeOffset(2025, 1, 29, 8, 0, 0, TimeSpan.FromHours(8)), 100),
            Bar(new DateTimeOffset(2025, 1, 30, 8, 0, 0, TimeSpan.FromHours(8)), 101),
            Bar(new DateTimeOffset(2025, 1, 31, 8, 0, 0, TimeSpan.FromHours(8)), 102),
            Bar(cutoff, 103)
        };
        var futureBar = Bar(new DateTimeOffset(2025, 2, 1, 8, 1, 0, TimeSpan.FromHours(8)), 9999);

        var result = RobustMarketAnalyzer.Analyze(bars, cutoff);
        var resultWithFutureBar = RobustMarketAnalyzer.Analyze(bars.Append(futureBar), cutoff);

        Assert.Equal(cutoff.ToUniversalTime(), result.CutoffUtc);
        Assert.Equal(result, resultWithFutureBar);
        Assert.Equal(4, result.SampleCount7Days);
        Assert.Equal(4, result.InlierCount7Days);
        Assert.Equal(101.5m, result.RobustMedian7Days);
        Assert.Equal(4, result.SampleCount30Days);
        Assert.Equal(101.5m, result.RobustMedian30Days);
    }

    private static PriceBar Bar(DateTimeOffset endUtc, int close)
        => new(endUtc.AddDays(-1), endUtc, close, close, close, close, 1, false);

    private static DateTimeOffset Utc(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, TimeSpan.Zero);
}
