using MH.Core.Backtesting;
using MH.Core.Recommendations;

namespace MH.Tests;

public sealed class BacktestQualityGateTests
{
    private static readonly DateTimeOffset BaseUtc = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmptyInputRemainsResearchOnly()
    {
        var result = BacktestQualityGate.Evaluate([]);

        Assert.Equal(BacktestQualityStatus.ResearchOnly, result.Status);
        Assert.Null(result.RuleVersion);
        Assert.Contains(result.Reasons, reason => reason.Code == "sample-insufficient");
    }

    [Fact]
    public void RequiresThreeNonOverlappingWindowsAndPerWindowMinimumCoverage()
    {
        var tooFew = BacktestQualityGate.Evaluate([Window(0), Window(40)]);

        Assert.Equal(BacktestQualityStatus.ResearchOnly, tooFew.Status);
        Assert.Contains(tooFew.Reasons, reason => reason.Code == "window-count-insufficient");

        var result = BacktestQualityGate.Evaluate(
        [
            Window(0, coverageDays: 29),
            Window(40, decisionCount: 19),
            Window(80, tradeCount: 2)
        ]);

        Assert.Equal(BacktestQualityStatus.ResearchOnly, result.Status);
        Assert.Contains(result.Reasons, reason => reason.Code == "coverage-insufficient");
        Assert.Contains(result.Reasons, reason => reason.Code == "decision-count-insufficient");
        Assert.Contains(result.Reasons, reason => reason.Code == "trade-count-insufficient");
    }

    [Fact]
    public void OverallLossDisablesTheGate()
    {
        var result = BacktestQualityGate.Evaluate(
        [
            Window(0, totalReturn: -0.02m),
            Window(40, totalReturn: -0.01m),
            Window(80, totalReturn: -0.03m)
        ]);

        Assert.Equal(BacktestQualityStatus.Disabled, result.Status);
        Assert.Contains(result.Reasons, reason => reason.Code == "overall-loss");
    }

    [Fact]
    public void CatastrophicDrawdownDisablesTheGate()
    {
        var result = BacktestQualityGate.Evaluate(
        [
            Window(0),
            Window(40, maxDrawdown: 0.36m),
            Window(80)
        ]);

        Assert.Equal(BacktestQualityStatus.Disabled, result.Status);
        Assert.Contains(result.Reasons, reason => reason.Code == "catastrophic-drawdown");
    }

    [Fact]
    public void DrawdownBoundariesSeparateTrialResearchAndDisabled()
    {
        var trialBoundary = BacktestQualityGate.Evaluate(
        [
            Window(0, maxDrawdown: 0.20m),
            Window(40),
            Window(80)
        ]);
        Assert.Equal(BacktestQualityStatus.TrialEligible, trialBoundary.Status);

        foreach (var drawdown in new[] { 0.21m, 0.34m, 0.35m })
        {
            var research = BacktestQualityGate.Evaluate(
            [
                Window(0, maxDrawdown: drawdown),
                Window(40),
                Window(80)
            ]);

            Assert.Equal(BacktestQualityStatus.ResearchOnly, research.Status);
            Assert.Contains(research.Reasons, reason => reason.Code == "trial-drawdown-too-high");
            Assert.DoesNotContain(research.Reasons, reason => reason.Code == "catastrophic-drawdown");
        }

        var disabled = BacktestQualityGate.Evaluate(
        [
            Window(0, maxDrawdown: 0.36m),
            Window(40),
            Window(80)
        ]);

        Assert.Equal(BacktestQualityStatus.Disabled, disabled.Status);
        Assert.Contains(disabled.Reasons, reason => reason.Code == "catastrophic-drawdown");
        Assert.DoesNotContain(disabled.Reasons, reason => reason.Code == "trial-drawdown-too-high");
    }

