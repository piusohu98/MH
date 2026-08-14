using System.Net.Http;
using MH.Client.Controls;
using MH.Client.ViewModels;
using MH.Core;
using MH.Core.Contracts;
using MH.Core.Models;

namespace MH.Tests;

public sealed class ClientEventPresentationTests
{
    private static readonly DateTimeOffset AsOfUtc = new(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset LastSuccessUtc = new(2025, 1, 2, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EventFilteringExcludesTechnicalTypesAndFocusSelectionIsStable()
    {
        var ongoingStart = AsOfUtc.AddDays(-2);
        var events = new[]
        {
            Event("day-night", MarketEventType.DayNight, "DEMO Day/Night Cycle", ongoingStart, AsOfUtc.AddDays(1)),
            Event("ocr", MarketEventType.OcrAnomaly, "DEMO OCR", ongoingStart, AsOfUtc.AddDays(1)),
            Event("ongoing-later", MarketEventType.SupplyChange, "DEMO Supply Shortage", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1)),
            Event("ongoing-earlier", MarketEventType.Holiday, "DEMO Festival", ongoingStart, AsOfUtc.AddDays(1)),
            Event("ended", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-5), AsOfUtc.AddDays(-3)),
            Event("future", MarketEventType.SupplyChange, "DEMO Supply Surplus", AsOfUtc.AddDays(2), AsOfUtc.AddDays(5))
        };

        var relevant = FirstScreenViewModel.FilterRelevantEvents(events);
        var focus = FirstScreenViewModel.SelectFocusEvent(relevant, AsOfUtc);
        var calendarOrder = FirstScreenViewModel.OrderRelevantEvents(relevant, AsOfUtc);
        var tied = FirstScreenViewModel.SelectFocusEvent(
            [
                Event("z-event", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1)),
                Event("a-event", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1))
            ],
            AsOfUtc);

        Assert.Equal(4, relevant.Count);
        Assert.DoesNotContain(relevant, eventItem => eventItem.Type is MarketEventType.DayNight or MarketEventType.OcrAnomaly);
        Assert.Equal("ongoing-later", focus!.Id);
        Assert.Equal(["ongoing-later", "ongoing-earlier", "ended", "future"], calendarOrder.Select(eventItem => eventItem.Id));
        Assert.Equal("a-event", tied!.Id);
    }

    [Fact]
    public async Task EventCardUsesPlayerLanguageAndRequestsOnlyFocusedImpact()
    {
        var api = CreateApi(
            [
                Event("holiday", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1)),
                Event("shortage", MarketEventType.SupplyChange, "DEMO Supply Shortage", AsOfUtc.AddDays(-3), AsOfUtc.AddDays(4)),
                Event("surplus", MarketEventType.SupplyChange, "DEMO Supply Surplus", AsOfUtc.AddDays(4), AsOfUtc.AddDays(8))
            ],
            FakeMarketApi.CreateEventImpact("holiday", AsOfUtc));
        var viewModel = await LoadAsync(api);

        Assert.Equal(1, api.EventsCalls);
        Assert.Equal(1, api.EventImpactCalls);
        var eventRequest = Assert.Single(api.EventRequests);
        Assert.Equal(AsOfUtc.AddDays(-30), eventRequest.FromUtc);
        Assert.Equal(AsOfUtc.AddDays(30), eventRequest.ToUtc);
        Assert.Null(eventRequest.Type);
        Assert.Contains("模拟节日", viewModel.FocusEventTitleText, StringComparison.Ordinal);
        Assert.Contains("节日活动", viewModel.FocusEventPeriodText, StringComparison.Ordinal);
        Assert.Equal("进行中，样本仍在积累", viewModel.FocusEventStatusText);
        Assert.Contains("高于活动前", viewModel.DuringPriceImpactText, StringComparison.Ordinal);
        Assert.Contains("+10.0%", viewModel.DuringPriceImpactText, StringComparison.Ordinal);
        Assert.Contains("多于活动前", viewModel.DuringSupplyImpactText, StringComparison.Ordinal);
        Assert.Contains("低于活动前", viewModel.AfterPriceImpactText, StringComparison.Ordinal);
        Assert.Contains("少于活动前", viewModel.AfterSupplyImpactText, StringComparison.Ordinal);
        Assert.Contains("样本可用", viewModel.EventEvidenceText, StringComparison.Ordinal);
        Assert.Contains("原始日线 3", viewModel.EventEvidenceText, StringComparison.Ordinal);
        Assert.Contains("不是买卖建议", viewModel.EventResearchNoticeText, StringComparison.Ordinal);
        Assert.Contains("模拟供应减少", viewModel.EventCalendarText, StringComparison.Ordinal);
        Assert.Contains("模拟供应增加", viewModel.EventCalendarText, StringComparison.Ordinal);
        Assert.DoesNotContain("DayNight", viewModel.EventCalendarText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventPresentationPropertiesNotifyWhenSnapshotChanges()
    {
        var api = CreateApi(
            [Event("holiday", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1))],
            FakeMarketApi.CreateEventImpact("holiday", AsOfUtc));
        var viewModel = CreateViewModel(api);
        var propertyNames = new List<string?>();
        viewModel.PropertyChanged += (_, args) => propertyNames.Add(args.PropertyName);

        await viewModel.RefreshAsync();

        Assert.Contains(nameof(FirstScreenViewModel.EventCalendarText), propertyNames);
        Assert.Contains(nameof(FirstScreenViewModel.FocusEventTitleText), propertyNames);
        Assert.Contains(nameof(FirstScreenViewModel.DuringPriceImpactText), propertyNames);
        Assert.Contains(nameof(FirstScreenViewModel.EventEvidenceText), propertyNames);
        Assert.Contains(nameof(FirstScreenViewModel.EventResearchNoticeText), propertyNames);
    }

    [Fact]
    public async Task PartialInsufficientAndUnavailableImpactUseChineseSafeText()
    {
        var baselineImpact = FakeMarketApi.CreateEventImpact("holiday", AsOfUtc);
        var impact = baselineImpact with
        {
            During = baselineImpact.During with
            {
                Availability = EventImpactAvailability.Partial,
                PriceChangeVsBefore = null,
                PriceComparisonUnavailableReason = "baseline-price-unavailable",
                VisibleSupplyChangeVsBefore = 0.1m
            },
            After = baselineImpact.After with
            {
                Availability = EventImpactAvailability.InsufficientData,
                PriceChangeVsBefore = null,
                VisibleSupplyChangeVsBefore = null,
                PriceComparisonUnavailableReason = "phase-price-unavailable",
                VisibleSupplyComparisonUnavailableReason = "phase-visible-supply-unavailable"
            }
        };
        var viewModel = await LoadAsync(CreateApi(
            [Event("holiday", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1))],
            impact));

        Assert.Contains("进行中，样本仍在积累", viewModel.EventEvidenceText, StringComparison.Ordinal);
        Assert.Contains("样本不足", viewModel.EventEvidenceText, StringComparison.Ordinal);
        Assert.Contains("暂不可比较", viewModel.DuringPriceImpactText, StringComparison.Ordinal);
        Assert.Contains("活动前价格样本不足", viewModel.DuringPriceImpactText, StringComparison.Ordinal);
        Assert.Contains("本阶段价格样本不足", viewModel.AfterPriceImpactText, StringComparison.Ordinal);
        Assert.DoesNotContain("baseline-", viewModel.DuringPriceImpactText, StringComparison.Ordinal);
        Assert.DoesNotContain("phase-", viewModel.AfterPriceImpactText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoEventsShowsExplicitNoDataText()
    {
        var viewModel = await LoadAsync(CreateApi([], null));

        Assert.Equal("暂无重点活动", viewModel.FocusEventTitleText);
        Assert.Contains("没有可比较", viewModel.FocusEventPeriodText, StringComparison.Ordinal);
        Assert.Equal("暂无活动", viewModel.FocusEventStatusText);
        Assert.Contains("附近没有", viewModel.EventCalendarText, StringComparison.Ordinal);
        Assert.Contains("暂无重点活动影响资料", viewModel.DuringPriceImpactText, StringComparison.Ordinal);
        Assert.Contains("暂无重点活动样本", viewModel.EventEvidenceText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventApiFailureDoesNotTakeCoreMarketOffline()
    {
        var api = CreateApi(
            [Event("holiday", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1))],
            FakeMarketApi.CreateEventImpact("holiday", AsOfUtc));
        api.EventsFailure = new HttpRequestException("events unavailable");
        var viewModel = await LoadAsync(api);

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.NotNull(viewModel.Snapshot);
        Assert.Equal("活动资料暂时不可用。", viewModel.EventResearchErrorText);
        Assert.Contains("活动资料暂时不可用", viewModel.EventResearchErrorText, StringComparison.Ordinal);
        Assert.False(viewModel.IsStale);
    }

    [Fact]
    public async Task EventImpactFailureRetainsPreviousActivitySnapshot()
    {
        var events = new[]
        {
            Event("holiday", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1))
        };
        var api = CreateApi(events, FakeMarketApi.CreateEventImpact("holiday", AsOfUtc));
        var viewModel = await LoadAsync(api);
        var previousEvents = viewModel.RelevantEvents;
        var previousImpact = viewModel.SelectedEventImpact;

        api.EventImpactFailure = new InvalidOperationException("empty impact response");
        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.False(viewModel.IsStale);
        Assert.Equal(previousEvents, viewModel.RelevantEvents);
        Assert.Same(previousImpact, viewModel.SelectedEventImpact);
        Assert.Equal("活动资料暂时不可用，显示上次成功结果。", viewModel.EventResearchErrorText);
    }

    [Fact]
    public async Task EventFailureOnDifferentMarketDoesNotReusePreviousActivity()
    {
        var api = CreateApi(
            [Event("holiday", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1))],
            FakeMarketApi.CreateEventImpact("holiday", AsOfUtc));
        var viewModel = await LoadAsync(api);

        api.Series = null;
        api.EventsFailure = new HttpRequestException("events unavailable");
        viewModel.SelectedItemId = "item-2";
        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.Empty(viewModel.RelevantEvents);
        Assert.Null(viewModel.SelectedEventImpact);
        Assert.Equal("活动资料暂时不可用。", viewModel.EventResearchErrorText);
    }

    [Fact]
    public async Task EventPresentationStaysOnSnapshotImpactWhenEditableAsOfChanges()
    {
        var api = CreateApi(
            [Event("holiday", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1))],
            FakeMarketApi.CreateEventImpact("holiday", AsOfUtc));
        var viewModel = await LoadAsync(api);
        var title = viewModel.FocusEventTitleText;
        var period = viewModel.FocusEventPeriodText;
        var status = viewModel.FocusEventStatusText;
        var duringPrice = viewModel.DuringPriceImpactText;
        var afterSupply = viewModel.AfterSupplyImpactText;

        viewModel.SelectedAsOfUtcText = AsOfUtc.AddDays(5).ToString("O");

        Assert.Equal(title, viewModel.FocusEventTitleText);
        Assert.Equal(period, viewModel.FocusEventPeriodText);
        Assert.Equal(status, viewModel.FocusEventStatusText);
        Assert.Equal(duringPrice, viewModel.DuringPriceImpactText);
        Assert.Equal(afterSupply, viewModel.AfterSupplyImpactText);
        Assert.Equal("进行中，样本仍在积累", viewModel.FocusEventStatusText);
    }

    [Fact]
    public async Task OfflineRefreshRetainsActivityWithWholePreviousSnapshot()
    {
        var api = CreateApi(
            [Event("holiday", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1))],
            FakeMarketApi.CreateEventImpact("holiday", AsOfUtc));
        var viewModel = await LoadAsync(api);
        var previousSnapshot = viewModel.Snapshot;

        api.Failure = new HttpRequestException("offline");
        await viewModel.RefreshAsync();

        Assert.Equal(MarketViewState.Offline, viewModel.State);
        Assert.True(viewModel.IsStale);
        Assert.Same(previousSnapshot, viewModel.Snapshot);
        Assert.NotNull(viewModel.SelectedEventImpact);
        Assert.Single(viewModel.RelevantEvents);
    }

    [Fact]
    public async Task OlderActivityImpactCannotOverwriteNewerRefresh()
    {
        var api = CreateApi(
            [Event("holiday", MarketEventType.Holiday, "DEMO Festival", AsOfUtc.AddDays(-1), AsOfUtc.AddDays(1))],
            null);
        api.BlockEventImpact = true;
        var viewModel = CreateViewModel(api);

        var older = viewModel.RefreshAsync();
        await api.WaitForEventImpactCountAsync(1);
        var newer = viewModel.RefreshAsync();
        await api.WaitForEventImpactCountAsync(2);

        api.CompleteEventImpact(1, FakeMarketApi.CreateEventImpact("new-impact", AsOfUtc, rawBarCount: 9));
        await newer;
        api.CompleteEventImpact(0, FakeMarketApi.CreateEventImpact("old-impact", AsOfUtc, rawBarCount: 1));
        await older;

        Assert.Equal(MarketViewState.Ready, viewModel.State);
        Assert.Equal("new-impact", viewModel.SelectedEventImpact!.Event.Id);
        Assert.Contains("原始日线 9", viewModel.EventEvidenceText, StringComparison.Ordinal);
    }

    private static FakeMarketApi CreateApi(
        IReadOnlyList<MarketEventDto> events,
        EventImpactResponse? impact)
        => new()
        {
            Events = events,
            EventImpact = impact,
            Series = new MarketSeriesResponse(
                "server-1",
                "item-1",
                AsOfUtc.AddDays(-30),
                AsOfUtc,
                [new PriceBarDto(AsOfUtc.AddDays(-1), AsOfUtc, 100, 110, 90, 100, 10, false)])
        };

    private static FirstScreenViewModel CreateViewModel(FakeMarketApi api)
        => new(api, () => LastSuccessUtc)
        {
            SelectedServerId = "server-1",
            SelectedItemId = "item-1",
            SelectedAsOfUtc = AsOfUtc
        };

    private static async Task<FirstScreenViewModel> LoadAsync(FakeMarketApi api)
    {
        var viewModel = CreateViewModel(api);
        await viewModel.RefreshAsync();
        Assert.Equal(MarketViewState.Ready, viewModel.State);
        return viewModel;
    }

    private static MarketEventDto Event(
        string id,
        MarketEventType type,
        string label,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc)
        => new(id, "server-1", "item-1", type, label, startsAtUtc, endsAtUtc, CatalogKind.Demo);
}

