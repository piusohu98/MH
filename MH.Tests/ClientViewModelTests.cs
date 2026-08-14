using System.Net;
using System.Net.Http.Json;
using MH.Client.Api;
using MH.Client.ViewModels;
using MH.Core.Backtesting;
using MH.Core.Contracts;
using MH.Core.Models;
using MH.Core.Recommendations;

namespace MH.Tests;

public sealed class ClientApiTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task HttpClientLoadsAllFourReadOnlyEndpoints()
    {
        var handler = new RecordingHandler(CreateCatalog(), CreateSeries(), CreateIndicators(), CreatePreview());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var api = new HttpMarketApiClient(httpClient);
        using var cancellation = new CancellationTokenSource();

        var catalog = await api.GetCatalogAsync(CatalogKind.Demo, cancellation.Token);
        var series = await api.GetSeriesAsync("服务器 1", "商品 中文", AsOfUtc.AddDays(-30), AsOfUtc, cancellation.Token);
        var indicators = await api.GetIndicatorsAsync("服务器 1", "商品 中文", AsOfUtc, cancellation.Token);
        var recommendation = await api.GetRecommendationAsync("服务器 1", "商品 中文", AsOfUtc, cancellation.Token);

        Assert.Equal(CatalogKind.Demo, catalog.CatalogKind);
        Assert.Equal("服务器 1", series.ServerId);
        Assert.Equal("服务器 1", indicators.ServerId);
        Assert.Equal("服务器 1", recommendation.ServerId);
        Assert.Equal(4, handler.Requests.Count);

        var seriesRequest = Assert.Single(handler.Requests, request => request.AbsolutePath.EndsWith("/series", StringComparison.Ordinal));
        Assert.DoesNotContain(" ", seriesRequest.AbsolutePath);
        Assert.DoesNotContain("服务器", seriesRequest.AbsolutePath);
        Assert.Contains("fromUtc=2024-12-03T03%3A04%3A05.0000000Z", seriesRequest.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("toUtc=2025-01-02T03%3A04%3A05.0000000Z", seriesRequest.Query, StringComparison.OrdinalIgnoreCase);

        var recommendationRequest = Assert.Single(handler.Requests, request => request.AbsolutePath.EndsWith("/recommendation", StringComparison.Ordinal));
        Assert.Contains("asOfUtc=2025-01-02T03%3A04%3A05.0000000Z", recommendationRequest.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UriBuilderEncodesPathSegmentsAndNormalizesUtc()
    {
        var uri = MarketApiUris.Recommendation(
            "服务器 1",
            "商品 中文",
            new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(8)));

        Assert.DoesNotContain(" ", uri.OriginalString);
        Assert.DoesNotContain("服务器", uri.OriginalString);
        Assert.DoesNotContain("商品", uri.OriginalString);
        Assert.Contains("%E6%9C%8D", uri.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%E5%95%86", uri.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("asOfUtc=2025-01-01T19%3A04%3A05.0000000Z", uri.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    private static CatalogResponse CreateCatalog()
        => new(
            CatalogKind.Demo,
            [new ServerDto("服务器 1", "Demo Server", "测试", CatalogKind.Demo, AsOfUtc)],
            [new ItemDto("商品 中文", "Demo Item", "材料", "个", CatalogKind.Demo, AsOfUtc)]);

    private static MarketSeriesResponse CreateSeries()
        => new(
            "服务器 1",
            "商品 中文",
            AsOfUtc.AddDays(-30),
            AsOfUtc,
            [new PriceBarDto(AsOfUtc.AddDays(-1), AsOfUtc, 100, 110, 90, 105, 10, false)]);

    private static MarketIndicatorsResponse CreateIndicators()
        => new(
            "服务器 1",
            "商品 中文",
            AsOfUtc,
            100m,
            100m,
            1m,
            1m,
            7,
            30,
            7,
            30,
            0.05m,
            0.08m,
            105m,
            103m,
            0.02m,
            0.03m,
            -0.1m,
            -0.05m,
            12.5m);

    private static RecommendationPreviewResponse CreatePreview(
        RecommendationAction action = RecommendationAction.CandidateBuy,
        bool isActionable = false,
        BacktestQualityStatus gateStatus = BacktestQualityStatus.ResearchOnly)
    {
        var window = new BacktestWindowSummary(
            AsOfUtc.AddDays(-40),
            AsOfUtc,
            40m,
            30,
            4,
            0.03m,
            0.08m,
            0.5m,
            RecommendationRule.RuleVersion);
        var gate = new BacktestQualityGateResult(
            gateStatus,
            "backtest-quality-gate-v1",
            RecommendationRule.RuleVersion,
            new BacktestQualitySummary(3, 0.03m, 0.03m, 2m / 3m, 0.08m, 0.5m),
            [window, window with { StartUtc = AsOfUtc.AddDays(-80), EndUtc = AsOfUtc.AddDays(-40) }, window with { StartUtc = AsOfUtc.AddDays(-120), EndUtc = AsOfUtc.AddDays(-80) }],
            [new BacktestQualityReason("gate-research-only", "仅供研究验证。")]);
        var decision = new RecommendationDecision(
            AsOfUtc,
            action,
            action == RecommendationAction.CandidateSell ? -65 : 65,
            0.8m,
            RecommendationRule.RuleVersion,
            [new RecommendationReason("trend-consistent", "趋势样例。")],
            ["数据转为陈旧时失效。"],
            0.2m);
        return new RecommendationPreviewResponse(
            "服务器 1",
            "商品 中文",
            AsOfUtc,
            decision,
            isActionable,
            gate,
            new RecommendationPreviewResearchAssumptions(100_000m, 0.01m, 0.005m, 3, 40, 30, "只读研究预览；不代表真实获利保证。"));
    }

    private sealed class RecordingHandler(
        CatalogResponse catalog,
        MarketSeriesResponse series,
        MarketIndicatorsResponse indicators,
        RecommendationPreviewResponse recommendation) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri ?? throw new InvalidOperationException("Request URI was not set.");
            Requests.Add(requestUri);
            var path = requestUri.AbsolutePath;
            object response = path.EndsWith("/catalog", StringComparison.Ordinal)
                ? catalog
                : path.EndsWith("/series", StringComparison.Ordinal)
                    ? series
                    : path.EndsWith("/indicators", StringComparison.Ordinal)
                        ? indicators
                        : recommendation;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response)
            });
        }
    }
}

