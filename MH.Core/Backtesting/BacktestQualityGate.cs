using MH.Core.Recommendations;

namespace MH.Core.Backtesting;

public static class BacktestQualityGate
{
    public const string GateVersion = "backtest-quality-gate-v1";
    public const int MinimumWindowCount = 3;
    public const int MinimumCoverageDays = 30;
    public const int MinimumDecisionCount = 20;
    public const int MinimumTradeCount = 3;
    public const decimal MinimumProfitableWindowRatio = 2m / 3m;
    public const decimal MinimumMedianReturn = 0.02m;
    public const decimal MaximumTrialDrawdown = 0.20m;
    public const decimal CatastrophicDrawdown = 0.35m;
    public const decimal TailWindowResearchLossThreshold = -0.10m;
    public const decimal CatastrophicWindowLossThreshold = -0.25m;
    public const decimal MaximumAverageTurnover = 1.00m;
    public const string TrialUseNotice = "仅允许小额、人工监督试用，不代表真实获利保证。";

    public static BacktestQualityGateResult Evaluate(
        IEnumerable<RollingBacktestResult> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var results = windows
            .Select((window, index) => window ?? throw new ArgumentException(
                $"第 {index} 个回测窗口不能为空。",
                nameof(windows)))
            .ToArray();
        foreach (var result in results)
        {
            ValidateResult(result);
        }

        var summaries = results
            .Select(CreateWindowSummary)
            .OrderBy(window => window.StartUtc)
            .ThenBy(window => window.EndUtc)
            .ToArray();
        var summary = Summarize(summaries);
        var ruleVersion = GetReportedRuleVersion(summaries);
        var reasons = new List<BacktestQualityReason>();
        var ruleVersionMismatch = summaries.Any(window =>
            !string.Equals(window.RuleVersion, RecommendationRule.RuleVersion, StringComparison.Ordinal));

        if (ruleVersionMismatch)
        {
            reasons.Add(new BacktestQualityReason(
                "rule-version-mismatch",
                $"回测使用的规则版本必须全部为 {RecommendationRule.RuleVersion}，实际为 {ruleVersion ?? "无"}。"));
        }

        if (summaries.Length < MinimumWindowCount)
        {
            reasons.Add(new BacktestQualityReason(
                summaries.Length == 0 ? "sample-insufficient" : "window-count-insufficient",
                summaries.Length == 0
                    ? "没有可用回测窗口，暂只能保留研究状态。"
                    : $"至少需要 {MinimumWindowCount} 个互不重叠回测窗口，实际为 {summaries.Length} 个。"));
        }

        if (HasOverlappingWindows(summaries))
        {
            reasons.Add(new BacktestQualityReason(
                "windows-overlap",
                "回测窗口必须按时间互不重叠，边界相接不视为重叠。"));
        }

        foreach (var window in summaries)
        {
            if (window.CoverageDays < MinimumCoverageDays)
            {
                reasons.Add(new BacktestQualityReason(
                    "coverage-insufficient",
                    $"窗口 {window.StartUtc:O} 至 {window.EndUtc:O} 覆盖 {window.CoverageDays:0.##} 天，少于最低 {MinimumCoverageDays} 天。"));
            }

            if (window.DecisionCount < MinimumDecisionCount)
            {
                reasons.Add(new BacktestQualityReason(
                    "decision-count-insufficient",
                    $"窗口 {window.StartUtc:O} 至 {window.EndUtc:O} 只有 {window.DecisionCount} 次决策，少于最低 {MinimumDecisionCount} 次。"));
            }

            if (window.TradeCount < MinimumTradeCount)
            {
                reasons.Add(new BacktestQualityReason(
                    "trade-count-insufficient",
                    $"窗口 {window.StartUtc:O} 至 {window.EndUtc:O} 只有 {window.TradeCount} 次交易，少于最低 {MinimumTradeCount} 次。"));
            }
        }

        var sampleInsufficient = summaries.Length < MinimumWindowCount
            || HasOverlappingWindows(summaries)
            || summaries.Any(window =>
                window.CoverageDays < MinimumCoverageDays
                || window.DecisionCount < MinimumDecisionCount
                || window.TradeCount < MinimumTradeCount);
        if (sampleInsufficient)
        {
            if (reasons.Count == 0)
            {
                reasons.Add(new BacktestQualityReason(
                    "sample-insufficient",
                    "回测窗口的覆盖或样本不足，暂只能保留研究状态。"));
            }

            return CreateResult(
                ruleVersionMismatch
                    ? BacktestQualityStatus.Disabled
                    : BacktestQualityStatus.ResearchOnly,
                ruleVersion,
                summary,
                summaries,
                reasons);
        }

        var disabled = false;
        if (summary.AverageReturn <= 0m)
        {
            reasons.Add(new BacktestQualityReason(
                "overall-loss",
                $"窗口平均总收益为 {summary.AverageReturn:P2}，整体未实现正收益。"));
            disabled = true;
        }

        if (summary.WorstMaxDrawdown > CatastrophicDrawdown)
        {
            reasons.Add(new BacktestQualityReason(
                "catastrophic-drawdown",
                $"最坏窗口最大回撤为 {summary.WorstMaxDrawdown:P2}，超过 {CatastrophicDrawdown:P0} 的灾难阈值。"));
            disabled = true;
        }

        var catastrophicLossWindows = summaries
            .Where(window => window.TotalReturn <= CatastrophicWindowLossThreshold)
            .ToArray();
        if (catastrophicLossWindows.Length > 0)
        {
            reasons.Add(new BacktestQualityReason(
                "catastrophic-window-loss",
                $"至少一个窗口总收益不高于 {CatastrophicWindowLossThreshold:P0}，最差为 {catastrophicLossWindows.Min(window => window.TotalReturn):P2}。"));
            disabled = true;
        }

        if (ruleVersionMismatch || disabled)
        {
            return CreateResult(
                BacktestQualityStatus.Disabled,
                ruleVersion,
                summary,
                summaries,
                reasons);
        }

        if (summary.WorstMaxDrawdown > MaximumTrialDrawdown)
        {
            reasons.Add(new BacktestQualityReason(
                "trial-drawdown-too-high",
                $"最坏窗口最大回撤为 {summary.WorstMaxDrawdown:P2}，超过 {MaximumTrialDrawdown:P0} 的小额试用上限但未达到灾难阈值。"));
        }

        var tailLossWindows = summaries
            .Where(window => window.TotalReturn < TailWindowResearchLossThreshold
                && window.TotalReturn > CatastrophicWindowLossThreshold)
            .ToArray();
        if (tailLossWindows.Length > 0)
        {
            reasons.Add(new BacktestQualityReason(
                "tail-window-loss",
                $"至少一个窗口总收益低于 {TailWindowResearchLossThreshold:P0} 且高于 {CatastrophicWindowLossThreshold:P0}，最差为 {tailLossWindows.Min(window => window.TotalReturn):P2}。"));
        }

        if (summary.ProfitableWindowRatio < MinimumProfitableWindowRatio)
        {
            reasons.Add(new BacktestQualityReason(
                "window-conflict",
                $"盈利窗口比例为 {summary.ProfitableWindowRatio:P2}，少于最低 {MinimumProfitableWindowRatio:P0}。"));
        }

        if (summary.MedianReturn < MinimumMedianReturn)
        {
            reasons.Add(new BacktestQualityReason(
                "median-return-insufficient",
                $"窗口总收益中位数为 {summary.MedianReturn:P2}，少于最低 {MinimumMedianReturn:P0}。"));
        }

        if (summary.AverageTurnover > MaximumAverageTurnover)
        {
            reasons.Add(new BacktestQualityReason(
                "high-turnover",
                $"窗口平均换手为 {summary.AverageTurnover:P2}，超过 {MaximumAverageTurnover:P0} 的保守上限。"));
        }

        if (reasons.Count > 0)
        {
            return CreateResult(
                BacktestQualityStatus.ResearchOnly,
                ruleVersion,
                summary,
                summaries,
                reasons);
        }

        reasons.Add(new BacktestQualityReason("trial-small-size-only", TrialUseNotice));
        return CreateResult(
            BacktestQualityStatus.TrialEligible,
            ruleVersion,
            summary,
            summaries,
            reasons);
    }

