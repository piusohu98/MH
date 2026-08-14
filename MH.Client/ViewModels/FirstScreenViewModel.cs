using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using MH.Client.Api;
using MH.Core.Backtesting;
using MH.Core.Contracts;
using MH.Core.Recommendations;

namespace MH.Client.ViewModels;

public sealed class FirstScreenViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<ServerDto> EmptyServers = Array.Empty<ServerDto>();
    private static readonly IReadOnlyList<ItemDto> EmptyItems = Array.Empty<ItemDto>();
    private static readonly IReadOnlyList<RecommendationReason> EmptyRecommendationReasons = Array.Empty<RecommendationReason>();
    private static readonly IReadOnlyList<BacktestQualityReason> EmptyGateReasons = Array.Empty<BacktestQualityReason>();

    private readonly IReadOnlyMarketApiClient apiClient;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly object refreshSync = new();
    private CancellationTokenSource? activeRefreshCancellation;
    private long refreshVersion;
    private CatalogResponse? catalog;
    private string? selectedServerId;
    private string? selectedItemId;
    private DateTimeOffset? selectedAsOfUtc;
    private string selectedAsOfUtcText = string.Empty;
    private string? asOfInputError;
    private MarketViewState state;
    private MarketScreenSnapshot? snapshot;
    private DateTimeOffset? lastSuccessfulAtUtc;
    private bool isStale;
    private string? errorMessage;

    public FirstScreenViewModel(
        IReadOnlyMarketApiClient apiClient,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MarketViewState State
    {
        get => state;
        private set
        {
            if (state == value)
            {
                return;
            }

            state = value;
            OnPropertyChanged();
            NotifyPresentationProperties();
        }
    }

    public string? SelectedServerId
    {
        get => selectedServerId;
        set
        {
            if (selectedServerId == value)
            {
                return;
            }

            selectedServerId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRefresh));
        }
    }

    public string? SelectedItemId
    {
        get => selectedItemId;
        set
        {
            if (selectedItemId == value)
            {
                return;
            }

            selectedItemId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRefresh));
        }
    }

    public DateTimeOffset? SelectedAsOfUtc
    {
        get => selectedAsOfUtc;
        set
        {
            if (selectedAsOfUtc == value)
            {
                return;
            }

            selectedAsOfUtc = value;
            selectedAsOfUtcText = value.HasValue ? FormatAsOf(value.Value) : string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAsOfUtcText));
            OnPropertyChanged(nameof(SelectedAsOfDisplayText));
            OnPropertyChanged(nameof(CanRefresh));
            AsOfInputError = null;
        }
    }

    public string SelectedAsOfUtcText
    {
        get => selectedAsOfUtcText;
        set
        {
            var normalizedText = value ?? string.Empty;
            if (selectedAsOfUtcText == normalizedText)
            {
                return;
            }

            selectedAsOfUtcText = normalizedText;
            OnPropertyChanged();
            if (DateTimeOffset.TryParse(
                normalizedText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
            {
                selectedAsOfUtc = parsed.ToUniversalTime();
                OnPropertyChanged(nameof(SelectedAsOfUtc));
                OnPropertyChanged(nameof(SelectedAsOfDisplayText));
                OnPropertyChanged(nameof(CanRefresh));
                AsOfInputError = null;
            }
            else
            {
                selectedAsOfUtc = null;
                OnPropertyChanged(nameof(SelectedAsOfUtc));
                OnPropertyChanged(nameof(SelectedAsOfDisplayText));
                OnPropertyChanged(nameof(CanRefresh));
                AsOfInputError = "请输入带时区的 ISO-8601 时间，例如 2025-01-02T03:04:05Z。";
            }
        }
    }

    public string? AsOfInputError
    {
        get => asOfInputError;
        private set
        {
            if (asOfInputError == value)
            {
                return;
            }

            asOfInputError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRefresh));
        }
    }

    public CatalogResponse? Catalog
    {
        get => catalog;
        private set
        {
            catalog = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Servers));
            OnPropertyChanged(nameof(Items));
        }
    }

    public IReadOnlyList<ServerDto> Servers => Catalog?.Servers ?? EmptyServers;

    public IReadOnlyList<ItemDto> Items => Catalog?.Items ?? EmptyItems;

    public MarketScreenSnapshot? Snapshot
    {
        get => snapshot;
        private set
        {
            snapshot = value;
            OnPropertyChanged();
            NotifyPresentationProperties();
        }
    }

    public DateTimeOffset? LastSuccessfulAtUtc
    {
        get => lastSuccessfulAtUtc;
        private set
        {
            lastSuccessfulAtUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastSuccessfulText));
        }
    }

    public bool IsStale
    {
        get => isStale;
        private set
        {
            if (isStale == value)
            {
                return;
            }

            isStale = value;
            OnPropertyChanged();
            NotifyPresentationProperties();
        }
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (errorMessage == value)
            {
                return;
            }

            errorMessage = value;
            OnPropertyChanged();
        }
    }

    public RecommendationAction? RawRecommendationAction => Snapshot?.Recommendation.Decision.Action;

    public RecommendationAction? DisplayedRecommendationAction
    {
        get
        {
            var recommendation = Snapshot?.Recommendation;
            if (recommendation is null)
            {
                return null;
            }

            return !IsActionable
                && recommendation.Decision.Action is RecommendationAction.CandidateBuy or RecommendationAction.CandidateSell
                ? RecommendationAction.Observe
                : recommendation.Decision.Action;
        }
    }

    public bool IsActionable
        => State == MarketViewState.Ready
            && !IsStale
            && Snapshot?.Recommendation.IsActionable == true;

    public BacktestQualityStatus? GateStatus => Snapshot?.Recommendation.QualityGate.Status;

    public string? GateVersion => Snapshot?.Recommendation.QualityGate.GateVersion;

    public string? RuleVersion => Snapshot?.Recommendation.Decision.RuleVersion
        ?? Snapshot?.Recommendation.QualityGate.RuleVersion;

    public IReadOnlyList<BacktestQualityReason> GateReasons
        => Snapshot?.Recommendation.QualityGate.Reasons ?? EmptyGateReasons;

    public IReadOnlyList<RecommendationReason> RecommendationReasons
        => Snapshot?.Recommendation.Decision.Reasons ?? EmptyRecommendationReasons;

    public decimal? DataAgeHours => Snapshot?.Indicators.DataAgeHours;

    public string? ResearchNotice => Snapshot?.Recommendation.ResearchAssumptions.ScopeNotice;

    public bool CanInitialize => State != MarketViewState.Loading;

    public bool CanRefresh => CanInitialize
        && !string.IsNullOrWhiteSpace(SelectedServerId)
        && !string.IsNullOrWhiteSpace(SelectedItemId)
        && SelectedAsOfUtc.HasValue
        && AsOfInputError is null;

    public string StateText => State switch
    {
        MarketViewState.Idle => "未加载",
        MarketViewState.Loading => "加载中…",
        MarketViewState.Ready => "已就绪",
        MarketViewState.Offline => "离线（保留上次数据）",
        MarketViewState.Error => "需要处理",
        _ => "未知状态"
    };

    public string StatusText => IsStale ? $"{StateText} · 数据陈旧" : StateText;

    public string SelectedAsOfDisplayText
        => SelectedAsOfUtc.HasValue ? FormatAsOf(SelectedAsOfUtc.Value) : "未选择";

    public string LastSuccessfulText
        => LastSuccessfulAtUtc.HasValue
            ? LastSuccessfulAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "暂无成功刷新";

    public int? CurrentReferencePrice => GetLatestBar()?.Close;

    public int? LatestLowPrice => GetLatestBar()?.Low;

    public int? LatestHighPrice => GetLatestBar()?.High;

    public DateTimeOffset? LatestCollectionEndUtc => GetLatestBar()?.EndUtc.ToUniversalTime();

    public string CurrentReferencePriceText
        => CurrentReferencePrice.HasValue
            ? $"{CurrentReferencePrice.Value.ToString("N0", CultureInfo.InvariantCulture)} 金币"
            : "数据不足";

    public string LatestRangeText
        => LatestLowPrice.HasValue && LatestHighPrice.HasValue
            ? $"{LatestLowPrice.Value.ToString("N0", CultureInfo.InvariantCulture)} ~ {LatestHighPrice.Value.ToString("N0", CultureInfo.InvariantCulture)} 金币"
            : "数据不足";

    public string PriceCollectionCutoffText
        => LatestCollectionEndUtc.HasValue ? FormatAsOf(LatestCollectionEndUtc.Value) : "数据不足";

    public string CollectionCutoffText => PriceCollectionCutoffText;

    public string RelativePriceText
    {
        get
        {
            var currentPrice = CurrentReferencePrice;
            var median = Snapshot?.Indicators.RobustMedian7Days;
            if (!currentPrice.HasValue || !median.HasValue || median.Value <= 0)
            {
                return "数据不足：暂无足够采集样本；不是官方实时最低价。";
            }

            var difference = currentPrice.Value / median.Value - 1m;
            var conclusion = difference < -0.05m
                ? "价格偏低"
                : difference > 0.05m
                    ? "价格偏高"
                    : "接近近期常见价";
            return $"{conclusion}（相对近 7 天常见价 {FormatPercent(difference)}，基于采集样本；不是官方实时最低价）";
        }
    }

    public string PriceChange7Text
    {
        get
        {
            var change = Snapshot?.Indicators.Return7Days;
            if (!change.HasValue)
            {
                return "数据不足";
            }

            var trend = change.Value >= 0.03m
                ? "上涨"
                : change.Value <= -0.03m
                    ? "下跌"
                    : "基本平稳";
            return $"近 7 天价格变化：{FormatPercent(change)}（{trend}）";
        }
    }

    public string PriceStability7Text
    {
        get
        {
            var volatility = Snapshot?.Indicators.Volatility7Days;
            if (!volatility.HasValue)
            {
                return "数据不足";
            }

            var stability = volatility.Value <= 0.08m
                ? "较稳定"
                : volatility.Value <= 0.18m
                    ? "有一定波动"
                    : "波动较大";
            return $"价格稳定性：{stability}（{FormatPercent(volatility)}）";
        }
    }

    public string SupplyChange7Text
    {
        get
        {
            var change = Snapshot?.Indicators.VisibleSupplyChange7Days;
            if (!change.HasValue)
            {
                return "数据不足";
            }

            var supply = change.Value >= 0.25m
                ? "明显变多"
                : change.Value >= 0.05m
                    ? "小幅变多"
                    : change.Value >= -0.05m
                        ? "变化不大"
                        : change.Value > -0.25m
                            ? "小幅变少"
                            : "明显变少";
            return $"在售数量变化（采集代理）：{FormatPercent(change)}（{supply}）";
        }
    }

    public string ReferenceLevelText
    {
        get
        {
            var confidence = Snapshot?.Recommendation.Decision.Confidence;
            if (!confidence.HasValue)
            {
                return "参考程度：数据不足";
            }

            var level = confidence.Value >= 0.75m
                ? "高"
                : confidence.Value >= 0.5m
                    ? "中"
                    : "低";
            return $"参考程度：{level}";
        }
    }

    public string Median7Text => FormatNumber(Snapshot?.Indicators.RobustMedian7Days);

    public string Median30Text => FormatNumber(Snapshot?.Indicators.RobustMedian30Days);

    public string Return7Text => FormatPercent(Snapshot?.Indicators.Return7Days);

    public string Return30Text => FormatPercent(Snapshot?.Indicators.Return30Days);

    public string Volatility7Text => FormatPercent(Snapshot?.Indicators.Volatility7Days);

    public string Volatility30Text => FormatPercent(Snapshot?.Indicators.Volatility30Days);

    public string Supply7Text => FormatPercent(Snapshot?.Indicators.VisibleSupplyChange7Days);

    public string Supply30Text => FormatPercent(Snapshot?.Indicators.VisibleSupplyChange30Days);

    public string DataAgeText
        => DataAgeHours.HasValue ? $"{DataAgeHours.Value.ToString("0.##", CultureInfo.InvariantCulture)} 小时" : "无数据";

    public string OcrAnomalyText
    {
        get
        {
            var count = Snapshot?.Series.Bars.Count(bar => bar.HasOcrAnomaly) ?? 0;
            return count == 0 ? "可能识别异常：无" : $"可能识别异常：有 {count} 个采集点（仅提醒，不自动修正）";
        }
    }

    public string ActionText => GetActionText(DisplayedRecommendationAction);

    public string ActionabilityText
        => IsActionable
            ? "数据和历史模拟达到试用标准，也只能少量尝试并由你人工判断。"
            : "数据或历史表现还不足，建议继续观察，不要仅凭本工具囤货或出售。";

    public string GateStatusText => GetGateStatusText(GateStatus);

    public string DirectionScoreText
        => Snapshot is null ? "—" : Snapshot.Recommendation.Decision.DirectionScore.ToString(CultureInfo.InvariantCulture);

    public string ConfidenceText => FormatPercent(Snapshot?.Recommendation.Decision.Confidence);

    public string MaxSuggestedPositionText
        => Snapshot?.Recommendation.Decision.MaxSuggestedPosition is decimal position
            ? $"最多占用金币比例：{FormatPercent(position)}"
            : "数据不足";

    public string ReasonsText
        => RecommendationReasons.Count == 0
            ? "暂无结构化理由。"
            : string.Join(Environment.NewLine, RecommendationReasons.Select(reason => $"• {reason.Detail}"));

    public string InvalidationConditionsText
        => Snapshot is null || Snapshot.Recommendation.Decision.InvalidationConditions.Count == 0
            ? "暂无失效条件。"
            : string.Join(Environment.NewLine, Snapshot.Recommendation.Decision.InvalidationConditions.Select(condition => $"• {condition}"));

    public string GateSummaryText
    {
        get
        {
            var summary = Snapshot?.Recommendation.QualityGate.Summary;
            return summary is null
                ? "暂无门禁摘要。"
                : $"窗口 {summary.WindowCount} 个 · 有利阶段 {FormatPercent(summary.ProfitableWindowRatio)} · "
                  + $"典型模拟结果 {FormatPercent(summary.MedianReturn)} · 最差回落 {FormatPercent(summary.WorstMaxDrawdown)} · "
                  + $"库存调整强度 {summary.AverageTurnover.ToString("0.##", CultureInfo.InvariantCulture)}";
        }
    }

    public string ResearchNoticeText
        => ResearchNotice ?? "只读研究预览；不代表真实获利保证。";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var request = BeginRequest(cancellationToken);
        using var requestCancellation = request.Token;
        CancelPrevious(request.Previous);
        State = MarketViewState.Loading;
        ErrorMessage = null;

        try
        {
            var loadedCatalog = await apiClient.GetCatalogAsync(cancellationToken: requestCancellation.Token);
            if (!IsCurrent(request.Version))
            {
                return;
            }

            Catalog = loadedCatalog;
            if (loadedCatalog.Servers.Count == 0 || loadedCatalog.Items.Count == 0)
            {
                SelectedServerId = null;
                SelectedItemId = null;
                SelectedAsOfUtc = null;
                ApplyNoDataError("DEMO 目录为空，暂时没有可选择的区服或商品。");
                return;
            }

            var serverId = loadedCatalog.Servers[0].Id;
            var itemId = loadedCatalog.Items[0].Id;
            SelectedServerId = serverId;
            SelectedItemId = itemId;

            var series = await apiClient.GetSeriesAsync(
                serverId,
                itemId,
                fromUtc: null,
                toUtc: null,
                cancellationToken: requestCancellation.Token);
            if (!IsCurrent(request.Version))
            {
                return;
            }

            var latestBar = series.Bars.OrderByDescending(bar => bar.EndUtc).FirstOrDefault();
            if (latestBar is null)
            {
                SelectedAsOfUtc = null;
                ApplyNoDataError("所选商品没有历史行情，未请求指标和建议。");
                return;
            }

            SelectedAsOfUtc = latestBar.EndUtc.ToUniversalTime();
            var indicators = await apiClient.GetIndicatorsAsync(serverId, itemId, SelectedAsOfUtc.Value, requestCancellation.Token);
            var recommendation = await apiClient.GetRecommendationAsync(serverId, itemId, SelectedAsOfUtc.Value, requestCancellation.Token);
            if (!IsCurrent(request.Version))
            {
                return;
            }

            ApplyLoadedSnapshot(loadedCatalog, series, indicators, recommendation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || !IsCurrent(request.Version))
        {
            if (IsCurrent(request.Version) && cancellationToken.IsCancellationRequested)
            {
                RestoreAfterCallerCancellation();
            }
        }
        catch (Exception exception)
        {
            if (!IsCurrent(request.Version))
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                RestoreAfterCallerCancellation();
                return;
            }

            ApplyFailure(exception, preserveSnapshot: Snapshot is not null);
        }
        finally
        {
            EndRequest(request.Token);
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!TryGetSelection(out var serverId, out var itemId, out var asOfUtc))
        {
            CancelActiveRefresh();
            if (Snapshot is null)
            {
                ApplyFailure(new ArgumentException("请选择有效的服务器、商品和 UTC 历史时点。"), preserveSnapshot: false);
            }
            else
            {
                ErrorMessage = AsOfInputError ?? "请选择有效的服务器、商品和 UTC 历史时点。";
                State = IsStale ? MarketViewState.Offline : MarketViewState.Ready;
            }
            return;
        }

        var request = BeginRequest(cancellationToken);
        using var requestCancellation = request.Token;
        CancelPrevious(request.Previous);

        State = MarketViewState.Loading;
        ErrorMessage = null;

        try
        {
            var catalog = await apiClient.GetCatalogAsync(cancellationToken: requestCancellation.Token);
            var series = await apiClient.GetSeriesAsync(
                serverId,
                itemId,
                asOfUtc.AddDays(-30),
                asOfUtc,
                requestCancellation.Token);
            var indicators = await apiClient.GetIndicatorsAsync(serverId, itemId, asOfUtc, requestCancellation.Token);
            var recommendation = await apiClient.GetRecommendationAsync(serverId, itemId, asOfUtc, requestCancellation.Token);

            if (!IsCurrent(request.Version))
            {
                return;
            }

            ApplyLoadedSnapshot(catalog, series, indicators, recommendation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || !IsCurrent(request.Version))
        {
            if (IsCurrent(request.Version) && cancellationToken.IsCancellationRequested)
            {
                RestoreAfterCallerCancellation();
            }
        }
        catch (Exception exception)
        {
            if (!IsCurrent(request.Version))
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                RestoreAfterCallerCancellation();
                return;
            }

            ApplyFailure(exception, preserveSnapshot: Snapshot is not null);
        }
        finally
        {
            EndRequest(request.Token);
        }
    }

    private bool TryGetSelection(out string serverId, out string itemId, out DateTimeOffset asOfUtc)
    {
        serverId = SelectedServerId?.Trim() ?? string.Empty;
        itemId = SelectedItemId?.Trim() ?? string.Empty;
        asOfUtc = SelectedAsOfUtc?.ToUniversalTime() ?? default;
        return serverId.Length > 0
            && itemId.Length > 0
            && SelectedAsOfUtc.HasValue
            && AsOfInputError is null;
    }

    private void CancelActiveRefresh()
    {
        Interlocked.Increment(ref refreshVersion);
        CancellationTokenSource? active;
        lock (refreshSync)
        {
            active = activeRefreshCancellation;
            activeRefreshCancellation = null;
        }

        try
        {
            active?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool IsCurrent(long requestVersion)
        => Interlocked.Read(ref refreshVersion) == requestVersion;

    private (long Version, CancellationTokenSource Token, CancellationTokenSource? Previous) BeginRequest(
        CancellationToken cancellationToken)
    {
        var version = Interlocked.Increment(ref refreshVersion);
        var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previous;
        lock (refreshSync)
        {
            previous = activeRefreshCancellation;
            activeRefreshCancellation = token;
        }

        return (version, token, previous);
    }

    private static void CancelPrevious(CancellationTokenSource? previous)
    {
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void EndRequest(CancellationTokenSource token)
    {
        lock (refreshSync)
        {
            if (ReferenceEquals(activeRefreshCancellation, token))
            {
                activeRefreshCancellation = null;
            }
        }
    }

    private void ApplyLoadedSnapshot(
        CatalogResponse loadedCatalog,
        MarketSeriesResponse series,
        MarketIndicatorsResponse indicators,
        RecommendationPreviewResponse recommendation)
    {
        Catalog = loadedCatalog;
        Snapshot = new MarketScreenSnapshot(loadedCatalog, series, indicators, recommendation);
        LastSuccessfulAtUtc = utcNow().ToUniversalTime();
        IsStale = indicators.DataAgeHours is > RecommendationRule.MaxDataAgeHours;
        ErrorMessage = null;
        State = MarketViewState.Ready;
    }

    private void ApplyNoDataError(string message)
    {
        Snapshot = null;
        IsStale = false;
        ErrorMessage = message;
        State = MarketViewState.Error;
    }

    private void ApplyFailure(Exception exception, bool preserveSnapshot)
    {
        ErrorMessage = ToUserMessage(exception);
        if (preserveSnapshot)
        {
            IsStale = true;
            State = MarketViewState.Offline;
        }
        else
        {
            IsStale = false;
            Snapshot = null;
            State = MarketViewState.Error;
        }
    }

    private void RestoreAfterCallerCancellation()
    {
        ErrorMessage = null;
        State = Snapshot is null ? MarketViewState.Idle : IsStale ? MarketViewState.Offline : MarketViewState.Ready;
    }

    private void NotifyPresentationProperties()
    {
        OnPropertyChanged(nameof(RawRecommendationAction));
        OnPropertyChanged(nameof(DisplayedRecommendationAction));
        OnPropertyChanged(nameof(IsActionable));
        OnPropertyChanged(nameof(GateStatus));
        OnPropertyChanged(nameof(GateVersion));
        OnPropertyChanged(nameof(RuleVersion));
        OnPropertyChanged(nameof(GateReasons));
        OnPropertyChanged(nameof(RecommendationReasons));
        OnPropertyChanged(nameof(DataAgeHours));
        OnPropertyChanged(nameof(ResearchNotice));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CurrentReferencePrice));
        OnPropertyChanged(nameof(LatestLowPrice));
        OnPropertyChanged(nameof(LatestHighPrice));
        OnPropertyChanged(nameof(LatestCollectionEndUtc));
        OnPropertyChanged(nameof(CurrentReferencePriceText));
        OnPropertyChanged(nameof(LatestRangeText));
        OnPropertyChanged(nameof(PriceCollectionCutoffText));
        OnPropertyChanged(nameof(CollectionCutoffText));
        OnPropertyChanged(nameof(RelativePriceText));
        OnPropertyChanged(nameof(PriceChange7Text));
        OnPropertyChanged(nameof(PriceStability7Text));
        OnPropertyChanged(nameof(SupplyChange7Text));
        OnPropertyChanged(nameof(ReferenceLevelText));
        OnPropertyChanged(nameof(Median7Text));
        OnPropertyChanged(nameof(Median30Text));
        OnPropertyChanged(nameof(Return7Text));
        OnPropertyChanged(nameof(Return30Text));
        OnPropertyChanged(nameof(Volatility7Text));
        OnPropertyChanged(nameof(Volatility30Text));
        OnPropertyChanged(nameof(Supply7Text));
        OnPropertyChanged(nameof(Supply30Text));
        OnPropertyChanged(nameof(DataAgeText));
        OnPropertyChanged(nameof(OcrAnomalyText));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(ActionabilityText));
        OnPropertyChanged(nameof(GateStatusText));
        OnPropertyChanged(nameof(DirectionScoreText));
        OnPropertyChanged(nameof(ConfidenceText));
        OnPropertyChanged(nameof(MaxSuggestedPositionText));
        OnPropertyChanged(nameof(ReasonsText));
        OnPropertyChanged(nameof(InvalidationConditionsText));
        OnPropertyChanged(nameof(GateSummaryText));
        OnPropertyChanged(nameof(ResearchNoticeText));
        OnPropertyChanged(nameof(CanInitialize));
        OnPropertyChanged(nameof(CanRefresh));
    }

    private PriceBarDto? GetLatestBar()
        => Snapshot?.Series.Bars
            .OrderByDescending(bar => bar.EndUtc)
            .FirstOrDefault();

    private static string FormatAsOf(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string FormatNumber(decimal? value)
        => value.HasValue ? value.Value.ToString("N0", CultureInfo.InvariantCulture) : "—";

    private static string FormatPercent(decimal? value)
        => value.HasValue ? value.Value.ToString("P1", CultureInfo.InvariantCulture) : "—";

    private static string GetActionText(RecommendationAction? action)
        => action switch
        {
            RecommendationAction.DataInsufficient => "数据还不够，暂时别囤",
            RecommendationAction.Observe => "先观察，暂不囤货",
            RecommendationAction.CandidateBuy => "可以考虑少量囤货",
            RecommendationAction.Hold => "已有库存可继续观察",
            RecommendationAction.CandidateSell => "可以考虑分批出售",
            RecommendationAction.Avoid => "风险较高，不建议囤货",
            _ => "等待行情"
        };

    private static string GetGateStatusText(BacktestQualityStatus? status)
        => status switch
        {
            BacktestQualityStatus.ResearchOnly => "仅研究（不可执行）",
            BacktestQualityStatus.Disabled => "已禁用（不可执行）",
            BacktestQualityStatus.TrialEligible => "可小额人工试用",
            _ => "暂无门禁结果"
        };

    private static string ToUserMessage(Exception exception)
        => exception switch
        {
            ArgumentException => "请选择有效的服务器、商品和 UTC 历史时点。",
            HttpRequestException => "无法连接市场服务，请检查服务状态或网络。",
            TaskCanceledException => "市场服务请求超时。",
            JsonException => "市场服务返回的数据格式无效。",
            _ => $"行情刷新失败：{exception.Message}"
        };

    private void OnPropertyChanged(string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
