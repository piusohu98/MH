using MH.Core;
using MH.Core.Recommendations;
using MH.Core.Simulation;

namespace MH.Tests;

public sealed class RecommendationValidationScenarioTests
{
    [Fact]
    public void ScenariosProduceTheirExpectedActionsThroughRealAnalyzerAndRule()
    {
        Assert.Equal(5, RecommendationValidationScenarios.All.Count);

        foreach (var scenario in RecommendationValidationScenarios.All)
        {
            var indicators = RobustMarketAnalyzer.Analyze(scenario.Bars, scenario.AsOfUtc);
            var decision = RecommendationRule.Evaluate(indicators, scenario.AsOfUtc);

            Assert.Equal(scenario.ExpectedAction, decision.Action);
        }
    }

    [Fact]
    public void ScenariosAreDeterministic()
    {
        var first = RecommendationValidationScenarios.All;
        var second = RecommendationValidationScenarios.Create();

        Assert.Equal(first.Count, second.Count);
        for (var index = 0; index < first.Count; index++)
        {
            Assert.Equal(first[index].Name, second[index].Name);
            Assert.Equal(first[index].AsOfUtc, second[index].AsOfUtc);
            Assert.Equal(first[index].ExpectedAction, second[index].ExpectedAction);
            Assert.Equal(BarSnapshot(first[index].Bars), BarSnapshot(second[index].Bars));
        }
    }

    [Fact]
    public void FutureExtremeBarsCannotChangeHistoricalIndicatorsOrRecommendations()
    {
        foreach (var scenario in RecommendationValidationScenarios.All)
        {
            var indicators = RobustMarketAnalyzer.Analyze(scenario.Bars, scenario.AsOfUtc);
            var decision = RecommendationRule.Evaluate(indicators, scenario.AsOfUtc);
            var futureBar = new MH.Core.Models.PriceBar(
                scenario.AsOfUtc.AddMinutes(1),
                scenario.AsOfUtc.AddMinutes(2),
                1_000_000,
                1_000_000,
                1,
                1_000_000,
                1,
                false);
            var futureIndicators = RobustMarketAnalyzer.Analyze(
                scenario.Bars.Append(futureBar),
                scenario.AsOfUtc);
            var futureDecision = RecommendationRule.Evaluate(futureIndicators, scenario.AsOfUtc);

            Assert.Equal(indicators, futureIndicators);
            Assert.Equal(DecisionSnapshot(decision), DecisionSnapshot(futureDecision));
            Assert.Equal(
                decision.Reasons.Select(reason => (reason.Code, reason.Detail)),
                futureDecision.Reasons.Select(reason => (reason.Code, reason.Detail)));
            Assert.Equal(decision.InvalidationConditions, futureDecision.InvalidationConditions);
        }
    }

    private static IEnumerable<(
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        int Open,
        int High,
        int Low,
        int Close,
        int Volume,
        bool HasOcrAnomaly)> BarSnapshot(
        IEnumerable<MH.Core.Models.PriceBar> bars)
        => bars.Select(bar => (
            bar.StartUtc,
            bar.EndUtc,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume,
            bar.HasOcrAnomaly));

    private static (
        RecommendationAction Action,
        int DirectionScore,
        decimal Confidence,
        string RuleVersion,
        decimal MaxSuggestedPosition) DecisionSnapshot(RecommendationDecision decision)
        => (
            decision.Action,
            decision.DirectionScore,
            decision.Confidence,
            decision.RuleVersion,
            decision.MaxSuggestedPosition);
}
