using MH.Core.Models;
using MH.Core.Recommendations;

namespace MH.Core.Backtesting;

public static class RollingBacktest
{
    public static RollingBacktestResult Run(
        IEnumerable<PriceBar> dailyBars,
        RollingBacktestParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(dailyBars);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();

        var startUtc = parameters.StartUtc.ToUniversalTime();
        var endUtc = parameters.EndUtc.ToUniversalTime();
        var bars = dailyBars
            .Select(NormalizeAndValidateBar)
            .OrderBy(bar => bar.StartUtc)
            .ThenBy(bar => bar.EndUtc)
            .ToArray();

        var cash = parameters.InitialCapital;
        var positionQuantity = 0;
        var peakEquity = parameters.InitialCapital;
        var maxDrawdown = 0m;
        var turnover = 0m;
        var tradeCount = 0;
        var records = new List<RollingBacktestRecord>();
        PriceBar? lastDecisionBar = null;

        for (var index = 0; index < bars.Length; index++)
        {
            var decisionBar = bars[index];
            if (decisionBar.EndUtc < startUtc || decisionBar.EndUtc > endUtc)
            {
                continue;
            }

            lastDecisionBar = decisionBar;
            var decisionAtUtc = decisionBar.EndUtc;
            var historicalBars = bars
                .Take(index + 1)
                .Where(bar => bar.EndUtc <= decisionAtUtc)
                .ToArray();
            var indicators = RobustMarketAnalyzer.Analyze(historicalBars, decisionAtUtc);
            var decision = RecommendationRule.Evaluate(indicators, decisionAtUtc);
            var equityAtDecision = MarkToMarket(cash, positionQuantity, decisionBar.Close);
            UpdateDrawdown(equityAtDecision, ref peakEquity, ref maxDrawdown);

            var nextBar = index + 1 < bars.Length
                && bars[index + 1].EndUtc <= endUtc
                && bars[index + 1].StartUtc >= decisionAtUtc
                ? bars[index + 1]
                : null;
            var currentPositionRatio = equityAtDecision <= 0m
                ? 0m
                : positionQuantity * (decimal)decisionBar.Close / equityAtDecision;
            var appliedTargetPosition = GetAppliedTargetPosition(decision, currentPositionRatio);
            var execution = nextBar is null
                ? ExecutionResult.NotExecuted(cash, positionQuantity)
                : ExecuteTarget(
                    decision,
                    nextBar,
                    cash,
                    positionQuantity,
                    parameters.TradingCostRate,
                    parameters.SlippageRate,
                    appliedTargetPosition);

            cash = execution.CashAfter;
            positionQuantity = execution.PositionQuantityAfter;
            if (execution.Executed)
            {
                tradeCount++;
                turnover += execution.ReferenceNotional / parameters.InitialCapital;
                UpdateDrawdown(execution.EquityAfterExecution, ref peakEquity, ref maxDrawdown);
            }

            EnsureNonNegativeState(cash, positionQuantity);
            records.Add(new RollingBacktestRecord(
                decisionAtUtc,
                execution.ExecutionAtUtc,
                decision.Action,
                decision.DirectionScore,
                decision.Confidence,
                decision.MaxSuggestedPosition,
                appliedTargetPosition,
                execution.Executed,
                execution.QuantityDelta,
                execution.ExecutionPrice,
                execution.TradingCost,
                execution.SlippageCost,
                cash,
                positionQuantity,
                equityAtDecision,
                execution.EquityAfterExecutionNullable,
                decision.Reasons));
        }

        var finalEquity = lastDecisionBar is null
            ? parameters.InitialCapital
            : MarkToMarket(cash, positionQuantity, lastDecisionBar.Close);
        UpdateDrawdown(finalEquity, ref peakEquity, ref maxDrawdown);

        return new RollingBacktestResult(
            startUtc,
            endUtc,
            parameters.InitialCapital,
            parameters.TradingCostRate,
            parameters.SlippageRate,
            parameters.RuleVersion,
            finalEquity,
            finalEquity / parameters.InitialCapital - 1m,
            maxDrawdown,
            turnover,
            records.Count,
            tradeCount,
            cash,
            positionQuantity,
            records.ToArray());
    }

