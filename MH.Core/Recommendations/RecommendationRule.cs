using System.Globalization;

namespace MH.Core.Recommendations;

public static class RecommendationRule
{
    public const string RuleVersion = "recommendation-rules-v1";
    public const decimal MaxDataAgeHours = 48m;
    public const decimal HighVolatilityThreshold = 0.25m;
    public const decimal TrendThreshold = 0.03m;
    public const decimal SupplyChangeThreshold = 0.25m;
    public const decimal MaxSuggestedPositionCap = 0.25m;
    public const int MinimumSamples = 3;

    private static readonly string[] DefaultInvalidationConditions =
    [
        "数据年龄超过 48 小时",
        "7 日或 30 日趋势方向反转",
        "任一窗口有效样本低于 3",
        "波动率进入高风险区间"
    ];

    public static RecommendationDecision Evaluate(
        RobustMarketIndicators indicators,
        DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(indicators);

        var normalizedAsOfUtc = asOfUtc.ToUniversalTime();
        var normalizedCutoffUtc = indicators.CutoffUtc.ToUniversalTime();
        if (normalizedCutoffUtc > normalizedAsOfUtc)
        {
            return Insufficient(
                normalizedAsOfUtc,
                "future-indicators",
                "指标截止时间晚于显式建议时点，拒绝读取未来信息。");
        }

        if (indicators.DataAgeHours is null || indicators.DataAgeHours < 0m)
        {
            return Insufficient(normalizedAsOfUtc, "freshness-missing", "缺少有效的数据年龄。");
        }

        var effectiveAgeHours = indicators.DataAgeHours.Value
            + (normalizedAsOfUtc - normalizedCutoffUtc).Ticks / (decimal)TimeSpan.TicksPerHour;
        if (effectiveAgeHours > MaxDataAgeHours)
        {
            return Insufficient(
                normalizedAsOfUtc,
                "stale-data",
                $"有效数据年龄 {effectiveAgeHours.ToString("0.##", CultureInfo.InvariantCulture)} 小时超过门槛。");
        }

        if (indicators.SampleCount7Days < MinimumSamples
            || indicators.SampleCount30Days < MinimumSamples
            || indicators.InlierCount7Days < MinimumSamples
            || indicators.InlierCount30Days < MinimumSamples)
        {
            return Insufficient(normalizedAsOfUtc, "sample-insufficient", "7 日和 30 日窗口都需要至少 3 个有效内点。");
        }

        if (indicators.Return7Days is null
            || indicators.Return30Days is null
            || indicators.Volatility7Days is null
            || indicators.Volatility30Days is null)
        {
            return Insufficient(normalizedAsOfUtc, "metrics-missing", "趋势或波动指标不完整。");
        }

        var return7 = indicators.Return7Days.Value;
        var return30 = indicators.Return30Days.Value;
        var positiveTrend = return7 >= TrendThreshold && return30 >= TrendThreshold;
        var negativeTrend = return7 <= -TrendThreshold && return30 <= -TrendThreshold;
        var conflictingTrend = (return7 >= TrendThreshold && return30 <= -TrendThreshold)
            || (return7 <= -TrendThreshold && return30 >= TrendThreshold);

        var supplyValues = new[] { indicators.VisibleSupplyChange7Days, indicators.VisibleSupplyChange30Days }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        var averageSupplyChange = supplyValues.Length == 0 ? (decimal?)null : supplyValues.Average();
        var supplyContraction = averageSupplyChange <= -SupplyChangeThreshold;
        var supplyExpansion = averageSupplyChange >= SupplyChangeThreshold;

        var rawScore = return7 * 220m + return30 * 160m;
        if (averageSupplyChange.HasValue)
        {
            rawScore -= averageSupplyChange.Value * 35m;
        }

        var directionScore = ClampScore(rawScore);
        var highVolatility = indicators.Volatility7Days.Value >= HighVolatilityThreshold
            || indicators.Volatility30Days.Value >= HighVolatilityThreshold;
        var reasons = new List<RecommendationReason>();

        if (positiveTrend || negativeTrend)
        {
            reasons.Add(new RecommendationReason(
                "trend-consistent",
                $"7 日收益 {return7.ToString("P1", CultureInfo.InvariantCulture)} 与 30 日收益 {return30.ToString("P1", CultureInfo.InvariantCulture)} 同向。"));
        }
        else if (conflictingTrend)
        {
            reasons.Add(new RecommendationReason(
                "trend-conflict",
                $"7 日收益 {return7.ToString("P1", CultureInfo.InvariantCulture)} 与 30 日收益 {return30.ToString("P1", CultureInfo.InvariantCulture)} 方向冲突。"));
        }
        else
        {
            reasons.Add(new RecommendationReason("trend-neutral", "趋势未达到方向阈值。"));
        }

        if (supplyContraction)
        {
            reasons.Add(new RecommendationReason("supply-contraction", "可见供给收缩，作为方向分数的正向证据。"));
        }
        else if (supplyExpansion)
        {
            reasons.Add(new RecommendationReason("supply-expansion", "可见供给扩张，作为方向分数的负向证据。"));
        }

        if (highVolatility)
        {
            reasons.Add(new RecommendationReason("high-volatility", "7 日或 30 日波动率达到高风险阈值。"));
        }

        var confidence = CalculateConfidence(
            indicators,
            effectiveAgeHours,
            positiveTrend,
            negativeTrend,
            conflictingTrend,
            supplyValues.Length > 0,
            highVolatility);

        RecommendationAction action;
        if (highVolatility)
        {
            action = RecommendationAction.Avoid;
        }
        else if (conflictingTrend || (positiveTrend && supplyExpansion) || (negativeTrend && supplyContraction))
        {
            action = RecommendationAction.Observe;
        }
        else if (directionScore >= 30)
        {
            action = RecommendationAction.CandidateBuy;
        }
        else if (directionScore <= -30)
        {
            action = RecommendationAction.CandidateSell;
        }
        else if (positiveTrend)
        {
            action = RecommendationAction.Hold;
        }
        else
        {
            action = RecommendationAction.Observe;
        }

        var maxPosition = action switch
        {
            RecommendationAction.CandidateBuy => CalculateBuyPosition(confidence, directionScore),
            RecommendationAction.CandidateSell => CalculateSellPosition(confidence, directionScore),
            RecommendationAction.Hold => MaxSuggestedPositionCap,
            _ => 0m
        };

        return new RecommendationDecision(
            normalizedAsOfUtc,
            action,
            directionScore,
            confidence,
            RuleVersion,
            reasons.ToArray(),
            DefaultInvalidationConditions,
            maxPosition);
    }

    private static RecommendationDecision Insufficient(
        DateTimeOffset asOfUtc,
        string code,
        string detail)
        => new(
            asOfUtc,
            RecommendationAction.DataInsufficient,
            0,
            0m,
            RuleVersion,
            [new RecommendationReason(code, detail)],
            DefaultInvalidationConditions,
            0m);

    private static decimal CalculateConfidence(
        RobustMarketIndicators indicators,
        decimal effectiveAgeHours,
        bool positiveTrend,
        bool negativeTrend,
        bool conflictingTrend,
        bool hasSupplyEvidence,
        bool highVolatility)
    {
        var sevenQuality = Math.Min(1m, (decimal)indicators.InlierCount7Days / indicators.SampleCount7Days);
        var thirtyQuality = Math.Min(1m, (decimal)indicators.InlierCount30Days / indicators.SampleCount30Days);
        var confidence = 0.35m + (sevenQuality + thirtyQuality) / 2m * 0.20m;
        confidence += positiveTrend || negativeTrend ? 0.25m : conflictingTrend ? 0.05m : 0.10m;
        confidence += hasSupplyEvidence ? 0.05m : 0m;
        confidence -= Math.Min(0.15m, effectiveAgeHours / MaxDataAgeHours * 0.15m);
        confidence -= highVolatility ? 0.15m : 0m;
        return Math.Clamp(confidence, 0m, 1m);
    }

    private static int ClampScore(decimal rawScore)
        => (int)Math.Clamp(decimal.Round(rawScore, 0, MidpointRounding.AwayFromZero), -100m, 100m);

    private static decimal CalculateBuyPosition(decimal confidence, int directionScore)
        => ClampPosition(MaxSuggestedPositionCap * confidence * directionScore / 100m);

    private static decimal CalculateSellPosition(decimal confidence, int directionScore)
        => ClampPosition(MaxSuggestedPositionCap * (1m - confidence * Math.Abs(directionScore) / 100m));

    private static decimal ClampPosition(decimal position)
        => Math.Clamp(position, 0m, MaxSuggestedPositionCap);
}
