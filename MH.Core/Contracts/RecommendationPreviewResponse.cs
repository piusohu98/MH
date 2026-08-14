using MH.Core.Backtesting;
using MH.Core.Recommendations;

namespace MH.Core.Contracts;

public sealed record RecommendationPreviewResearchAssumptions(
    decimal InitialCapital,
    decimal TradingCostRate,
    decimal SlippageRate,
    int WindowCount,
    int WindowDays,
    int WarmupDays,
    string ScopeNotice);

public sealed record RecommendationPreviewResponse(
    string ServerId,
    string ItemId,
    DateTimeOffset AsOfUtc,
    RecommendationDecision Decision,
    bool IsActionable,
    BacktestQualityGateResult QualityGate,
    RecommendationPreviewResearchAssumptions ResearchAssumptions);
