using MH.Core.Recommendations;

namespace MH.Core.Backtesting;

public sealed record RollingBacktestParameters(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    decimal InitialCapital,
    decimal TradingCostRate,
    decimal SlippageRate,
    string RuleVersion = RecommendationRule.RuleVersion)
{
    public void Validate()
    {
        if (StartUtc == default || EndUtc == default || StartUtc.ToUniversalTime() >= EndUtc.ToUniversalTime())
        {
            throw new ArgumentOutOfRangeException(nameof(StartUtc), "回测区间必须是非空且 StartUtc 早于 EndUtc。");
        }

        if (InitialCapital <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialCapital), "初始资金必须大于零。");
        }

        if (TradingCostRate < 0m || TradingCostRate >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(TradingCostRate), "交易成本率必须位于 [0, 1) 内。");
        }

        if (SlippageRate < 0m || SlippageRate >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(SlippageRate), "滑点率必须位于 [0, 1) 内。");
        }

        if (TradingCostRate + SlippageRate >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(SlippageRate), "交易成本率与滑点率之和必须小于 1。");
        }

        if (!string.Equals(RuleVersion, RecommendationRule.RuleVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("回测只支持当前 recommendation-rules-v1。", nameof(RuleVersion));
        }
    }
}

public sealed record RollingBacktestRecord(
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
    decimal? EquityAfterExecution,
    IReadOnlyList<RecommendationReason> Reasons);

public sealed record RollingBacktestResult(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    decimal InitialCapital,
    decimal TradingCostRate,
    decimal SlippageRate,
    string RuleVersion,
    decimal FinalEquity,
    decimal TotalReturn,
    decimal MaxDrawdown,
    decimal Turnover,
    int DecisionCount,
    int TradeCount,
    decimal FinalCash,
    int FinalPositionQuantity,
    IReadOnlyList<RollingBacktestRecord> Records);
