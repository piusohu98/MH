using Microsoft.EntityFrameworkCore;
using MH.Core;
using MH.Core.Backtesting;
using MH.Core.Contracts;
using MH.Core.Models;
using MH.Core.Recommendations;
using MH.Server.Data;

namespace MH.Server.Services;

public sealed class RecommendationPreviewService(MarketDbContext db)
{
    public const decimal InitialCapital = 100_000m;
    public const decimal TradingCostRate = 0.01m;
    public const decimal SlippageRate = 0.005m;
    public const int WindowCount = 3;
    public const int WindowDays = 40;
    public const int WarmupDays = 30;
    public const int HistoryLookbackDays = WindowCount * WindowDays + WarmupDays + 1;
    public const string ScopeNotice = "只读研究预览；固定三窗口回测不代表真实获利保证。";

    public async Task<RecommendationPreviewResponse?> BuildAsync(
        string serverId,
        string itemId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        var cutoffUtc = asOfUtc.ToUniversalTime();
        var marketExists = await db.Servers.AsNoTracking()
            .Where(server => server.Id == serverId)
            .Join(
                db.Items.AsNoTracking().Where(item => item.Id == itemId),
                server => server.CatalogKind,
                item => item.CatalogKind,
                (_, _) => 1)
            .AnyAsync(cancellationToken);
        if (!marketExists)
        {
            return null;
        }

        var historyStartUtc = cutoffUtc.AddDays(-HistoryLookbackDays);
        var observations = await db.ListingObservations.AsNoTracking()
            .Where(observation => observation.ServerId == serverId
                && observation.ItemId == itemId
                && observation.ObservedAtUtc >= historyStartUtc
                && observation.ObservedAtUtc <= cutoffUtc)
            .OrderBy(observation => observation.ObservedAtUtc)
            .ToListAsync(cancellationToken);
        var dailyBars = PriceBarAggregator.Aggregate(observations);
        var indicators = RobustMarketAnalyzer.Analyze(dailyBars, cutoffUtc);
        var decision = RecommendationRule.Evaluate(indicators, cutoffUtc);
        var qualityGate = BacktestQualityGate.Evaluate(CreateBacktestWindows(dailyBars, cutoffUtc));
        var isActionable = qualityGate.Status == BacktestQualityStatus.TrialEligible
            && decision.Action is RecommendationAction.CandidateBuy or RecommendationAction.CandidateSell;

        return new RecommendationPreviewResponse(
            serverId,
            itemId,
            cutoffUtc,
            decision,
            isActionable,
            qualityGate,
            new RecommendationPreviewResearchAssumptions(
                InitialCapital,
                TradingCostRate,
                SlippageRate,
                WindowCount,
                WindowDays,
                WarmupDays,
                ScopeNotice));
    }

    private static IReadOnlyList<RollingBacktestResult> CreateBacktestWindows(
        IReadOnlyList<PriceBar> dailyBars,
        DateTimeOffset cutoffUtc)
    {
        var windows = new List<RollingBacktestResult>(WindowCount);
        for (var index = 0; index < WindowCount; index++)
        {
            var endUtc = cutoffUtc.AddDays(-index * WindowDays);
            var startUtc = endUtc.AddDays(-WindowDays);
            var windowBars = dailyBars
                .Where(bar => bar.EndUtc < startUtc
                    || bar.EndUtc > startUtc && bar.EndUtc <= endUtc)
                .ToArray();
            windows.Add(RollingBacktest.Run(
                windowBars,
                new RollingBacktestParameters(
                    startUtc,
                    endUtc,
                    InitialCapital,
                    TradingCostRate,
                    SlippageRate,
                    RecommendationRule.RuleVersion)));
        }

        return windows;
    }
}
