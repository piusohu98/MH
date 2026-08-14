namespace MH.Core.Recommendations;

public enum RecommendationAction
{
    DataInsufficient = 0,
    Observe = 1,
    CandidateBuy = 2,
    Hold = 3,
    CandidateSell = 4,
    Avoid = 5
}

public sealed record RecommendationReason(string Code, string Detail);

/// <summary>
/// An explainable, post-trade target decision. <see cref="MaxSuggestedPosition"/> is the
/// maximum fraction of available trading capital that should remain allocated to this item
/// after the suggested action.
/// </summary>
public sealed record RecommendationDecision(
    DateTimeOffset AsOfUtc,
    RecommendationAction Action,
    int DirectionScore,
    decimal Confidence,
    string RuleVersion,
    IReadOnlyList<RecommendationReason> Reasons,
    IReadOnlyList<string> InvalidationConditions,
    decimal MaxSuggestedPosition);
