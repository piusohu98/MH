using MH.Core;
using MH.Core.Models;
using MH.Core.Recommendations;
using MH.Core.Backtesting;

namespace MH.Tests;

public sealed class RollingBacktestTests
{
    private static readonly DateTimeOffset StartUtc = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UsesOnlyPastBarsAndSkipsTheFinalSignalWithoutNextBar()
    {
        var bars = TradeScenario();
        var parameters = Parameters();
        var baseline = RollingBacktest.Run(bars, parameters);
        var withFutureBar = RollingBacktest.Run(bars.Append(Bar(5, 10_000, 10_000)), parameters);

        Assert.Equal(baseline.DecisionCount, withFutureBar.DecisionCount);
        Assert.Equal(baseline.TradeCount, withFutureBar.TradeCount);
        Assert.Equal(RecordSnapshot(baseline.Records), RecordSnapshot(withFutureBar.Records));
        Assert.False(baseline.Records[^1].Executed);
        Assert.Null(baseline.Records[^1].ExecutionAtUtc);
    }

    [Fact]
    public void FutureBarsCannotChangeHistoryBeforeTheNewExecutionOpportunity()
    {
        var baseline = RollingBacktest.Run(
            TradeScenario(),
            Parameters() with { EndUtc = StartUtc.AddDays(7) });
        var extendedBars = TradeScenario()
            .Append(Bar(5, 1_000_000, 1_000_000))
            .Append(Bar(6, 1, 1));
        var extended = RollingBacktest.Run(
            extendedBars,
            Parameters() with { EndUtc = StartUtc.AddDays(7) });

        var prefixLength = baseline.Records.Count - 1;
        Assert.True(prefixLength > 0);
        Assert.Equal(
            RecordSnapshot(baseline.Records.Take(prefixLength)),
            RecordSnapshot(extended.Records.Take(prefixLength)));
        Assert.False(baseline.Records[^1].Executed);
        Assert.True(extended.Records[baseline.Records.Count - 1].Executed);
    }

    [Fact]
    public void RebalancesAfterAnOvernightGapWithoutExceedingThePositionCap()
    {
        var parameters = Parameters(0.02m, 0.01m) with { EndUtc = StartUtc.AddDays(6) };
        var result = RollingBacktest.Run(OvernightGapRebalanceScenario(), parameters);
        var executed = result.Records.Where(record => record.Executed).ToArray();

        Assert.True(executed.Count(record => record.QuantityDelta > 0) >= 2);
        Assert.All(executed, record =>
        {
            var referencePrice = record.QuantityDelta > 0
                ? record.ExecutionPrice!.Value / (1m + parameters.SlippageRate)
                : record.ExecutionPrice!.Value / (1m - parameters.SlippageRate);
            var actualPositionRatio = record.PositionQuantityAfter * referencePrice
                / record.EquityAfterExecution!.Value;

            Assert.InRange(actualPositionRatio, 0m, RecommendationRule.MaxSuggestedPositionCap);
            Assert.True(record.CashAfter >= 0m);
            Assert.True(record.PositionQuantityAfter >= 0);
        });
    }

    [Fact]
    public void SameInputProducesACompletelyDeterministicResult()
    {
        var parameters = Parameters();
        var first = RollingBacktest.Run(TradeScenario(), parameters);
        var second = RollingBacktest.Run(TradeScenario(), parameters);

        Assert.Equal(first.StartUtc, second.StartUtc);
        Assert.Equal(first.EndUtc, second.EndUtc);
        Assert.Equal(first.FinalEquity, second.FinalEquity);
        Assert.Equal(first.TotalReturn, second.TotalReturn);
        Assert.Equal(first.MaxDrawdown, second.MaxDrawdown);
        Assert.Equal(first.Turnover, second.Turnover);
        Assert.Equal(first.DecisionCount, second.DecisionCount);
        Assert.Equal(first.TradeCount, second.TradeCount);
        Assert.Equal(RecordSnapshot(first.Records), RecordSnapshot(second.Records));
    }

    [Fact]
    public void CostsAndSlippageDoNotIncreaseFinalEquity()
    {
        var noCost = RollingBacktest.Run(TradeScenario(), Parameters());
        var withCost = RollingBacktest.Run(TradeScenario(), Parameters(0.02m, 0.01m));

        Assert.True(withCost.FinalEquity <= noCost.FinalEquity);
        Assert.True(withCost.TotalReturn <= noCost.TotalReturn);
        Assert.True(withCost.Records.Sum(x => x.TradingCost) > 0m);
        Assert.True(withCost.Records.Sum(x => x.SlippageCost) > 0m);
        Assert.All(withCost.Records, record =>
        {
            Assert.True(record.CashAfter >= 0m);
            Assert.True(record.PositionQuantityAfter >= 0);
        });
    }

