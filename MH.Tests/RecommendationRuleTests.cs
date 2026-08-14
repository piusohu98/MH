using MH.Core;
using MH.Core.Models;
using MH.Core.Recommendations;

namespace MH.Tests;

public sealed class RecommendationRuleTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2025, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReturnsDataInsufficientWhenSamplesAreBelowTheGate()
    {
        var decision = RecommendationRule.Evaluate(
            Indicators(sampleCount7: 2, inlierCount7: 2, return7: null, volatility7: null),
            AsOfUtc);

        Assert.Equal(RecommendationAction.DataInsufficient, decision.Action);
        Assert.Equal(0, decision.DirectionScore);
        Assert.Equal(0m, decision.Confidence);
        Assert.Equal(0m, decision.MaxSuggestedPosition);
        Assert.Contains(decision.Reasons, reason => reason.Code == "sample-insufficient");
    }

    [Fact]
    public void ReturnsDataInsufficientWhenDataIsStaleAtExplicitAsOf()
    {
        var decision = RecommendationRule.Evaluate(
            Indicators(dataAgeHours: 47m),
            AsOfUtc.AddHours(2));

        Assert.Equal(RecommendationAction.DataInsufficient, decision.Action);
        Assert.Contains(decision.Reasons, reason => reason.Code == "stale-data");
        Assert.Equal(0m, decision.MaxSuggestedPosition);
    }

    [Fact]
    public void ProducesCandidateBuyForConsistentUpwardTrend()
    {
        var decision = RecommendationRule.Evaluate(
            Indicators(return7: 0.20m, return30: 0.12m, supply7: -0.30m, supply30: -0.20m),
            AsOfUtc);

        Assert.Equal(RecommendationAction.CandidateBuy, decision.Action);
        Assert.True(decision.DirectionScore > 0);
        Assert.True(decision.Confidence >= 0.5m);
        Assert.True(decision.MaxSuggestedPosition > 0m);
        Assert.Contains(decision.Reasons, reason => reason.Code == "trend-consistent");
        Assert.Contains(decision.Reasons, reason => reason.Code == "supply-contraction");
    }

    [Fact]
    public void ObservesWhenShortAndLongTrendConflict()
    {
        var decision = RecommendationRule.Evaluate(
            Indicators(return7: 0.20m, return30: -0.12m),
            AsOfUtc);

        Assert.Equal(RecommendationAction.Observe, decision.Action);
        Assert.Contains(decision.Reasons, reason => reason.Code == "trend-conflict");
        Assert.InRange(decision.MaxSuggestedPosition, 0m, 0.25m);
    }

    [Fact]
    public void AvoidsHighVolatilityEvenWhenTrendIsPositive()
    {
        var decision = RecommendationRule.Evaluate(
            Indicators(return7: 0.20m, return30: 0.12m, volatility7: 0.35m, volatility30: 0.30m),
            AsOfUtc);

        Assert.Equal(RecommendationAction.Avoid, decision.Action);
        Assert.Equal(0m, decision.MaxSuggestedPosition);
        Assert.Contains(decision.Reasons, reason => reason.Code == "high-volatility");
    }

    [Fact]
    public void SupplyChangeChangesDirectionScoreAndIsExplained()
    {
        var contraction = RecommendationRule.Evaluate(
            Indicators(return7: 0.12m, return30: 0.08m, supply7: -0.50m, supply30: -0.40m),
            AsOfUtc);
        var expansion = RecommendationRule.Evaluate(
            Indicators(return7: 0.12m, return30: 0.08m, supply7: 0.50m, supply30: 0.40m),
            AsOfUtc);

        Assert.True(contraction.DirectionScore > expansion.DirectionScore);
        Assert.Contains(contraction.Reasons, reason => reason.Code == "supply-contraction");
        Assert.Contains(expansion.Reasons, reason => reason.Code == "supply-expansion");
    }

    [Fact]
    public void KeepsScoreConfidenceAndPositionWithinContractBounds()
    {
        var positive = RecommendationRule.Evaluate(
            Indicators(return7: 99m, return30: 99m, supply7: -99m, supply30: -99m),
            AsOfUtc);
        var negative = RecommendationRule.Evaluate(
            Indicators(return7: -99m, return30: -99m, supply7: 99m, supply30: 99m),
            AsOfUtc);

        foreach (var decision in new[] { positive, negative })
        {
            Assert.InRange(decision.DirectionScore, -100, 100);
            Assert.InRange(decision.Confidence, 0m, 1m);
            Assert.InRange(decision.MaxSuggestedPosition, 0m, RecommendationRule.MaxSuggestedPositionCap);
        }
    }

    [Fact]
    public void CapsPositionsAndKeepsDirectionalPositionMonotonic()
    {
        var weakBuy = RecommendationRule.Evaluate(
            Indicators(return7: 0.10m, return30: 0.08m),
            AsOfUtc);
        var strongBuy = RecommendationRule.Evaluate(
            Indicators(return7: 0.40m, return30: 0.30m),
            AsOfUtc);
        var weakSell = RecommendationRule.Evaluate(
            Indicators(return7: -0.10m, return30: -0.08m),
            AsOfUtc);
        var strongSell = RecommendationRule.Evaluate(
            Indicators(return7: -0.40m, return30: -0.30m),
            AsOfUtc);
        var hold = RecommendationRule.Evaluate(
            Indicators(return7: 0.04m, return30: 0.04m),
            AsOfUtc);

        Assert.Equal(RecommendationAction.CandidateBuy, weakBuy.Action);
        Assert.Equal(RecommendationAction.CandidateBuy, strongBuy.Action);
        Assert.Equal(RecommendationAction.CandidateSell, weakSell.Action);
        Assert.Equal(RecommendationAction.CandidateSell, strongSell.Action);
        Assert.Equal(RecommendationAction.Hold, hold.Action);
        Assert.True(strongBuy.MaxSuggestedPosition >= weakBuy.MaxSuggestedPosition);
        Assert.True(strongSell.MaxSuggestedPosition <= weakSell.MaxSuggestedPosition);
        Assert.InRange(hold.MaxSuggestedPosition, 0m, RecommendationRule.MaxSuggestedPositionCap);
        Assert.All(
            new[] { weakBuy, strongBuy, weakSell, strongSell, hold },
            decision => Assert.InRange(decision.MaxSuggestedPosition, 0m, RecommendationRule.MaxSuggestedPositionCap));
    }

    [Fact]
    public void KeepsWeakNegativeTrendInObserveWithoutPosition()
    {
        var decision = RecommendationRule.Evaluate(
            Indicators(return7: -0.02m, return30: -0.02m),
            AsOfUtc);

        Assert.Equal(RecommendationAction.Observe, decision.Action);
        Assert.Equal(0m, decision.MaxSuggestedPosition);
    }

    [Fact]
    public void RejectsIndicatorsThatAreAfterTheExplicitAsOf()
    {
        var decision = RecommendationRule.Evaluate(
            Indicators(cutoffUtc: AsOfUtc.AddMinutes(1)),
            AsOfUtc);

        Assert.Equal(RecommendationAction.DataInsufficient, decision.Action);
        Assert.Contains(decision.Reasons, reason => reason.Code == "future-indicators");
        Assert.Equal(AsOfUtc, decision.AsOfUtc);
    }

    [Fact]
    public void SameInputProducesTheSameDecision()
    {
        var first = RecommendationRule.Evaluate(
            Indicators(return7: 0.12m, return30: 0.08m, supply7: -0.20m),
            AsOfUtc);
        var second = RecommendationRule.Evaluate(
            Indicators(return7: 0.12m, return30: 0.08m, supply7: -0.20m),
            AsOfUtc);

        Assert.Equal(first.Action, second.Action);
        Assert.Equal(first.DirectionScore, second.DirectionScore);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.RuleVersion, second.RuleVersion);
        Assert.Equal(first.Reasons, second.Reasons);
        Assert.Equal(first.InvalidationConditions, second.InvalidationConditions);
        Assert.Equal(first.MaxSuggestedPosition, second.MaxSuggestedPosition);
    }

    private static RobustMarketIndicators Indicators(
        DateTimeOffset? cutoffUtc = null,
        int sampleCount7 = 7,
        int sampleCount30 = 30,
        int inlierCount7 = 7,
        int inlierCount30 = 30,
        decimal? return7 = 0.02m,
        decimal? return30 = 0.02m,
        decimal? volatility7 = 0.05m,
        decimal? volatility30 = 0.08m,
        decimal? supply7 = 0m,
        decimal? supply30 = 0m,
        decimal? dataAgeHours = 12m)
        => new(
            cutoffUtc ?? AsOfUtc,
            100m,
            100m,
            1m,
            1m,
            sampleCount7,
            sampleCount30,
            inlierCount7,
            inlierCount30,
            return7,
            return30,
            100m,
            100m,
            volatility7,
            volatility30,
            supply7,
            supply30,
            dataAgeHours);
}