    private static BacktestQualityGateResult CreateResult(
        BacktestQualityStatus status,
        string? ruleVersion,
        BacktestQualitySummary summary,
        IReadOnlyList<BacktestWindowSummary> windows,
        IReadOnlyList<BacktestQualityReason> reasons)
        => new(
            status,
            GateVersion,
            ruleVersion,
            summary,
            windows.ToArray(),
            reasons.ToArray());

    private static BacktestWindowSummary CreateWindowSummary(RollingBacktestResult result)
    {
        var startUtc = result.StartUtc.ToUniversalTime();
        var endUtc = result.EndUtc.ToUniversalTime();
        return new BacktestWindowSummary(
            startUtc,
            endUtc,
            (decimal)(endUtc - startUtc).Ticks / TimeSpan.TicksPerDay,
            result.DecisionCount,
            result.TradeCount,
            result.TotalReturn,
            result.MaxDrawdown,
            result.Turnover,
            result.RuleVersion);
    }

    private static BacktestQualitySummary Summarize(
        IReadOnlyList<BacktestWindowSummary> windows)
    {
        if (windows.Count == 0)
        {
            return new BacktestQualitySummary(0, 0m, 0m, 0m, 0m, 0m);
        }

        var returns = windows.Select(window => window.TotalReturn).ToArray();
        return new BacktestQualitySummary(
            windows.Count,
            returns.Average(),
            Median(returns),
            (decimal)returns.Count(value => value > 0m) / windows.Count,
            windows.Max(window => window.MaxDrawdown),
            windows.Average(window => window.Turnover));
    }