    [Fact]
    public void TailWindowLossUsesStrictResearchAndCatastrophicBoundaries()
    {
        var research = BacktestQualityGate.Evaluate(
        [
            Window(0, totalReturn: -0.15m),
            Window(40, totalReturn: 0.30m),
            Window(80, totalReturn: 0.30m)
        ]);
        Assert.Equal(BacktestQualityStatus.ResearchOnly, research.Status);
        Assert.Contains(research.Reasons, reason => reason.Code == "tail-window-loss");

        var lowerBoundary = BacktestQualityGate.Evaluate(
        [
            Window(0, totalReturn: -0.10m),
            Window(40, totalReturn: 0.30m),
            Window(80, totalReturn: 0.30m)
        ]);
        Assert.Equal(BacktestQualityStatus.TrialEligible, lowerBoundary.Status);
        Assert.DoesNotContain(lowerBoundary.Reasons, reason => reason.Code == "tail-window-loss");

        var catastrophic = BacktestQualityGate.Evaluate(
        [
            Window(0, totalReturn: -0.25m),
            Window(40, totalReturn: 0.30m),
            Window(80, totalReturn: 0.30m)
        ]);
        Assert.Equal(BacktestQualityStatus.Disabled, catastrophic.Status);
        Assert.Contains(catastrophic.Reasons, reason => reason.Code == "catastrophic-window-loss");
    }

    [Fact]
    public void HighTurnoverRemainsResearchOnly()
    {
        var result = BacktestQualityGate.Evaluate(
        [
            Window(0, turnover: 1.01m),
            Window(40, turnover: 1.01m),
            Window(80, turnover: 1.01m)
        ]);

        Assert.Equal(BacktestQualityStatus.ResearchOnly, result.Status);
        Assert.Contains(result.Reasons, reason => reason.Code == "high-turnover");
    }

    [Fact]
    public void RuleVersionConflictDisablesTheGate()
    {
        var result = BacktestQualityGate.Evaluate(
        [
            Window(0, ruleVersion: "other-rule"),
            Window(40, ruleVersion: "other-rule"),
            Window(80, ruleVersion: "other-rule")
        ]);

        Assert.Equal(BacktestQualityStatus.Disabled, result.Status);
        Assert.Equal("other-rule", result.RuleVersion);
        Assert.Contains(result.Reasons, reason => reason.Code == "rule-version-mismatch");
    }

    [Fact]
    public void ConflictingWindowResultsRemainResearchOnly()
    {
        var result = BacktestQualityGate.Evaluate(
        [
            Window(0, totalReturn: 0.12m),
            Window(40, totalReturn: -0.01m),
            Window(80, totalReturn: -0.02m)
        ]);

        Assert.Equal(BacktestQualityStatus.ResearchOnly, result.Status);
        Assert.Contains(result.Reasons, reason => reason.Code == "window-conflict");
        Assert.Equal(1m / 3m, result.Summary.ProfitableWindowRatio);
    }

    [Fact]
    public void FullyPassingWindowsAreEligibleOnlyForSmallTrialUse()
    {
        var result = BacktestQualityGate.Evaluate(PassingWindows());

        Assert.Equal(BacktestQualityStatus.TrialEligible, result.Status);
        Assert.Equal(BacktestQualityGate.GateVersion, result.GateVersion);
        Assert.Equal(RecommendationRule.RuleVersion, result.RuleVersion);
        Assert.Equal(3, result.Summary.WindowCount);
        Assert.Equal(0.10m, result.Summary.MedianReturn);
        Assert.Equal(1m, result.Summary.ProfitableWindowRatio);
        Assert.Equal(0.10m, result.Summary.WorstMaxDrawdown);
        Assert.Equal(0.25m, result.Summary.AverageTurnover);
        Assert.Contains(result.Reasons, reason => reason.Code == "trial-small-size-only");
    }

