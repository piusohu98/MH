using System.Globalization;
using MH.Client.ViewModels;
using MH.Core.Backtesting;
using MH.Core.Contracts;
using MH.Core.Recommendations;

namespace MH.Tests;

public sealed class ClientPlayerPresentationTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset LastSuccessUtc = new(2025, 1, 2, 4, 5, 6, TimeSpan.Zero);

    [Fact]
    public async Task LatestPlayerPriceUsesLatestBarWhenSeriesIsUnordered()
    {
        var latestEndUtc = AsOfUtc.AddDays(2);
        var api = CreateApi(
            bars:
            [
                new PriceBarDto(latestEndUtc.AddDays(-1), latestEndUtc, 118, 130, 110, 120, 20, false),
                new PriceBarDto(AsOfUtc.AddDays(-1), AsOfUtc, 98, 108, 90, 100, 10, false)
            ]);
        var viewModel = await LoadAsync(api);

        Assert.Equal(120, viewModel.CurrentReferencePrice);
        Assert.Equal(110, viewModel.LatestLowPrice);
        Assert.Equal(130, viewModel.LatestHighPrice);
        Assert.Equal(latestEndUtc, viewModel.LatestCollectionEndUtc);
        Assert.Contains("120 金币", viewModel.CurrentReferencePriceText, StringComparison.Ordinal);
        Assert.Contains("110", viewModel.LatestRangeText, StringComparison.Ordinal);
        Assert.Contains("130", viewModel.LatestRangeText, StringComparison.Ordinal);
        Assert.Contains("采集样本", viewModel.RelativePriceText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(94, "价格偏低")]
    [InlineData(100, "接近近期常见价")]
    [InlineData(106, "价格偏高")]
    public async Task RelativePriceUsesSevenDayMedianThresholds(int close, string expected)
    {
        var api = CreateApi(
            bars: [new PriceBarDto(AsOfUtc.AddDays(-1), AsOfUtc, close, close + 2, close - 2, close, 10, false)],
            median7Days: 100m);
        var viewModel = await LoadAsync(api);

        Assert.Contains(expected, viewModel.RelativePriceText, StringComparison.Ordinal);
        Assert.Contains("不是官方实时最低价", viewModel.RelativePriceText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.03, "上涨")]
    [InlineData(-0.03, "下跌")]
    [InlineData(0.029, "基本平稳")]
    public async Task PriceChangeUsesPlayerFriendlyTrendThresholds(double change, string expected)
    {
        var viewModel = await LoadAsync(CreateApi(return7Days: (decimal)change));

        Assert.Contains("近 7 天价格变化", viewModel.PriceChange7Text, StringComparison.Ordinal);
        Assert.Contains(expected, viewModel.PriceChange7Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.08, "较稳定")]
    [InlineData(0.0801, "有一定波动")]
    [InlineData(0.18, "有一定波动")]
    [InlineData(0.1801, "波动较大")]
    public async Task PriceStabilityUsesPlayerFriendlyThresholds(double volatility, string expected)
    {
        var viewModel = await LoadAsync(CreateApi(volatility7Days: (decimal)volatility));

        Assert.Contains("价格稳定性", viewModel.PriceStability7Text, StringComparison.Ordinal);
        Assert.Contains(expected, viewModel.PriceStability7Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.25, "明显变多")]
    [InlineData(0.05, "小幅变多")]
    [InlineData(0.0499, "变化不大")]
    [InlineData(-0.05, "变化不大")]
    [InlineData(-0.0501, "小幅变少")]
    [InlineData(-0.25, "明显变少")]
    public async Task VisibleSupplyUsesPlayerFriendlyThresholds(double change, string expected)
    {
        var viewModel = await LoadAsync(CreateApi(supply7Days: (decimal)change));

        Assert.Contains("在售数量变化（采集代理）", viewModel.SupplyChange7Text, StringComparison.Ordinal);
        Assert.Contains(expected, viewModel.SupplyChange7Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.75, "参考程度：高")]
    [InlineData(0.7499, "参考程度：中")]
    [InlineData(0.5, "参考程度：中")]
    [InlineData(0.4999, "参考程度：低")]
    public async Task ConfidenceUsesPlayerFriendlyReferenceLevel(double confidence, string expected)
    {
        var viewModel = await LoadAsync(CreateApi(
            preview: FakeMarketApi.CreatePreview(
                RecommendationAction.Observe,
                false,
                BacktestQualityStatus.ResearchOnly,
                (decimal)confidence)));

        Assert.Equal(expected, viewModel.ReferenceLevelText);
    }

    [Theory]
    [InlineData(RecommendationAction.DataInsufficient, "数据还不够，暂时别囤")]
    [InlineData(RecommendationAction.Observe, "先观察，暂不囤货")]
    [InlineData(RecommendationAction.CandidateBuy, "可以考虑少量囤货")]
    [InlineData(RecommendationAction.Hold, "已有库存可继续观察")]
    [InlineData(RecommendationAction.CandidateSell, "可以考虑分批出售")]
    [InlineData(RecommendationAction.Avoid, "风险较高，不建议囤货")]
    public async Task RecommendationUsesPlayerFriendlyActionText(
        RecommendationAction action,
        string expected)
    {
        var api = CreateApi(preview: FakeMarketApi.CreatePreview(action, true, BacktestQualityStatus.TrialEligible));
        var viewModel = await LoadAsync(api);

        Assert.Equal(expected, viewModel.ActionText);
        Assert.DoesNotContain("门禁", viewModel.ActionabilityText, StringComparison.Ordinal);
        Assert.DoesNotContain("可执行", viewModel.ActionabilityText, StringComparison.Ordinal);
        Assert.DoesNotContain("交易", viewModel.ActionabilityText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonActionableCandidateBuyIsDisplayedAsObservation()
    {
        var api = CreateApi(preview: FakeMarketApi.CreatePreview(
            RecommendationAction.CandidateBuy,
            false,
            BacktestQualityStatus.ResearchOnly));
        var viewModel = await LoadAsync(api);

        Assert.False(viewModel.IsActionable);
        Assert.Equal(RecommendationAction.Observe, viewModel.DisplayedRecommendationAction);
        Assert.Equal("先观察，暂不囤货", viewModel.ActionText);
        Assert.DoesNotContain("门禁", viewModel.ActionabilityText, StringComparison.Ordinal);
        Assert.DoesNotContain("可执行", viewModel.ActionabilityText, StringComparison.Ordinal);
        Assert.DoesNotContain("交易", viewModel.ActionabilityText, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptySnapshotUsesExplicitInsufficientDataText()
    {
        var viewModel = new FirstScreenViewModel(new FakeMarketApi(), () => LastSuccessUtc);

        Assert.Equal("数据不足", viewModel.CurrentReferencePriceText);
        Assert.Equal("数据不足", viewModel.LatestRangeText);
        Assert.Contains("数据不足", viewModel.RelativePriceText, StringComparison.Ordinal);
        Assert.Equal("数据不足", viewModel.PriceChange7Text);
        Assert.Equal("数据不足", viewModel.PriceStability7Text);
        Assert.Equal("数据不足", viewModel.SupplyChange7Text);
        Assert.Equal("等待行情", viewModel.ActionText);
    }

    [Fact]
    public async Task NullablePlayerMetricsDoNotDisplayMisleadingValues()
    {
        var viewModel = await LoadAsync(CreateApi(
            median7Days: null,
            return7Days: null,
            volatility7Days: null,
            supply7Days: null));

        Assert.Contains("数据不足", viewModel.RelativePriceText, StringComparison.Ordinal);
        Assert.Equal("数据不足", viewModel.PriceChange7Text);
        Assert.Equal("数据不足", viewModel.PriceStability7Text);
        Assert.Equal("数据不足", viewModel.SupplyChange7Text);
    }

    [Fact]
    public async Task LastSuccessfulTextUsesLocalMachineTimeWithoutSeconds()
    {
        var viewModel = await LoadAsync(CreateApi());

        Assert.Equal(
            LastSuccessUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            viewModel.LastSuccessfulText);
    }

    private static FakeMarketApi CreateApi(
        IReadOnlyList<PriceBarDto>? bars = null,
        decimal? median7Days = 100m,
        decimal? return7Days = 0.05m,
        decimal? volatility7Days = 0.02m,
        decimal? supply7Days = -0.1m,
        RecommendationPreviewResponse? preview = null)
        => new()
        {
            Series = new MarketSeriesResponse("server-1", "item-1", null, null, bars ??
            [new PriceBarDto(AsOfUtc.AddDays(-1), AsOfUtc, 100, 110, 90, 100, 10, false)]),
            RobustMedian7Days = median7Days,
            Return7Days = return7Days,
            Volatility7Days = volatility7Days,
            VisibleSupplyChange7Days = supply7Days,
            Preview = preview
        };

    private static async Task<FirstScreenViewModel> LoadAsync(FakeMarketApi api)
    {
        var viewModel = new FirstScreenViewModel(api, () => LastSuccessUtc)
        {
            SelectedServerId = "server-1",
            SelectedItemId = "item-1",
            SelectedAsOfUtc = AsOfUtc
        };

        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        return viewModel;
    }
}