    private static string? GetReportedRuleVersion(
        IReadOnlyList<BacktestWindowSummary> windows)
    {
        if (windows.Count == 0)
        {
            return null;
        }

        var versions = windows
            .Select(window => window.RuleVersion)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return versions.Length == 1 ? versions[0] : "mixed";
    }

    private static bool HasOverlappingWindows(
        IReadOnlyList<BacktestWindowSummary> windows)
    {
        for (var index = 1; index < windows.Count; index++)
        {
            if (windows[index - 1].EndUtc > windows[index].StartUtc)
            {
                return true;
            }
        }

        return false;
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2m
            : ordered[middle];
    }

    private static void ValidateResult(RollingBacktestResult result)
    {
        var startUtc = result.StartUtc.ToUniversalTime();
        var endUtc = result.EndUtc.ToUniversalTime();
        if (result.StartUtc == default || result.EndUtc == default || startUtc >= endUtc)
        {
            throw new ArgumentException("回测窗口必须是非空且 StartUtc 早于 EndUtc。", nameof(result));
        }

        if (result.InitialCapital <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(result), "回测初始资金必须大于零。");
        }

        if (result.FinalEquity < 0m || result.FinalCash < 0m || result.FinalPositionQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(result), "回测最终权益、现金和持仓不能为负数。");
        }

        if (result.DecisionCount < 0 || result.TradeCount < 0 || result.TradeCount > result.DecisionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(result), "回测决策数和交易数必须非负，交易数不能超过决策数。");
        }

        if (result.TotalReturn < -1m || result.MaxDrawdown < 0m || result.MaxDrawdown > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(result), "收益必须不低于 -100%，最大回撤必须位于 [0, 1]。");
        }

        if (result.Turnover < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(result), "换手不能为负数。");
        }

        if (result.TradingCostRate < 0m
            || result.TradingCostRate >= 1m
            || result.SlippageRate < 0m
            || result.SlippageRate >= 1m
            || result.TradingCostRate + result.SlippageRate >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(result), "交易成本率和滑点率必须位于有效区间。");
        }

        if (string.IsNullOrWhiteSpace(result.RuleVersion))
        {
            throw new ArgumentException("回测必须包含规则版本。", nameof(result));
        }

        if (result.Records is null)
        {
            throw new ArgumentException("回测记录集合不能为空。", nameof(result));
        }

        decimal expectedFinalEquity;
        try
        {
            expectedFinalEquity = result.InitialCapital * (1m + result.TotalReturn);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(result), "回测收益超出可验证范围。");
        }

        if (result.FinalEquity != expectedFinalEquity)
        {
            throw new ArgumentException("回测最终权益与总收益不一致。", nameof(result));
        }
    }
}
