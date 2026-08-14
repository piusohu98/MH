using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using MH.Client.Api;
using MH.Core.Backtesting;
using MH.Core.Recommendations;

namespace MH.Client.ViewModels;

public sealed class FirstScreenViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<RecommendationReason> EmptyRecommendationReasons = Array.Empty<RecommendationReason>();
    private static readonly IReadOnlyList<BacktestQualityReason> EmptyGateReasons = Array.Empty<BacktestQualityReason>();

    private readonly IReadOnlyMarketApiClient apiClient;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly object refreshSync = new();
    private CancellationTokenSource? activeRefreshCancellation;
    private long refreshVersion;
    private string? selectedServerId;
    private string? selectedItemId;
    private DateTimeOffset? selectedAsOfUtc;
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
            OnPropertyChanged(nameof(IsActionable));
            OnPropertyChanged(nameof(DisplayedRecommendationAction));
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
            OnPropertyChanged();
        }
    }

    public MarketScreenSnapshot? Snapshot
    {
        get => snapshot;
        private set
        {
            snapshot = value;
            OnPropertyChanged();
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
        }
    }

    public DateTimeOffset? LastSuccessfulAtUtc
    {
        get => lastSuccessfulAtUtc;
        private set
        {
            lastSuccessfulAtUtc = value;
            OnPropertyChanged();
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
            OnPropertyChanged(nameof(IsActionable));
            OnPropertyChanged(nameof(DisplayedRecommendationAction));
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

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!TryGetSelection(out var serverId, out var itemId, out var asOfUtc))
        {
            CancelActiveRefresh();
            ApplyFailure(new ArgumentException("请选择有效的服务器、商品和 UTC 历史时点。"), preserveSnapshot: false);
            return;
        }

        var requestVersion = Interlocked.Increment(ref refreshVersion);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previousRefresh;
        lock (refreshSync)
        {
            previousRefresh = activeRefreshCancellation;
            activeRefreshCancellation = requestCancellation;
        }

        try
        {
            previousRefresh?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

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

            if (!IsCurrent(requestVersion))
            {
                return;
            }

            Snapshot = new MarketScreenSnapshot(catalog, series, indicators, recommendation);
            LastSuccessfulAtUtc = utcNow().ToUniversalTime();
            IsStale = indicators.DataAgeHours is > RecommendationRule.MaxDataAgeHours;
            ErrorMessage = null;
            State = MarketViewState.Ready;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || !IsCurrent(requestVersion))
        {
            if (IsCurrent(requestVersion) && cancellationToken.IsCancellationRequested)
            {
                RestoreAfterCallerCancellation();
            }
        }
        catch (Exception exception)
        {
            if (!IsCurrent(requestVersion))
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
            lock (refreshSync)
            {
                if (ReferenceEquals(activeRefreshCancellation, requestCancellation))
                {
                    activeRefreshCancellation = null;
                }
            }
        }
    }

    private bool TryGetSelection(out string serverId, out string itemId, out DateTimeOffset asOfUtc)
    {
        serverId = SelectedServerId?.Trim() ?? string.Empty;
        itemId = SelectedItemId?.Trim() ?? string.Empty;
        asOfUtc = SelectedAsOfUtc?.ToUniversalTime() ?? default;
        return serverId.Length > 0 && itemId.Length > 0 && SelectedAsOfUtc.HasValue;
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
