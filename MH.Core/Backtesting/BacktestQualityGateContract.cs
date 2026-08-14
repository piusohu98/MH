namespace MH.Core.Backtesting;

/// <summary>
/// A conservative backtest quality state. Trial eligibility only permits a small,
/// human-supervised trial and is not a promise of real-world profit.
/// </summary>
public enum BacktestQualityStatus
{
    ResearchOnly = 0,
    Disabled = 1,
    TrialEligible = 2
}

public sealed record BacktestQualityReason(string Code, string Detail);

public sealed record BacktestWindowSummary(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    decimal CoverageDays,
    int DecisionCount,
    int TradeCount,
    decimal TotalReturn,
    decimal MaxDrawdown,
    decimal Turnover,
    string RuleVersion);

public sealed record BacktestQualitySummary(
    int WindowCount,
    decimal AverageReturn,
    decimal MedianReturn,
    decimal ProfitableWindowRatio,
    decimal WorstMaxDrawdown,
    decimal AverageTurnover);

public sealed record BacktestQualityGateResult(
    BacktestQualityStatus Status,
    string GateVersion,
    string? RuleVersion,
    BacktestQualitySummary Summary,
    IReadOnlyList<BacktestWindowSummary> Windows,
    IReadOnlyList<BacktestQualityReason> Reasons);
