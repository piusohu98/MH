namespace MH.Client.ViewModels;

public enum MarketOverlayDataState
{
    NoSnapshot = 0,
    Ready = 1,
    Offline = 2,
    Stale = 3
}

public sealed record MarketOverlayProjection(
    MarketOverlayDataState State,
    string StateText,
    string ServerName,
    string ItemName,
    string ReferencePriceText,
    string LatestRangeText,
    string CollectionCutoffText,
    string ClientRefreshText,
    string DataAgeText,
    string RecommendationText,
    string SafetyText)
{
    public static MarketOverlayProjection From(FirstScreenViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (viewModel.Snapshot is null)
        {
            return Empty;
        }

        var snapshot = viewModel.Snapshot;
        var serverId = snapshot.Series.ServerId;
        var itemId = snapshot.Series.ItemId;
        var serverName = snapshot.Catalog.Servers
            .FirstOrDefault(server => string.Equals(server.Id, serverId, StringComparison.Ordinal))
            ?.Name ?? serverId;
        var itemName = snapshot.Catalog.Items
            .FirstOrDefault(item => string.Equals(item.Id, itemId, StringComparison.Ordinal))
            ?.Name ?? itemId;
        var state = ResolveDataState(true, viewModel.State, viewModel.IsStale);
        var stateText = state switch
        {
            MarketOverlayDataState.Ready => "已加载快照",
            MarketOverlayDataState.Offline => "离线快照",
            MarketOverlayDataState.Stale => "数据陈旧",
            _ => "暂无缓存行情"
        };
        var recommendationText = viewModel.IsActionable && state == MarketOverlayDataState.Ready
            ? "行情已加载，仅供人工判断"
            : viewModel.ActionText;

        return new(
            state,
            stateText,
            serverName,
            itemName,
            viewModel.CurrentReferencePriceText,
            viewModel.LatestRangeText,
            viewModel.PriceCollectionCutoffText,
            viewModel.LastSuccessfulText,
            viewModel.DataAgeText,
            recommendationText,
            "缓存行情；未接入屏幕 OCR，不读取屏幕、不截图、不自动切换商品。");
    }

    public static MarketOverlayProjection Empty { get; } = new(
        MarketOverlayDataState.NoSnapshot,
        "暂无缓存行情",
        "—",
        "—",
        "数据不足",
        "数据不足",
        "数据不足",
        "暂无成功刷新",
        "无数据",
        "等待行情",
        "请先在主窗口加载行情；未接入屏幕 OCR，不会根据游戏画面猜测商品。");

    public static MarketOverlayDataState ResolveDataState(
        bool hasSnapshot,
        MarketViewState state,
        bool isStale)
        => !hasSnapshot
            ? MarketOverlayDataState.NoSnapshot
            : isStale
                ? MarketOverlayDataState.Stale
                : state == MarketViewState.Offline
                    ? MarketOverlayDataState.Offline
                    : state == MarketViewState.Ready
                        ? MarketOverlayDataState.Ready
                        : MarketOverlayDataState.Stale;
}