public sealed class PriceChartEventTests
{
    private static readonly DateTimeOffset RangeStartUtc = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeEndUtc = new(2025, 1, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EventBandsClipToChartRangeFilterTechnicalEventsAndKeepOverlapDeterministic()
    {
        var bands = PriceChart.GetVisibleEventBands(
            [
                Event("holiday", MarketEventType.Holiday, "Festival", RangeStartUtc.AddDays(-2), RangeStartUtc.AddDays(2)),
                Event("supply-a", MarketEventType.SupplyChange, "Supply A", RangeStartUtc.AddDays(1), RangeStartUtc.AddDays(5)),
                Event("supply-b", MarketEventType.SupplyChange, "Supply B", RangeStartUtc.AddDays(1), RangeStartUtc.AddDays(5)),
                Event("day-night", MarketEventType.DayNight, "Day/Night", RangeStartUtc, RangeStartUtc.AddDays(2)),
                Event("future", MarketEventType.Holiday, "Future", RangeEndUtc.AddDays(1), RangeEndUtc.AddDays(2))
            ],
            RangeStartUtc,
            RangeEndUtc);

        Assert.Equal(3, bands.Count);
        Assert.Equal("holiday", bands[0].Id);
        Assert.Equal(RangeStartUtc, bands[0].StartUtc);
        Assert.Equal(RangeStartUtc.AddDays(2), bands[0].EndUtc);
        Assert.Equal("supply-a", bands[1].Id);
        Assert.Equal("supply-b", bands[2].Id);
        Assert.All(bands, band => Assert.True(band.StartUtc < band.EndUtc));
    }

    [Fact]
    public void EmptyOrOutsideEventBandsRemainEmpty()
    {
        Assert.Empty(PriceChart.GetVisibleEventBands([], RangeStartUtc, RangeEndUtc));
        Assert.Empty(PriceChart.GetVisibleEventBands(
            [Event("future", MarketEventType.SupplyChange, "Future", RangeEndUtc, RangeEndUtc.AddDays(1))],
            RangeStartUtc,
            RangeEndUtc));
        Assert.Empty(PriceChart.GetVisibleEventBands([], RangeEndUtc, RangeStartUtc));
        Assert.Equal("Events", PriceChart.EventsProperty.Name);
    }

    [Fact]
    public void PricePointsUseUtcTimeMappingWhenDaysAreMissing()
    {
        var left = PriceChart.MapUtcToX(
            RangeStartUtc.AddDays(1),
            RangeStartUtc,
            RangeStartUtc.AddDays(3),
            10,
            110);
        var right = PriceChart.MapUtcToX(
            RangeStartUtc.AddDays(3),
            RangeStartUtc,
            RangeStartUtc.AddDays(3),
            10,
            110);

        Assert.Equal(43.3333333333, left, precision: 6);
        Assert.Equal(110, right, precision: 6);
    }

    private static MarketEventDto Event(
        string id,
        MarketEventType type,
        string label,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc)
        => new(id, "server-1", "item-1", type, label, startsAtUtc, endsAtUtc, CatalogKind.Demo);
}