    [Fact]
    public void CalculatesReproducibleReturnDrawdownAndTurnover()
    {
        var result = RollingBacktest.Run(TradeScenario(), Parameters());

        Assert.Equal(10_000m, result.InitialCapital);
        Assert.Equal(9_820m, result.FinalEquity);
        Assert.Equal(-0.018m, result.TotalReturn);
        Assert.Equal(0.018m, result.MaxDrawdown);
        Assert.Equal(0.09m, result.Turnover);
        Assert.Equal(5, result.DecisionCount);
        Assert.Equal(1, result.TradeCount);
        Assert.Equal(RecommendationRule.RuleVersion, result.RuleVersion);
    }

    [Fact]
    public void FinalEquityUsesTheLastCompletedBarInsideTheBacktestInterval()
    {
        var result = RollingBacktest.Run(
            TradeScenario().Append(Bar(5, 1, 1_000_000)),
            Parameters());

        Assert.Equal(
            result.FinalCash + result.FinalPositionQuantity * TradeScenario()[^1].Close,
            result.FinalEquity);
    }

    [Fact]
    public void EmptyOrInsufficientDataProducesNoTrades()
    {
        var parameters = Parameters();
        var empty = RollingBacktest.Run([], parameters);
        var insufficient = RollingBacktest.Run([TradeScenario()[0]], parameters with
        {
            EndUtc = TradeScenario()[0].EndUtc
        });

        Assert.Equal(0, empty.DecisionCount);
        Assert.Equal(0, empty.TradeCount);
        Assert.Equal(parameters.InitialCapital, empty.FinalEquity);
        Assert.Equal(1, insufficient.DecisionCount);
        Assert.Equal(0, insufficient.TradeCount);
        Assert.Equal(parameters.InitialCapital, insufficient.FinalEquity);
    }

    [Fact]
    public void ValidatesBacktestParameterBoundariesAndVersion()
    {
        var parameters = Parameters();

        Assert.Throws<ArgumentOutOfRangeException>(() => (parameters with { StartUtc = parameters.EndUtc }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (parameters with { InitialCapital = 0m }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (parameters with { TradingCostRate = -0.01m }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (parameters with { SlippageRate = 1m }).Validate());
        Assert.Throws<ArgumentException>(() => (parameters with { RuleVersion = "other-rule" }).Validate());
    }

    private static RollingBacktestParameters Parameters(
        decimal tradingCostRate = 0m,
        decimal slippageRate = 0m)
        => new(
            StartUtc,
            StartUtc.AddDays(5),
            10_000m,
            tradingCostRate,
            slippageRate,
            RecommendationRule.RuleVersion);

    private static IReadOnlyList<PriceBar> TradeScenario()
        =>
        [
            Bar(0, 100, 100),
            Bar(1, 100, 110),
            Bar(2, 100, 101),
            Bar(3, 100, 112),
            Bar(4, 100, 80)
        ];

    private static IReadOnlyList<PriceBar> OvernightGapRebalanceScenario()
        =>
        [
            Bar(0, 100, 100),
            Bar(1, 100, 150),
            Bar(2, 100, 200),
            Bar(3, 100, 250),
            Bar(4, 100, 300),
            Bar(5, 20, 25)
        ];

    private static PriceBar Bar(int day, int open, int close)
    {
        var start = StartUtc.AddDays(day);
        var high = Math.Max(open, close);
        var low = Math.Min(open, close);
        return new PriceBar(start, start.AddDays(1), open, high, low, close, 10, false);
    }

    private static IEnumerable<(
        DateTimeOffset DecisionAtUtc,
        DateTimeOffset? ExecutionAtUtc,
        RecommendationAction Action,
        int DirectionScore,
        decimal Confidence,
        decimal RecommendationMaxPosition,
        decimal AppliedTargetPosition,
        bool Executed,
        int QuantityDelta,
        decimal? ExecutionPrice,
        decimal TradingCost,
        decimal SlippageCost,
        decimal CashAfter,
        int PositionQuantityAfter,
        decimal EquityAtDecision,
        decimal? EquityAfterExecution)> RecordSnapshot(IEnumerable<RollingBacktestRecord> records)
        => records.Select(record => (
            record.DecisionAtUtc,
            record.ExecutionAtUtc,
            record.Action,
            record.DirectionScore,
            record.Confidence,
            record.RecommendationMaxPosition,
            record.AppliedTargetPosition,
            record.Executed,
            record.QuantityDelta,
            record.ExecutionPrice,
            record.TradingCost,
            record.SlippageCost,
            record.CashAfter,
            record.PositionQuantityAfter,
            record.EquityAtDecision,
            record.EquityAfterExecution));
}
