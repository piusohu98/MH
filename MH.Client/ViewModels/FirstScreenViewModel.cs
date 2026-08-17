using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using MH.Client.Api;
using MH.Core;
using MH.Core.Backtesting;
using MH.Core.Contracts;
using MH.Core.Models;
using MH.Core.Recommendations;

namespace MH.Client.ViewModels;

public sealed class FirstScreenViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<ServerDto> EmptyServers = Array.Empty<ServerDto>();
    private static readonly IReadOnlyList<ItemDto> EmptyItems = Array.Empty<ItemDto>();
    private static readonly IReadOnlyList<MarketEventDto> EmptyEvents = Array.Empty<MarketEventDto>();
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

    public IReadOnlyList<MarketEventDto> RelevantEvents => Snapshot?.RelevantEvents ?? EmptyEvents;

    public EventImpactResponse? SelectedEventImpact => Snapshot?.SelectedEventImpact;

    public string? EventResearchError => Snapshot?.EventResearchError;

    public EventPatternSummaryResponse? SelectedEventPatternSummary => Snapshot?.EventPatternSummary;

    public string? EventPatternSummaryError => Snapshot?.EventPatternSummaryError;

    public CrossServerEventStandardizationResponse? SelectedCrossServerEventSummary
        => Snapshot?.CrossServerEventSummary;

    public string? CrossServerEventSummaryError => Snapshot?.CrossServerEventSummaryError;

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

    public string EventCalendarText
        => RelevantEvents.Count == 0
            ? "附近没有可展示的节日或供给变化活动。"
            : string.Join(
                Environment.NewLine,
                GetCalendarEvents().Take(5).Select(eventItem =>
                    $"{GetEventTypeText(eventItem.Type)} · {GetEventLabelText(eventItem)} · {FormatEventDateRange(eventItem)}"));

    public string FocusEventTitleText
    {
        get
        {
            var eventItem = GetFocusEvent();
            return eventItem is null ? "暂无重点活动" : GetEventLabelText(eventItem);
        }
    }

    public string FocusEventPeriodText
    {
        get
        {
            var eventItem = GetFocusEvent();
            if (eventItem is null)
            {
                return "没有可比较的重点活动";
            }

            return $"{GetEventTypeText(eventItem.Type)} · {FormatEventDateRange(eventItem)}";
        }
    }

    public string FocusEventStatusText
    {
        get
        {
            var eventItem = GetFocusEvent();
            if (eventItem is null)
            {
                return "暂无活动";
            }

            var asOfUtc = (SelectedEventImpact?.AsOfUtc ?? Snapshot?.Indicators.CutoffUtc)?.ToUniversalTime();
            if (!asOfUtc.HasValue)
            {
                return "等待历史时点";
            }

            return asOfUtc.Value < eventItem.StartsAtUtc.ToUniversalTime()
                ? "尚未开始"
                : asOfUtc.Value < eventItem.EndsAtUtc.ToUniversalTime()
                    ? "进行中，样本仍在积累"
                    : "已结束，可查看活动后样本";
        }
    }

    public string DuringPriceImpactText
        => FormatEventImpact("活动中常见价", SelectedEventImpact?.During, isVisibleSupply: false);

    public string DuringSupplyImpactText
        => FormatEventImpact("活动中在售数量", SelectedEventImpact?.During, isVisibleSupply: true);

    public string AfterPriceImpactText
        => FormatEventImpact("活动后常见价", SelectedEventImpact?.After, isVisibleSupply: false);

    public string AfterSupplyImpactText
        => FormatEventImpact("活动后在售数量", SelectedEventImpact?.After, isVisibleSupply: true);

    public string EventEvidenceText
    {
        get
        {
            var impact = SelectedEventImpact;
            if (impact is null)
            {
                return EventResearchError is null ? "暂无重点活动样本。" : "活动样本暂不可用。";
            }

            return string.Join(
                Environment.NewLine,
                FormatPhaseEvidence("活动前", impact.Before),
                FormatPhaseEvidence("活动中", impact.During),
                FormatPhaseEvidence("活动后", impact.After));
        }
    }

    public string EventResearchNoticeText
        => "单次历史采集比较，不代表同类活动必然重复，不是买卖建议。";

    public string EventResearchErrorText
        => EventResearchError ?? string.Empty;

    public string EventPatternSummaryText
    {
        get
        {
            var summary = SelectedEventPatternSummary;
            if (summary is null)
            {
                return EventPatternSummaryError is null ? "暂无相似活动归纳。" : "相似活动归纳暂不可用。";
            }

            return string.Join(
                Environment.NewLine,
                $"相似活动历史归纳（{GetEventTypeText(summary.EventType)}）",
                $"样本活动 {summary.SampleEventCount} 个 · 历史窗口 {summary.HistoryDays} 天 · 统计版本 {summary.StatisticsVersion}",
                FormatPatternMetric("活动中价格", summary.DuringPrice, isSupply: false),
                FormatPatternMetric("活动后价格", summary.AfterPrice, isSupply: false),
                FormatPatternMetric("活动中在售数量", summary.DuringVisibleSupply, isSupply: true),
                FormatPatternMetric("活动后在售数量", summary.AfterVisibleSupply, isSupply: true),
                $"中性区间：±{FormatPercent(summary.NeutralThreshold)}；仅表示历史样本归纳，不是买卖建议。");
        }
    }

    public string EventPatternSummaryErrorText
        => EventPatternSummaryError ?? string.Empty;

    public string CrossServerEventSummaryText
    {
        get
        {
            var summary = SelectedCrossServerEventSummary;
            if (summary is null)
            {
                return CrossServerEventSummaryError is null ? "暂无跨区比较样本。" : "跨区比较暂不可用。";
            }

            return string.Join(
                Environment.NewLine,
                $"跨区比较（{GetEventTypeText(summary.EventType)}）",
                $"跨区样本 {summary.SampleServerCount} 个 · 统计版本 {summary.StatisticsVersion}",
                FormatCrossServerMetric("活动中价格", summary.DuringPrice),
                FormatCrossServerMetric("活动后价格", summary.AfterPrice),
                FormatCrossServerMetric("活动中在售数量", summary.DuringVisibleSupply),
                FormatCrossServerMetric("活动后在售数量", summary.AfterVisibleSupply),
                $"标准化方式：每个区先按活动中位变化，再对各区结果等权汇总；仅表示历史样本，不是买卖建议。");
        }
    }

    public string CrossServerEventSummaryErrorText
        => CrossServerEventSummaryError ?? string.Empty;

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
            var eventData = await LoadEventDataAsync(
                serverId,
                itemId,
                SelectedAsOfUtc.Value,
                Snapshot,
                requestCancellation.Token);
            if (!IsCurrent(request.Version))
            {
                return;
            }

            ApplyLoadedSnapshot(loadedCatalog, series, indicators, recommendation, eventData);
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
            var eventData = await LoadEventDataAsync(
                serverId,
                itemId,
                asOfUtc,
                Snapshot,
                requestCancellation.Token);

            if (!IsCurrent(request.Version))
            {
                return;
            }

            ApplyLoadedSnapshot(catalog, series, indicators, recommendation, eventData);
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
        RecommendationPreviewResponse recommendation,
        EventLoadResult eventData)
    {
        Catalog = loadedCatalog;
        Snapshot = new MarketScreenSnapshot(
            loadedCatalog,
            series,
            indicators,
            recommendation,
            eventData.Events,
            eventData.Impact,
            eventData.Error,
            eventData.PatternSummary,
            eventData.PatternSummaryError,
            eventData.CrossServerSummary,
            eventData.CrossServerSummaryError);
        LastSuccessfulAtUtc = utcNow().ToUniversalTime();
        IsStale = indicators.DataAgeHours is > RecommendationRule.MaxDataAgeHours;
        ErrorMessage = null;
        State = MarketViewState.Ready;
    }

    private async Task<EventLoadResult> LoadEventDataAsync(
        string serverId,
        string itemId,
        DateTimeOffset asOfUtc,
        MarketScreenSnapshot? previousSnapshot,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MarketEventDto> relevantEvents;
        string? eventError = null;
        try
        {
            var events = await apiClient.GetEventsAsync(
                serverId,
                itemId,
                asOfUtc.AddDays(-30),
                asOfUtc.AddDays(30),
                cancellationToken: cancellationToken);
            if (events is null)
            {
                throw new InvalidOperationException("市场服务返回了空活动列表。");
            }

            relevantEvents = FilterRelevantEvents(events);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsOptionalEventFailure(exception, cancellationToken))
        {
            if (previousSnapshot is { } prior
                && string.Equals(prior.Series.ServerId, serverId, StringComparison.Ordinal)
                && string.Equals(prior.Series.ItemId, itemId, StringComparison.Ordinal))
            {
                relevantEvents = prior.RelevantEvents;
                eventError = "活动资料暂时不可用，显示上次成功结果。";
            }
            else
            {
                relevantEvents = EmptyEvents;
                eventError = "活动资料暂时不可用。";
            }
        }

        var focusEvent = SelectFocusEvent(relevantEvents, asOfUtc);
        EventImpactResponse? impact = null;
        if (focusEvent is not null)
        {
            try
            {
                impact = await apiClient.GetEventImpactAsync(
                    serverId,
                    itemId,
                    focusEvent.Id,
                    asOfUtc,
                    EventImpactAnalyzer.DefaultWindowDays,
                    cancellationToken);
                if (impact is null
                    || !string.Equals(impact.Event.Id, focusEvent.Id, StringComparison.Ordinal)
                    || !string.Equals(impact.Event.ServerId, serverId, StringComparison.Ordinal)
                    || (impact.Event.ItemId is not null
                        && !string.Equals(impact.Event.ItemId, itemId, StringComparison.Ordinal))
                    || impact.AsOfUtc.ToUniversalTime() != asOfUtc.ToUniversalTime())
                {
                    throw new InvalidOperationException("市场服务返回了不匹配的活动影响结果。");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsOptionalEventFailure(exception, cancellationToken))
            {
                if (previousSnapshot is { } prior
                    && string.Equals(prior.Series.ServerId, serverId, StringComparison.Ordinal)
                    && string.Equals(prior.Series.ItemId, itemId, StringComparison.Ordinal)
                    && prior.SelectedEventImpact?.Event.Id == focusEvent.Id
                    && prior.SelectedEventImpact.AsOfUtc.ToUniversalTime() == asOfUtc.ToUniversalTime())
                {
                    impact = prior.SelectedEventImpact;
                    eventError ??= "活动资料暂时不可用，显示上次成功结果。";
                }
                else
                {
                    eventError ??= "活动资料暂时不可用。";
                }
            }
        }

        EventPatternSummaryResponse? patternSummary = null;
        string? patternSummaryError = null;
        if (focusEvent is not null)
        {
            try
            {
                patternSummary = await apiClient.GetEventPatternSummaryAsync(
                    serverId,
                    itemId,
                    focusEvent.Type,
                    asOfUtc,
                    EventPatternSummaryAnalyzer.DefaultWindowDays,
                    EventPatternSummaryAnalyzer.DefaultHistoryDays,
                    EventPatternSummaryAnalyzer.DefaultMaxEvents,
                    cancellationToken);
                if (patternSummary is null
                    || !string.Equals(patternSummary.ServerId, serverId, StringComparison.Ordinal)
                    || !string.Equals(patternSummary.ItemId, itemId, StringComparison.Ordinal)
                    || patternSummary.EventType != focusEvent.Type
                    || patternSummary.AsOfUtc.ToUniversalTime() != asOfUtc.ToUniversalTime())
                {
                    throw new InvalidOperationException("市场服务返回了不匹配的相似活动归纳结果。");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsOptionalEventFailure(exception, cancellationToken))
            {
                if (previousSnapshot is { } prior
                    && string.Equals(prior.Series.ServerId, serverId, StringComparison.Ordinal)
                    && string.Equals(prior.Series.ItemId, itemId, StringComparison.Ordinal)
                    && prior.EventPatternSummary?.EventType == focusEvent.Type
                    && prior.EventPatternSummary.AsOfUtc.ToUniversalTime() == asOfUtc.ToUniversalTime())
                {
                    patternSummary = prior.EventPatternSummary;
                    patternSummaryError = "相似活动归纳暂时不可用，显示上次同类活动结果。";
                }
                else
                {
                    patternSummaryError = "相似活动归纳暂时不可用。";
                }
            }
        }

        CrossServerEventStandardizationResponse? crossServerSummary = null;
        string? crossServerSummaryError = null;
        if (focusEvent is not null)
        {
            try
            {
                crossServerSummary = await apiClient.GetCrossServerEventSummaryAsync(
                    itemId,
                    focusEvent.Type,
                    asOfUtc,
                    CrossServerEventStandardizationAnalyzer.DefaultWindowDays,
                    CrossServerEventStandardizationAnalyzer.DefaultHistoryDays,
                    CrossServerEventStandardizationAnalyzer.DefaultMaxServers,
                    CrossServerEventStandardizationAnalyzer.DefaultMaxEventsPerServer,
                    cancellationToken);
                if (crossServerSummary is null
                    || !string.Equals(crossServerSummary.ItemId, itemId, StringComparison.Ordinal)
                    || crossServerSummary.EventType != focusEvent.Type
                    || crossServerSummary.AsOfUtc.ToUniversalTime() != asOfUtc.ToUniversalTime())
                {
                    throw new InvalidOperationException("市场服务返回了不匹配的跨区活动结果。");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsOptionalEventFailure(exception, cancellationToken))
            {
                if (previousSnapshot is { } prior
                    && string.Equals(prior.Series.ItemId, itemId, StringComparison.Ordinal)
                    && prior.CrossServerEventSummary?.EventType == focusEvent.Type
                    && prior.CrossServerEventSummary.AsOfUtc.ToUniversalTime() == asOfUtc.ToUniversalTime())
                {
                    crossServerSummary = prior.CrossServerEventSummary;
                    crossServerSummaryError = "跨区比较暂时不可用，显示上次同类活动结果。";
                }
                else
                {
                    crossServerSummaryError = "跨区比较暂时不可用。";
                }
            }
        }

        return new EventLoadResult(
            relevantEvents,
            impact,
            eventError,
            patternSummary,
            patternSummaryError,
            crossServerSummary,
            crossServerSummaryError);
    }

    public static IReadOnlyList<MarketEventDto> FilterRelevantEvents(IEnumerable<MarketEventDto> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events
            .Where(eventItem => eventItem.Type is MarketEventType.Holiday or MarketEventType.SupplyChange)
            .OrderBy(eventItem => eventItem.StartsAtUtc.ToUniversalTime())
            .ThenBy(eventItem => eventItem.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static MarketEventDto? SelectFocusEvent(
        IEnumerable<MarketEventDto> events,
        DateTimeOffset asOfUtc)
    {
        return OrderRelevantEvents(events, asOfUtc).FirstOrDefault();
    }

    public static IReadOnlyList<MarketEventDto> OrderRelevantEvents(
        IEnumerable<MarketEventDto> events,
        DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(events);
        var cutoffUtc = asOfUtc.ToUniversalTime();
        var relevant = events
            .Where(eventItem => eventItem.Type is MarketEventType.Holiday or MarketEventType.SupplyChange)
            .Select(eventItem => (Event: eventItem, StartUtc: eventItem.StartsAtUtc.ToUniversalTime(), EndUtc: eventItem.EndsAtUtc.ToUniversalTime()))
            .ToArray();

        return relevant
            .Where(item => item.StartUtc <= cutoffUtc && cutoffUtc < item.EndUtc)
            .OrderByDescending(item => item.StartUtc)
            .ThenBy(item => item.Event.Id, StringComparer.Ordinal)
            .Concat(relevant
                .Where(item => item.EndUtc <= cutoffUtc)
                .OrderByDescending(item => item.EndUtc)
                .ThenBy(item => item.Event.Id, StringComparer.Ordinal))
            .Concat(relevant
                .Where(item => item.StartUtc > cutoffUtc)
                .OrderBy(item => item.StartUtc)
                .ThenBy(item => item.Event.Id, StringComparer.Ordinal))
            .Select(item => item.Event)
            .ToArray();
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
        OnPropertyChanged(nameof(RelevantEvents));
        OnPropertyChanged(nameof(SelectedEventImpact));
        OnPropertyChanged(nameof(EventResearchError));
        OnPropertyChanged(nameof(SelectedEventPatternSummary));
        OnPropertyChanged(nameof(EventPatternSummaryError));
        OnPropertyChanged(nameof(SelectedCrossServerEventSummary));
        OnPropertyChanged(nameof(CrossServerEventSummaryError));
        OnPropertyChanged(nameof(EventCalendarText));
        OnPropertyChanged(nameof(FocusEventTitleText));
        OnPropertyChanged(nameof(FocusEventPeriodText));
        OnPropertyChanged(nameof(FocusEventStatusText));
        OnPropertyChanged(nameof(DuringPriceImpactText));
        OnPropertyChanged(nameof(DuringSupplyImpactText));
        OnPropertyChanged(nameof(AfterPriceImpactText));
        OnPropertyChanged(nameof(AfterSupplyImpactText));
        OnPropertyChanged(nameof(EventEvidenceText));
        OnPropertyChanged(nameof(EventResearchNoticeText));
        OnPropertyChanged(nameof(EventResearchErrorText));
        OnPropertyChanged(nameof(EventPatternSummaryText));
        OnPropertyChanged(nameof(EventPatternSummaryErrorText));
        OnPropertyChanged(nameof(CrossServerEventSummaryText));
        OnPropertyChanged(nameof(CrossServerEventSummaryErrorText));
        OnPropertyChanged(nameof(CanInitialize));
        OnPropertyChanged(nameof(CanRefresh));
    }

    private MarketEventDto? GetFocusEvent()
    {
        if (SelectedEventImpact is { } impact)
        {
            return impact.Event;
        }

        var cutoffUtc = Snapshot?.Indicators.CutoffUtc;
        return cutoffUtc.HasValue ? SelectFocusEvent(RelevantEvents, cutoffUtc.Value) : null;
    }

    private IEnumerable<MarketEventDto> GetCalendarEvents()
    {
        var cutoffUtc = SelectedEventImpact?.AsOfUtc ?? Snapshot?.Indicators.CutoffUtc;
        if (!cutoffUtc.HasValue)
        {
            return RelevantEvents;
        }

        return OrderRelevantEvents(RelevantEvents, cutoffUtc.Value);
    }

    private static string FormatEventImpact(
        string label,
        EventImpactPhaseResult? phase,
        bool isVisibleSupply)
    {
        if (phase is null)
        {
            return $"{label}：暂不可比较（暂无重点活动影响资料）";
        }

        var change = isVisibleSupply
            ? phase.VisibleSupplyChangeVsBefore
            : phase.PriceChangeVsBefore;
        if (!change.HasValue)
        {
            var reason = isVisibleSupply
                ? phase.VisibleSupplyComparisonUnavailableReason
                : phase.PriceComparisonUnavailableReason;
            return $"{label}：暂不可比较（{GetComparisonReasonText(reason)}）";
        }

        var direction = Math.Abs(change.Value) < 0.01m
            ? "接近活动前"
            : change.Value > 0
                ? isVisibleSupply ? "多于活动前" : "高于活动前"
                : isVisibleSupply ? "少于活动前" : "低于活动前";
        return $"{label}：{direction}（{FormatSignedPercent(change.Value)}）";
    }

    private static string FormatPhaseEvidence(string label, EventImpactPhaseResult phase)
        => $"{label}：{GetAvailabilityText(phase.Availability)} · 原始日线 {phase.RawBarCount} · 价格内点 {phase.PriceInlierCount} · 在售数量样本 {phase.VolumeSampleCount}";

    private static string FormatPatternMetric(
        string label,
        EventPatternMetricSummary metric,
        bool isSupply)
    {
        if (!metric.Available)
        {
            return $"{label}：样本不足（可比较 {metric.ComparableEventCount} 个，需要至少 3 个）";
        }

        var median = metric.MedianChange.HasValue
            ? FormatSignedPercent(metric.MedianChange.Value)
            : "—";
        return $"{label}：中位变化 {median} · 上涨 {metric.IncreaseCount} · 下跌 {metric.DecreaseCount} · 基本不变 {metric.StableCount} · 方向一致度 {FormatPercent(metric.DirectionConsistency)}";
    }

    private static string FormatCrossServerMetric(
        string label,
        CrossServerEventMetricSummary metric)
    {
        if (!metric.Available)
        {
            return $"{label}：跨区样本不足（可比较 {metric.ComparableServerCount} 个区服，需要至少 2 个）";
        }

        return $"{label}：中位变化 {FormatSignedPercent(metric.MedianChange!.Value)} · 区服范围 {FormatSignedPercent(metric.P25Change!.Value)} ~ {FormatSignedPercent(metric.P75Change!.Value)} · 上涨 {metric.IncreaseCount} · 下跌 {metric.DecreaseCount} · 基本不变 {metric.StableCount} · 一致度 {FormatPercent(metric.DirectionConsistency)}";
    }

    private static string GetComparisonReasonText(string? reason)
        => reason switch
        {
            "baseline-price-unavailable" => "活动前价格样本不足",
            "phase-price-unavailable" => "本阶段价格样本不足",
            "baseline-visible-supply-unavailable" => "活动前在售数量样本不足或基线为零",
            "phase-visible-supply-unavailable" => "本阶段在售数量样本不足",
            _ => "样本不足，暂不可比较"
        };

    private static string GetAvailabilityText(EventImpactAvailability availability)
        => availability switch
        {
            EventImpactAvailability.Available => "样本可用",
            EventImpactAvailability.Partial => "进行中，样本仍在积累",
            EventImpactAvailability.NotStarted => "尚未开始",
            EventImpactAvailability.InsufficientData => "样本不足",
            _ => "样本不足"
        };

    private static string GetEventTypeText(MarketEventType type)
        => type switch
        {
            MarketEventType.Holiday => "节日活动",
            MarketEventType.SupplyChange => "供给变化",
            _ => "活动"
        };

    private static string GetEventLabelText(MarketEventDto eventItem)
    {
        if (eventItem.CatalogKind == CatalogKind.Demo)
        {
            return eventItem.Label switch
            {
                "DEMO Festival" => "模拟节日",
                "DEMO Supply Shortage" => "模拟供应减少",
                "DEMO Supply Surplus" => "模拟供应增加",
                _ => eventItem.Label
            };
        }

        return eventItem.Label;
    }

    private static string FormatEventDateRange(MarketEventDto eventItem)
    {
        var start = eventItem.StartsAtUtc.ToUniversalTime().ToLocalTime();
        var end = eventItem.EndsAtUtc.ToUniversalTime().ToLocalTime();
        return $"{start:yyyy-MM-dd HH:mm} ~ {end:yyyy-MM-dd HH:mm}";
    }

    private static string FormatSignedPercent(decimal value)
        => $"{(value >= 0 ? "+" : string.Empty)}{value.ToString("P1", CultureInfo.InvariantCulture).Replace(" ", string.Empty, StringComparison.Ordinal)}";

    private static bool IsOptionalEventFailure(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException
            or JsonException
            or InvalidOperationException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;

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

    private sealed record EventLoadResult(
        IReadOnlyList<MarketEventDto> Events,
        EventImpactResponse? Impact,
        string? Error,
        EventPatternSummaryResponse? PatternSummary,
        string? PatternSummaryError,
        CrossServerEventStandardizationResponse? CrossServerSummary,
        string? CrossServerSummaryError);
}
