using MH.Client.ViewModels;
using MH.Core.Contracts;

namespace MH.Tests;

public sealed class ClientServerMarketProfileTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task RefreshDisplaysPlayerFocusedProxyText()
    {
        var api = new FakeMarketApi();
        var viewModel = CreateViewModel(api);

        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.Equal(1, api.ServerMarketProfileCalls);
        Assert.Contains("区服活跃度：高", viewModel.ActivityIndexText, StringComparison.Ordinal);
        Assert.Contains("高价值需求：中", viewModel.HighValueDemandIndexText, StringComparison.Ordinal);
        Assert.Contains("在售数量变化频率", viewModel.ServerMarketProfileEvidenceText, StringComparison.Ordinal);
        Assert.Contains("不代表真实在线人数", viewModel.ServerMarketProfileNoticeText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OptionalProfileFailureKeepsMainMarketReadyAndOnlyReusesSameScope()
    {
        var api = new FakeMarketApi();
        var viewModel = CreateViewModel(api);
        await viewModel.RefreshAsync();
        var previous = viewModel.SelectedServerMarketProfile;

        api.ServerMarketProfileFailure = new HttpRequestException("profile unavailable");
        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.Same(previous, viewModel.SelectedServerMarketProfile);
        Assert.Contains("显示上次同区同一时点结果", viewModel.ServerMarketProfileErrorText, StringComparison.Ordinal);

        viewModel.SelectedAsOfUtc = AsOfUtc.AddDays(1);
        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.Null(viewModel.SelectedServerMarketProfile);
        Assert.Equal("区服观察暂时不可用。", viewModel.ServerMarketProfileErrorText);
    }

    [Fact]
    public async Task StaleProxyUsesSafeUnavailableText()
    {
        var staleMetric = new ServerProxyMetric(
            ServerProxyAvailability.Stale,
            null,
            ServerProxyLevel.Unknown,
            0m,
            50,
            10,
            0,
            72m,
            [],
            "stale-data");
        var api = new FakeMarketApi
        {
            ServerMarketProfile = new ServerMarketProfileResponse(
                "server-1",
                AsOfUtc,
                7,
                "server-market-profile-v1",
                staleMetric,
                staleMetric,
                "不代表真实在线人数。")
        };
        var viewModel = CreateViewModel(api);

        await viewModel.RefreshAsync();

        Assert.Contains("暂不可判断", viewModel.ActivityIndexText, StringComparison.Ordinal);
        Assert.Contains("数据已过期", viewModel.HighValueDemandIndexText, StringComparison.Ordinal);
        Assert.DoesNotContain("/100", viewModel.ActivityIndexText, StringComparison.Ordinal);
    }

    private static FirstScreenViewModel CreateViewModel(FakeMarketApi api)
        => new(api, () => AsOfUtc.AddHours(1))
        {
            SelectedServerId = "server-1",
            SelectedItemId = "item-1",
            SelectedAsOfUtc = AsOfUtc
        };
}