    [Fact]
    public void SameInputIsDeterministicAndInputOrderDoesNotMatter()
    {
        var input = PassingWindows();
        var first = BacktestQualityGate.Evaluate(input);
        var second = BacktestQualityGate.Evaluate(input);
        var reversed = BacktestQualityGate.Evaluate(input.Reverse());

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.GateVersion, second.GateVersion);
        Assert.Equal(first.RuleVersion, second.RuleVersion);
        Assert.Equal(first.Summary, second.Summary);
        Assert.Equal(WindowSnapshot(first.Windows), WindowSnapshot(second.Windows));
        Assert.Equal(ReasonSnapshot(first.Reasons), ReasonSnapshot(second.Reasons));
        Assert.Equal(first.Summary, reversed.Summary);
        Assert.Equal(WindowSnapshot(first.Windows), WindowSnapshot(reversed.Windows));
        Assert.Equal(ReasonSnapshot(first.Reasons), ReasonSnapshot(reversed.Reasons));
    }

    [Fact]
    public void RejectsOverlappingOrMalformedResultsExplicitly()
    {
        var overlapping = BacktestQualityGate.Evaluate(
        [
            Window(0),
            Window(20),
            Window(80)
        ]);

        Assert.Equal(BacktestQualityStatus.ResearchOnly, overlapping.Status);
        Assert.Contains(overlapping.Reasons, reason => reason.Code == "windows-overlap");

        Assert.Throws<ArgumentNullException>(() => BacktestQualityGate.Evaluate(null!));
        Assert.Throws<ArgumentException>(() => BacktestQualityGate.Evaluate([Window(0, invalidInterval: true)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => BacktestQualityGate.Evaluate([Window(0, initialCapital: 0m)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => BacktestQualityGate.Evaluate([Window(0, totalReturn: -1.1m)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => BacktestQualityGate.Evaluate([Window(0, maxDrawdown: 1.01m)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => BacktestQualityGate.Evaluate([Window(0, tradingCostRate: 1m)]));
    }

    private static IReadOnlyList<RollingBacktestResult> PassingWindows()
        =>
        [
            Window(0, totalReturn: 0.08m, maxDrawdown: 0.08m),
            Window(40, totalReturn: 0.10m, maxDrawdown: 0.10m),
            Window(80, totalReturn: 0.12m, maxDrawdown: 0.06m)
        ];

    private static RollingBacktestResult Window(
        int startDay,
        int coverageDays = 40,
        int decisionCount = 30,
        int tradeCount = 5,
        decimal totalReturn = 0.10m,
        decimal maxDrawdown = 0.10m,
        decimal turnover = 0.25m,
        string? ruleVersion = null,
        decimal initialCapital = 10_000m,
        bool invalidInterval = false,
        decimal tradingCostRate = 0m)
    {
        var startUtc = BaseUtc.AddDays(startDay);
        var endUtc = invalidInterval ? startUtc : startUtc.AddDays(coverageDays);
        var finalEquity = initialCapital * (1m + totalReturn);
        return new RollingBacktestResult(
            startUtc,
            endUtc,
            initialCapital,
            tradingCostRate,
            0m,
            ruleVersion ?? RecommendationRule.RuleVersion,
            finalEquity,
            totalReturn,
            maxDrawdown,
            turnover,
            decisionCount,
            tradeCount,
            finalEquity,
            0,
            []);
    }

    private static IEnumerable<(
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        decimal CoverageDays,
        int DecisionCount,
        int TradeCount,
        decimal TotalReturn,
        decimal MaxDrawdown,
        decimal Turnover,
        string RuleVersion)> WindowSnapshot(
        IEnumerable<BacktestWindowSummary> windows)
        => windows.Select(window => (
            window.StartUtc,
            window.EndUtc,
            window.CoverageDays,
            window.DecisionCount,
            window.TradeCount,
            window.TotalReturn,
            window.MaxDrawdown,
            window.Turnover,
            window.RuleVersion));

    private static IEnumerable<(string Code, string Detail)> ReasonSnapshot(
        IEnumerable<BacktestQualityReason> reasons)
        => reasons.Select(reason => (reason.Code, reason.Detail));
}