    private static PriceBar NormalizeAndValidateBar(PriceBar bar)
    {
        var startUtc = bar.StartUtc.ToUniversalTime();
        var endUtc = bar.EndUtc.ToUniversalTime();
        if (startUtc >= endUtc)
        {
            throw new ArgumentException("日线的 EndUtc 必须晚于 StartUtc。", nameof(bar));
        }

        if (bar.Open <= 0 || bar.High <= 0 || bar.Low <= 0 || bar.Close <= 0 || bar.Volume < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bar), "日线价格必须大于零，数量不能为负数。");
        }

        return bar with { StartUtc = startUtc, EndUtc = endUtc };
    }

    private static decimal GetAppliedTargetPosition(
        RecommendationDecision decision,
        decimal currentPositionRatio)
        => decision.Action switch
        {
            RecommendationAction.CandidateBuy or RecommendationAction.CandidateSell
                => decision.MaxSuggestedPosition,
            RecommendationAction.Avoid or RecommendationAction.DataInsufficient => 0m,
            _ => currentPositionRatio
        };

    private static ExecutionResult ExecuteTarget(
        RecommendationDecision decision,
        PriceBar nextBar,
        decimal cash,
        int positionQuantity,
        decimal tradingCostRate,
        decimal slippageRate,
        decimal appliedTargetPosition)
    {
        var shouldRebalance = decision.Action is RecommendationAction.CandidateBuy
            or RecommendationAction.CandidateSell
            or RecommendationAction.Avoid
            or RecommendationAction.DataInsufficient;
        if (!shouldRebalance)
        {
            return ExecutionResult.NotExecuted(cash, positionQuantity);
        }

        var referencePrice = nextBar.Open;
        var executionOpenEquity = MarkToMarket(cash, positionQuantity, referencePrice);
        var desiredQuantity = CalculateTargetQuantity(
            decision,
            appliedTargetPosition,
            executionOpenEquity,
            cash,
            positionQuantity,
            referencePrice,
            tradingCostRate,
            slippageRate);
        var quantityDelta = desiredQuantity - positionQuantity;
        if (quantityDelta > 0)
        {
            var executionPrice = referencePrice * (1m + slippageRate);
            var maxAdditionalQuantity = FloorQuantity(cash, executionPrice * (1m + tradingCostRate));
            quantityDelta = Math.Min(quantityDelta, maxAdditionalQuantity);
            desiredQuantity = positionQuantity + quantityDelta;
        }

        if (quantityDelta == 0)
        {
            return ExecutionResult.NotExecuted(cash, positionQuantity);
        }

        var buying = quantityDelta > 0;
        var quantity = Math.Abs(quantityDelta);
        var executionPriceFinal = referencePrice * (buying ? 1m + slippageRate : 1m - slippageRate);
        var executionNotional = quantity * executionPriceFinal;
        var referenceNotional = quantity * referencePrice;
        var tradingCost = executionNotional * tradingCostRate;
        var slippageCost = referenceNotional * slippageRate;
        var cashAfter = buying
            ? cash - executionNotional - tradingCost
            : cash + executionNotional - tradingCost;
        var positionAfter = desiredQuantity;
        var equityAfterExecution = MarkToMarket(cashAfter, positionAfter, referencePrice);

        return new ExecutionResult(
            true,
            nextBar.StartUtc,
            quantityDelta,
            executionPriceFinal,
            tradingCost,
            slippageCost,
            cashAfter,
            positionAfter,
            referenceNotional,
            equityAfterExecution);
    }

    private static int CalculateTargetQuantity(
        RecommendationDecision decision,
        decimal targetPosition,
        decimal executionOpenEquity,
        decimal cash,
        int positionQuantity,
        int referencePrice,
        decimal tradingCostRate,
        decimal slippageRate)
    {
        if (decision.Action is RecommendationAction.CandidateSell
            or RecommendationAction.Avoid
            or RecommendationAction.DataInsufficient)
        {
            return CalculateSellTargetQuantity(
                targetPosition,
                executionOpenEquity,
                positionQuantity,
                referencePrice,
                tradingCostRate,
                slippageRate);
        }

        var buyTarget = CalculateBuyTargetQuantity(
            targetPosition,
            executionOpenEquity,
            cash,
            positionQuantity,
            referencePrice,
            tradingCostRate,
            slippageRate);
        return buyTarget >= positionQuantity
            ? buyTarget
            : CalculateSellTargetQuantity(
                targetPosition,
                executionOpenEquity,
                positionQuantity,
                referencePrice,
                tradingCostRate,
                slippageRate);
    }

    private static int CalculateBuyTargetQuantity(
        decimal targetPosition,
        decimal executionOpenEquity,
        decimal cash,
        int positionQuantity,
        int referencePrice,
        decimal tradingCostRate,
        decimal slippageRate)
    {
        if (targetPosition <= 0m)
        {
            return 0;
        }

        var executionPrice = referencePrice * (1m + slippageRate);
        var equityReductionPerUnit = referencePrice * slippageRate
            + executionPrice * tradingCostRate;
        var numerator = targetPosition
            * (executionOpenEquity + positionQuantity * equityReductionPerUnit);
        var denominator = referencePrice + targetPosition * equityReductionPerUnit;
        var targetQuantity = FloorQuantity(numerator, denominator);
        var maxAdditionalQuantity = FloorQuantity(
            cash,
            executionPrice * (1m + tradingCostRate));
        var affordableTarget = positionQuantity > int.MaxValue - maxAdditionalQuantity
            ? int.MaxValue
            : positionQuantity + maxAdditionalQuantity;
        return Math.Min(targetQuantity, affordableTarget);
    }

    private static int CalculateSellTargetQuantity(
        decimal targetPosition,
        decimal executionOpenEquity,
        int positionQuantity,
        int referencePrice,
        decimal tradingCostRate,
        decimal slippageRate)
    {
        if (targetPosition <= 0m || positionQuantity == 0)
        {
            return 0;
        }

        var executionPrice = referencePrice * (1m - slippageRate);
        var equityReductionPerUnit = referencePrice * slippageRate
            + executionPrice * tradingCostRate;
        var numerator = targetPosition
            * (executionOpenEquity - positionQuantity * equityReductionPerUnit);
        var denominator = referencePrice - targetPosition * equityReductionPerUnit;
        if (numerator <= 0m || denominator <= 0m)
        {
            return 0;
        }

        return Math.Min(positionQuantity, FloorQuantity(numerator, denominator));
    }

    private static int FloorQuantity(decimal notional, decimal price)
    {
        if (notional <= 0m || price <= 0m)
        {
            return 0;
        }

        var quantity = decimal.Floor(notional / price);
        return quantity >= int.MaxValue ? int.MaxValue : (int)quantity;
    }

    private static decimal MarkToMarket(decimal cash, int positionQuantity, int price)
        => cash + positionQuantity * (decimal)price;

    private static void UpdateDrawdown(decimal equity, ref decimal peakEquity, ref decimal maxDrawdown)
    {
        if (equity > peakEquity)
        {
            peakEquity = equity;
            return;
        }

        if (peakEquity > 0m)
        {
            maxDrawdown = Math.Max(maxDrawdown, 1m - equity / peakEquity);
        }
    }

    private static void EnsureNonNegativeState(decimal cash, int positionQuantity)
    {
        if (cash < 0m || positionQuantity < 0)
        {
            throw new InvalidOperationException("回测状态不能出现负现金或负持仓。");
        }
    }

    private sealed record ExecutionResult(
        bool Executed,
        DateTimeOffset? ExecutionAtUtc,
        int QuantityDelta,
        decimal? ExecutionPrice,
        decimal TradingCost,
        decimal SlippageCost,
        decimal CashAfter,
        int PositionQuantityAfter,
        decimal ReferenceNotional,
        decimal EquityAfterExecution)
    {
        public static ExecutionResult NotExecuted(decimal cash, int positionQuantity)
            => new(false, null, 0, null, 0m, 0m, cash, positionQuantity, 0m, 0m);

        public decimal? EquityAfterExecutionNullable => Executed ? EquityAfterExecution : null;
    }
}