public sealed class FirstScreenViewModelTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset LastSuccessUtc = new(2025, 1, 2, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshLoadsFourEndpointsAndExposesResearchGuardrails()
    {
        var api = new FakeMarketApi();
        var viewModel = CreateViewModel(api);

        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.Equal(1, api.CatalogCalls);
        Assert.Equal(1, api.SeriesCalls);
        Assert.Equal(1, api.IndicatorsCalls);
        Assert.Equal(1, api.RecommendationCalls);
        Assert.NotNull(viewModel.Snapshot);
        Assert.Equal(LastSuccessUtc, viewModel.LastSuccessfulAtUtc);
        Assert.False(viewModel.IsStale);
        Assert.False(viewModel.IsActionable);
        Assert.Equal(RecommendationAction.CandidateBuy, viewModel.RawRecommendationAction);
        Assert.Equal(RecommendationAction.Observe, viewModel.DisplayedRecommendationAction);
        Assert.Equal(BacktestQualityStatus.ResearchOnly, viewModel.GateStatus);
        Assert.Equal("backtest-quality-gate-v1", viewModel.GateVersion);
        Assert.Equal(RecommendationRule.RuleVersion, viewModel.RuleVersion);
        Assert.NotEmpty(viewModel.GateReasons);
        Assert.Equal(12.5m, viewModel.DataAgeHours);
        Assert.Contains("研究预览", viewModel.ResearchNotice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidSelectionDoesNotCallApi()
    {
        var api = new FakeMarketApi();
        var viewModel = new FirstScreenViewModel(api, () => LastSuccessUtc)
        {
            SelectedServerId = " ",
            SelectedItemId = "item",
            SelectedAsOfUtc = null
        };

        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Error, viewModel.State);
        Assert.Equal(0, api.TotalCalls);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
    }

    [Fact]
    public async Task FirstNetworkFailureEntersErrorWithoutSnapshot()
    {
        var api = new FakeMarketApi { Failure = new HttpRequestException("offline") };
        var viewModel = CreateViewModel(api);

        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Error, viewModel.State);
        Assert.Null(viewModel.Snapshot);
        Assert.False(viewModel.IsStale);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
    }

    [Fact]
    public async Task NetworkFailureRetainsLastSnapshotAndMarksItStale()
    {
        var api = new FakeMarketApi
        {
            Preview = FakeMarketApi.CreatePreview(RecommendationAction.CandidateBuy, true, BacktestQualityStatus.TrialEligible)
        };
        var viewModel = CreateViewModel(api);
        await viewModel.RefreshAsync();
        Assert.NotNull(viewModel.Snapshot);
        var previousSnapshot = viewModel.Snapshot!;
        Assert.True(viewModel.IsActionable);

        api.Failure = new HttpRequestException("offline");
        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Offline, viewModel.State);
        Assert.Same(previousSnapshot, viewModel.Snapshot);
        Assert.True(viewModel.IsStale);
        Assert.False(viewModel.IsActionable);
        Assert.Equal(LastSuccessUtc, viewModel.LastSuccessfulAtUtc);
        Assert.Equal(RecommendationAction.Observe, viewModel.DisplayedRecommendationAction);
    }

    [Fact]
    public async Task DataAgeAtMaximumAllowedAgeIsNotStale()
    {
        var api = new FakeMarketApi
        {
            DataAgeHours = RecommendationRule.MaxDataAgeHours,
            Preview = FakeMarketApi.CreatePreview(RecommendationAction.CandidateBuy, true, BacktestQualityStatus.TrialEligible)
        };
        var viewModel = CreateViewModel(api);

        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.False(viewModel.IsStale);
        Assert.True(viewModel.IsActionable);
        Assert.Equal(RecommendationAction.CandidateBuy, viewModel.DisplayedRecommendationAction);
    }

    [Fact]
    public async Task DataAgeOverMaximumAllowedAgeIsStaleAndNotActionable()
    {
        var api = new FakeMarketApi
        {
            DataAgeHours = RecommendationRule.MaxDataAgeHours + 0.01m,
            Preview = FakeMarketApi.CreatePreview(RecommendationAction.CandidateBuy, true, BacktestQualityStatus.TrialEligible)
        };
        var viewModel = CreateViewModel(api);

        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.True(viewModel.IsStale);
        Assert.False(viewModel.IsActionable);
        Assert.Equal(RecommendationAction.Observe, viewModel.DisplayedRecommendationAction);
    }

    [Fact]
    public async Task ActionabilityPropertiesNotifyWhenStateOrStaleChanges()
    {
        var api = new FakeMarketApi
        {
            Preview = FakeMarketApi.CreatePreview(RecommendationAction.CandidateBuy, true, BacktestQualityStatus.TrialEligible)
        };
        var viewModel = CreateViewModel(api);
        var propertyNames = new List<string?>();
        viewModel.PropertyChanged += (_, args) => propertyNames.Add(args.PropertyName);

        await viewModel.RefreshAsync();
        propertyNames.Clear();
        api.Failure = new HttpRequestException("offline");
        await viewModel.RefreshAsync();

        Assert.Contains(nameof(FirstScreenViewModel.IsActionable), propertyNames);
        Assert.Contains(nameof(FirstScreenViewModel.DisplayedRecommendationAction), propertyNames);
    }

    [Fact]
    public async Task CallerCancellationDoesNotBecomeAnError()
    {
        var api = new FakeMarketApi { BlockCatalog = true };
        var viewModel = CreateViewModel(api);
        using var cancellation = new CancellationTokenSource();

        var refresh = viewModel.RefreshAsync(cancellation.Token);
        await api.CatalogStarted.Task;
        cancellation.Cancel();
        await refresh;

        Assert.Equal(MarketViewState.Idle, viewModel.State);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsStale);
    }

    [Fact]
    public async Task NewerRefreshWinsWhenOlderRequestIgnoresCancellation()
    {
        var api = new FakeMarketApi { BlockRecommendation = true };
        var viewModel = CreateViewModel(api);

        var olderRefresh = viewModel.RefreshAsync();
        await api.WaitForRecommendationCountAsync(1);
        var newerRefresh = viewModel.RefreshAsync();
        await api.WaitForRecommendationCountAsync(2);

        api.CompleteRecommendation(1, FakeMarketApi.CreatePreview(RecommendationAction.CandidateBuy, true, BacktestQualityStatus.TrialEligible));
        await newerRefresh;
        api.CompleteRecommendation(0, FakeMarketApi.CreatePreview(RecommendationAction.CandidateSell, false));
        await olderRefresh;

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.Equal(RecommendationAction.CandidateBuy, viewModel.RawRecommendationAction);
        Assert.True(viewModel.IsActionable);
    }

    [Fact]
    public async Task InitializeSelectsCatalogEntriesAndUsesLatestBarAsOf()
    {
        var latestEndUtc = AsOfUtc.AddDays(2);
        var api = new FakeMarketApi
        {
            Series = new MarketSeriesResponse(
                "server-1",
                "item-1",
                null,
                null,
                [
                    new PriceBarDto(AsOfUtc.AddDays(-1), AsOfUtc, 100, 110, 90, 105, 10, false),
                    new PriceBarDto(AsOfUtc.AddDays(1), latestEndUtc, 106, 115, 100, 112, 12, true)
                ])
        };
        var viewModel = CreateViewModel(api);

        await viewModel.InitializeAsync();

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.Equal("server-1", viewModel.SelectedServerId);
        Assert.Equal("item-1", viewModel.SelectedItemId);
        Assert.Equal(latestEndUtc, viewModel.SelectedAsOfUtc);
        Assert.Equal(1, api.CatalogCalls);
        Assert.Equal(1, api.SeriesCalls);
        Assert.Equal(1, api.IndicatorsCalls);
        Assert.Equal(1, api.RecommendationCalls);
        Assert.Null(api.SeriesRequests.Single().FromUtc);
        Assert.Null(api.SeriesRequests.Single().ToUtc);
        Assert.NotNull(viewModel.Snapshot);
    }

    [Fact]
    public async Task InitializeEmptyCatalogEntersErrorWithoutHardcodedSelection()
    {
        var api = new FakeMarketApi
        {
            Catalog = new CatalogResponse(CatalogKind.Demo, [], [])
        };
        var viewModel = new FirstScreenViewModel(api, () => LastSuccessUtc);

        await viewModel.InitializeAsync();

        Assert.Equal(MarketViewState.Error, viewModel.State);
        Assert.Null(viewModel.SelectedServerId);
        Assert.Null(viewModel.SelectedItemId);
        Assert.Equal(1, api.CatalogCalls);
        Assert.Equal(0, api.SeriesCalls);
        Assert.Equal(0, api.IndicatorsCalls);
        Assert.Equal(0, api.RecommendationCalls);
        Assert.Contains("目录", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeWithoutBarsDoesNotRequestIndicatorsOrRecommendation()
    {
        var api = new FakeMarketApi
        {
            Series = new MarketSeriesResponse("server-1", "item-1", null, null, [])
        };
        var viewModel = new FirstScreenViewModel(api, () => LastSuccessUtc);

        await viewModel.InitializeAsync();

        Assert.Equal(MarketViewState.Error, viewModel.State);
        Assert.Contains("历史行情", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(1, api.CatalogCalls);
        Assert.Equal(1, api.SeriesCalls);
        Assert.Equal(0, api.IndicatorsCalls);
        Assert.Equal(0, api.RecommendationCalls);
    }

    [Fact]
    public async Task InitializeFirstNetworkFailureEntersError()
    {
        var api = new FakeMarketApi { Failure = new HttpRequestException("offline") };
        var viewModel = new FirstScreenViewModel(api, () => LastSuccessUtc);

        await viewModel.InitializeAsync();

        Assert.Equal(MarketViewState.Error, viewModel.State);
        Assert.Null(viewModel.Snapshot);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
    }

    [Fact]
    public async Task InitializeCanBeRetriedAfterFirstNetworkFailure()
    {
        var api = new FakeMarketApi { Failure = new HttpRequestException("offline") };
        var viewModel = new FirstScreenViewModel(api, () => LastSuccessUtc);

        await viewModel.InitializeAsync();
        Assert.Equal(MarketViewState.Error, viewModel.State);

        api.Failure = null;
        await viewModel.InitializeAsync();

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.NotNull(viewModel.Snapshot);
        Assert.Equal(2, api.CatalogCalls);
    }

    [Fact]
    public async Task InvalidAsOfTextDisablesRefreshAndRetainsSnapshot()
    {
        var api = new FakeMarketApi();
        var viewModel = CreateViewModel(api);
        await viewModel.RefreshAsync();
        var previousSnapshot = viewModel.Snapshot;
        Assert.NotNull(previousSnapshot);
        Assert.True(viewModel.CanRefresh);

        viewModel.SelectedAsOfUtcText = "not-a-timestamp";

        Assert.False(viewModel.CanRefresh);
        Assert.True(viewModel.CanInitialize);
        Assert.Same(previousSnapshot, viewModel.Snapshot);
        await viewModel.RefreshAsync();
        Assert.Same(previousSnapshot, viewModel.Snapshot);
    }

    [Fact]
    public async Task InitializeCallerCancellationDoesNotBecomeAnError()
    {
        var api = new FakeMarketApi { BlockCatalog = true };
        var viewModel = new FirstScreenViewModel(api, () => LastSuccessUtc);
        using var cancellation = new CancellationTokenSource();

        var initialize = viewModel.InitializeAsync(cancellation.Token);
        await api.CatalogStarted.Task;
        cancellation.Cancel();
        await initialize;

        Assert.Equal(MarketViewState.Idle, viewModel.State);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task NewerRefreshWinsOverOlderInitialization()
    {
        var api = new FakeMarketApi { BlockRecommendation = true };
        var viewModel = CreateViewModel(api);

        var initialize = viewModel.InitializeAsync();
        await api.WaitForRecommendationCountAsync(1);
        var refresh = viewModel.RefreshAsync();
        await api.WaitForRecommendationCountAsync(2);

        api.CompleteRecommendation(1, FakeMarketApi.CreatePreview(RecommendationAction.CandidateBuy, true, BacktestQualityStatus.TrialEligible));
        await refresh;
        api.CompleteRecommendation(0, FakeMarketApi.CreatePreview(RecommendationAction.CandidateSell, false));
        await initialize;

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.Equal(RecommendationAction.CandidateBuy, viewModel.RawRecommendationAction);
        Assert.True(viewModel.IsActionable);
    }

    private static FirstScreenViewModel CreateViewModel(FakeMarketApi api)
        => new(api, () => LastSuccessUtc)
        {
            SelectedServerId = "server-1",
            SelectedItemId = "item-1",
            SelectedAsOfUtc = AsOfUtc
        };
}

internal sealed class FakeMarketApi : IReadOnlyMarketApiClient
{
    private static readonly DateTimeOffset AsOfUtc = new(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private readonly object sync = new();
    private readonly List<TaskCompletionSource<RecommendationPreviewResponse>> recommendationRequests = [];
    private readonly Dictionary<int, TaskCompletionSource<bool>> requestWaiters = [];

    public Exception? Failure { get; set; }
    public CatalogResponse? Catalog { get; set; }
    public MarketSeriesResponse? Series { get; set; }
    public RecommendationPreviewResponse? Preview { get; set; }
    public decimal DataAgeHours { get; set; } = 12.5m;
    public decimal? RobustMedian7Days { get; set; } = 100m;
    public decimal? Return7Days { get; set; } = 0.05m;
    public decimal? Volatility7Days { get; set; } = 0.02m;
    public decimal? VisibleSupplyChange7Days { get; set; } = -0.1m;
    public bool BlockCatalog { get; set; }
    public bool BlockRecommendation { get; set; }
    public int CatalogCalls { get; private set; }
    public int SeriesCalls { get; private set; }
    public int IndicatorsCalls { get; private set; }
    public int RecommendationCalls { get; private set; }
    public int TotalCalls => CatalogCalls + SeriesCalls + IndicatorsCalls + RecommendationCalls;
    public List<(string ServerId, string ItemId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc)> SeriesRequests { get; } = [];
    public TaskCompletionSource<bool> CatalogStarted { get; } = NewCompletionSource<bool>();
    private TaskCompletionSource<CatalogResponse> CatalogGate { get; } = NewCompletionSource<CatalogResponse>();

    public Task<CatalogResponse> GetCatalogAsync(CatalogKind catalogKind, CancellationToken cancellationToken)
    {
        CatalogCalls++;
        if (Failure is not null)
        {
            return Task.FromException<CatalogResponse>(Failure);
        }

        if (!BlockCatalog)
        {
            return Task.FromResult(Catalog ?? CreateCatalog());
        }

        CatalogStarted.TrySetResult(true);
        return CatalogGate.Task.WaitAsync(cancellationToken);
    }

    public Task<MarketSeriesResponse> GetSeriesAsync(
        string serverId,
        string itemId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        SeriesCalls++;
        SeriesRequests.Add((serverId, itemId, fromUtc, toUtc));
        return Failure is null
            ? Task.FromResult(Series ?? CreateSeries(serverId, itemId))
            : Task.FromException<MarketSeriesResponse>(Failure);
    }

    public Task<MarketIndicatorsResponse> GetIndicatorsAsync(
        string serverId,
        string itemId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        IndicatorsCalls++;
        return Failure is null
            ? Task.FromResult(CreateIndicators(serverId, itemId, asOfUtc))
            : Task.FromException<MarketIndicatorsResponse>(Failure);
    }

    public Task<RecommendationPreviewResponse> GetRecommendationAsync(
        string serverId,
        string itemId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        RecommendationCalls++;
        if (Failure is not null)
        {
            return Task.FromException<RecommendationPreviewResponse>(Failure);
        }

        if (!BlockRecommendation)
        {
            return Task.FromResult(Preview ?? CreatePreview());
        }

        lock (sync)
        {
            var request = NewCompletionSource<RecommendationPreviewResponse>();
            recommendationRequests.Add(request);
            foreach (var waiter in requestWaiters.Where(pair => recommendationRequests.Count >= pair.Key).Select(pair => pair.Value))
            {
                waiter.TrySetResult(true);
            }

            return request.Task;
        }
    }

    public Task WaitForRecommendationCountAsync(int count)
    {
        lock (sync)
        {
            if (recommendationRequests.Count >= count)
            {
                return Task.CompletedTask;
            }

            var waiter = NewCompletionSource<bool>();
            requestWaiters[count] = waiter;
            return waiter.Task;
        }
    }

    public void CompleteRecommendation(int index, RecommendationPreviewResponse response)
    {
        lock (sync)
        {
            recommendationRequests[index].TrySetResult(response);
        }
    }

    public static RecommendationPreviewResponse CreatePreview(
        RecommendationAction action = RecommendationAction.CandidateBuy,
        bool isActionable = false,
        BacktestQualityStatus gateStatus = BacktestQualityStatus.ResearchOnly,
        decimal confidence = 0.8m)
    {
        var window = new BacktestWindowSummary(AsOfUtc.AddDays(-40), AsOfUtc, 40m, 30, 4, 0.03m, 0.08m, 0.5m, RecommendationRule.RuleVersion);
        var gate = new BacktestQualityGateResult(
            gateStatus,
            "backtest-quality-gate-v1",
            RecommendationRule.RuleVersion,
            new BacktestQualitySummary(3, 0.03m, 0.03m, 2m / 3m, 0.08m, 0.5m),
            [window, window with { StartUtc = AsOfUtc.AddDays(-80), EndUtc = AsOfUtc.AddDays(-40) }, window with { StartUtc = AsOfUtc.AddDays(-120), EndUtc = AsOfUtc.AddDays(-80) }],
            [new BacktestQualityReason("gate-research-only", "仅供研究验证。")]);
        var decision = new RecommendationDecision(
            AsOfUtc,
            action,
            action == RecommendationAction.CandidateSell ? -65 : 65,
            confidence,
            RecommendationRule.RuleVersion,
            [new RecommendationReason("trend-consistent", "趋势样例。")],
            ["数据转为陈旧时失效。"],
            0.2m);
        return new RecommendationPreviewResponse(
            "server-1",
            "item-1",
            AsOfUtc,
            decision,
            isActionable,
            gate,
            new RecommendationPreviewResearchAssumptions(100_000m, 0.01m, 0.005m, 3, 40, 30, "只读研究预览；不代表真实获利保证。"));
    }

    private static CatalogResponse CreateCatalog()
        => new(
            CatalogKind.Demo,
            [new ServerDto("server-1", "Demo Server", "测试", CatalogKind.Demo, AsOfUtc)],
            [new ItemDto("item-1", "Demo Item", "材料", "个", CatalogKind.Demo, AsOfUtc)]);

    private static MarketSeriesResponse CreateSeries(string serverId, string itemId)
        => new(serverId, itemId, AsOfUtc.AddDays(-30), AsOfUtc, [new PriceBarDto(AsOfUtc.AddDays(-1), AsOfUtc, 100, 110, 90, 105, 10, false)]);

    private MarketIndicatorsResponse CreateIndicators(string serverId, string itemId, DateTimeOffset asOfUtc)
        => new(
            serverId,
            itemId,
            asOfUtc.ToUniversalTime(),
            RobustMedian7Days,
            100m,
            1m,
            1m,
            7,
            30,
            7,
            30,
            Return7Days,
            0.08m,
            105m,
            103m,
            Volatility7Days,
            0.03m,
            VisibleSupplyChange7Days,
            -0.05m,
            DataAgeHours);

    private static TaskCompletionSource<T> NewCompletionSource<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
