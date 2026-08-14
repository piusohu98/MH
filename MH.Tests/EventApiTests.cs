using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MH.Core;
using MH.Core.Contracts;
using MH.Core.Models;
using MH.Server.Data;

namespace MH.Tests;

public sealed class EventApiTests(MarketApiFactory factory) : IClassFixture<MarketApiFactory>
{
    [Fact]
    public async Task EventListFiltersByOverlapTypeAndItemAndSorts()
    {
        using var client = factory.CreateClient();
        var start = new DateTimeOffset(2035, 1, 10, 0, 0, 0, TimeSpan.Zero);
        await AddEventsAsync(
            Event("api-event-global", null, MarketEventType.Holiday, start.AddDays(2), start.AddDays(4)),
            Event("api-event-item-b", "demo-item-01", MarketEventType.Holiday, start, start.AddDays(2)),
            Event("api-event-item-a", "demo-item-01", MarketEventType.Holiday, start, start.AddDays(1)),
            Event("api-event-other-item", "demo-item-02", MarketEventType.Holiday, start, start.AddDays(1)),
            Event("api-event-supply", "demo-item-01", MarketEventType.SupplyChange, start, start.AddDays(1)));

        var response = await client.GetAsync(
            "/api/v1/markets/demo-server-01/demo-item-01/events?fromUtc=2035-01-10T00:00:00Z&toUtc=2035-01-20T00:00:00Z&type=Holiday");
        var json = await response.Content.ReadAsStringAsync();
        var events = JsonSerializer.Deserialize<List<MarketEventDto>>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(events);
        Assert.Equal(
            ["api-event-item-a", "api-event-item-b", "api-event-global"],
            events!.Select(x => x.Id).ToArray());
        Assert.All(events, x => Assert.Equal(TimeSpan.Zero, x.StartsAtUtc.Offset));
        Assert.DoesNotContain(events, x => x.Id == "api-event-other-item");
        Assert.DoesNotContain(events, x => x.Type == MarketEventType.SupplyChange);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, document.RootElement[0].GetProperty("type").ValueKind);
        Assert.Equal("Holiday", document.RootElement[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task EventListUsesHalfOpenOverlapAndNormalizesUtcOffsets()
    {
        using var client = factory.CreateClient();
        var eventStart = new DateTimeOffset(2035, 2, 10, 0, 0, 0, TimeSpan.Zero);
        await AddEventsAsync(Event("api-event-boundary", null, MarketEventType.Holiday, eventStart, eventStart.AddDays(2)));

        var touching = await client.GetFromJsonAsync<List<MarketEventDto>>(
            "/api/v1/markets/demo-server-01/demo-item-01/events?fromUtc=2035-02-12T00:00:00Z&toUtc=2035-02-13T00:00:00Z");
        var overlapping = await client.GetFromJsonAsync<List<MarketEventDto>>(
            "/api/v1/markets/demo-server-01/demo-item-01/events?fromUtc=2035-02-11T08%3A00%3A00%2B08%3A00&toUtc=2035-02-12T08%3A00%3A00%2B08%3A00");

        Assert.DoesNotContain(touching!, x => x.Id == "api-event-boundary");
        var matched = Assert.Single(overlapping!, x => x.Id == "api-event-boundary");
        Assert.Equal(eventStart, matched.StartsAtUtc);
    }

    [Theory]
    [InlineData("/api/v1/markets/demo-server-01/demo-item-01/events?toUtc=2035-01-02T00:00:00Z")]
    [InlineData("/api/v1/markets/demo-server-01/demo-item-01/events?fromUtc=2035-01-02T00:00:00&toUtc=2035-01-03T00:00:00Z")]
    [InlineData("/api/v1/markets/demo-server-01/demo-item-01/events?fromUtc=2035-01-03T00:00:00Z&toUtc=2035-01-02T00:00:00Z")]
    [InlineData("/api/v1/markets/demo-server-01/demo-item-01/events?fromUtc=2035-01-01T00:00:00Z&toUtc=2036-02-02T00:00:00Z")]
    [InlineData("/api/v1/markets/demo-server-01/demo-item-01/events?fromUtc=2035-01-01T00:00:00Z&toUtc=2035-01-02T00:00:00Z&type=Unknown")]
    public async Task EventListRejectsInvalidParameters(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task EventListRequiresQueryableCatalogMarket()
    {
        using var client = factory.CreateClient();

        var unknownServer = await client.GetAsync(
            "/api/v1/markets/not-a-server/demo-item-01/events?fromUtc=2035-01-01T00:00:00Z&toUtc=2035-01-02T00:00:00Z");
        var unknownItem = await client.GetAsync(
            "/api/v1/markets/demo-server-01/not-an-item/events?fromUtc=2035-01-01T00:00:00Z&toUtc=2035-01-02T00:00:00Z");

        Assert.Equal(HttpStatusCode.NotFound, unknownServer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownItem.StatusCode);
    }

    [Fact]
    public async Task EventImpactReturnsFactsForDemoEvent()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/markets/demo-server-01/demo-item-01/events/demo-supply-007/impact?asOfUtc=2025-06-30T00:00:00Z&windowDays=7");
        var impact = await response.Content.ReadFromJsonAsync<EventImpactResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(impact);
        Assert.Equal("demo-supply-007", impact!.Event.Id);
        Assert.Equal(new DateTimeOffset(2025, 6, 30, 0, 0, 0, TimeSpan.Zero), impact.AsOfUtc);
        Assert.Equal(7, impact.WindowDays);
        Assert.Equal(EventImpactAvailability.Available, impact.Before.Availability);
        Assert.Equal(EventImpactAvailability.Available, impact.During.Availability);
        Assert.Equal(EventImpactAvailability.Available, impact.After.Availability);
        Assert.NotNull(impact.During.RobustMedianPrice);
        Assert.NotNull(impact.During.VisibleSupplyMedian);
        Assert.NotNull(impact.During.PriceChangeVsBefore);
        Assert.NotNull(impact.During.VisibleSupplyChangeVsBefore);
    }

    [Fact]
    public async Task EventImpactMarksOngoingAndFuturePhasesSafely()
    {
        using var client = factory.CreateClient();
        await AddEventsAsync(
            Event("api-event-ongoing", null, MarketEventType.Holiday,
                new DateTimeOffset(2025, 6, 20, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2025, 6, 27, 0, 0, 0, TimeSpan.Zero)),
            Event("api-event-future", null, MarketEventType.Holiday,
                new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2025, 12, 7, 0, 0, 0, TimeSpan.Zero)));

        var ongoing = await client.GetFromJsonAsync<EventImpactResponse>(
            "/api/v1/markets/demo-server-01/demo-item-01/events/api-event-ongoing/impact?asOfUtc=2025-06-23T00:00:00Z&windowDays=3");
        var future = await client.GetFromJsonAsync<EventImpactResponse>(
            "/api/v1/markets/demo-server-01/demo-item-01/events/api-event-future/impact?asOfUtc=2025-06-30T00:00:00Z&windowDays=3");

        Assert.Equal(EventImpactAvailability.Partial, ongoing!.During.Availability);
        Assert.Equal(EventImpactAvailability.NotStarted, ongoing.After.Availability);
        Assert.False(ongoing.During.WindowComplete);
        Assert.Equal(EventImpactAvailability.NotStarted, future!.Before.Availability);
        Assert.Equal(EventImpactAvailability.NotStarted, future.During.Availability);
        Assert.Equal(EventImpactAvailability.NotStarted, future.After.Availability);
    }

    [Fact]
    public async Task EventImpactRejectsUnknownOrMismatchedEvent()
    {
        using var client = factory.CreateClient();
        await AddEventsAsync(Event(
            "api-event-item-only",
            "demo-item-01",
            MarketEventType.Holiday,
            new DateTimeOffset(2035, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2035, 3, 5, 0, 0, 0, TimeSpan.Zero)));

        var unknown = await client.GetAsync(
            "/api/v1/markets/demo-server-01/demo-item-01/events/not-an-event/impact?asOfUtc=2035-03-10T00:00:00Z&windowDays=3");
        var mismatched = await client.GetAsync(
            "/api/v1/markets/demo-server-01/demo-item-02/events/api-event-item-only/impact?asOfUtc=2035-03-10T00:00:00Z&windowDays=3");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mismatched.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/markets/demo-server-01/demo-item-01/events/demo-supply-007/impact?windowDays=3")]
    [InlineData("/api/v1/markets/demo-server-01/demo-item-01/events/demo-supply-007/impact?asOfUtc=2025-06-30T00:00:00&windowDays=3")]
    [InlineData("/api/v1/markets/demo-server-01/demo-item-01/events/demo-supply-007/impact?asOfUtc=2025-06-30T00:00:00Z&windowDays=2")]
    [InlineData("/api/v1/markets/demo-server-01/demo-item-01/events/demo-supply-007/impact?asOfUtc=2025-06-30T00:00:00Z&windowDays=31")]
    [InlineData("/api/v1/markets/demo-server-01/demo-item-01/events/demo-supply-007/impact?asOfUtc=2025-06-30T00:00:00Z&windowDays=abc")]
    public async Task EventImpactRejectsInvalidParameters(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task EventImpactDefaultsOmittedOrBlankWindowDaysToSeven()
    {
        using var client = factory.CreateClient();
        const string prefix = "/api/v1/markets/demo-server-01/demo-item-01/events/demo-supply-007/impact?asOfUtc=2025-06-30T00:00:00Z";

        var omitted = await client.GetFromJsonAsync<EventImpactResponse>(prefix);
        var blank = await client.GetFromJsonAsync<EventImpactResponse>($"{prefix}&windowDays=%20");

        Assert.Equal(EventImpactAnalyzer.DefaultWindowDays, omitted!.WindowDays);
        Assert.Equal(EventImpactAnalyzer.DefaultWindowDays, blank!.WindowDays);
    }

    [Fact]
    public async Task EventImpactIgnoresObservationOutsideRequestedWindow()
    {
        using var client = factory.CreateClient();
        const string utcPath = "/api/v1/markets/demo-server-01/demo-item-01/events/demo-supply-007/impact?asOfUtc=2025-06-30T00:00:00Z&windowDays=7";
        const string offsetPath = "/api/v1/markets/demo-server-01/demo-item-01/events/demo-supply-007/impact?asOfUtc=2025-06-30T08%3A00%3A00%2B08%3A00&windowDays=7";

        var before = await client.GetFromJsonAsync<EventImpactResponse>(utcPath);
        var equivalent = await client.GetFromJsonAsync<EventImpactResponse>(offsetPath);
        var upload = await client.PostAsJsonAsync("/api/v1/snapshots", new SnapshotUploadRequest
        {
            BatchId = $"event-impact-future-{Guid.NewGuid():N}",
            ServerId = "demo-server-01",
            CapturedAtUtc = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero),
            Source = "event-test",
            Observations =
            [
                new ListingObservationDto
                {
                    ItemId = "demo-item-01",
                    Price = 999999,
                    Quantity = 999999,
                    ObservedAtUtc = new DateTimeOffset(2025, 7, 1, 0, 1, 0, TimeSpan.Zero)
                }
            ]
        });
        var after = await client.GetFromJsonAsync<EventImpactResponse>(utcPath);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Equal(before, equivalent);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task EventImpactExcludesObservationAfterCutoffInsideBoundedWindow()
    {
        using var client = factory.CreateClient();
        await AddEventsAsync(Event(
            "api-event-cutoff-bound",
            null,
            MarketEventType.Holiday,
            new DateTimeOffset(2025, 6, 20, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 6, 27, 0, 0, 0, TimeSpan.Zero)));

        const string utcPath = "/api/v1/markets/demo-server-01/demo-item-01/events/api-event-cutoff-bound/impact?asOfUtc=2025-06-23T00:00:00Z&windowDays=3";
        const string offsetPath = "/api/v1/markets/demo-server-01/demo-item-01/events/api-event-cutoff-bound/impact?asOfUtc=2025-06-23T08%3A00%3A00%2B08%3A00&windowDays=3";

        var before = await client.GetFromJsonAsync<EventImpactResponse>(utcPath);
        var equivalent = await client.GetFromJsonAsync<EventImpactResponse>(offsetPath);
        var upload = await client.PostAsJsonAsync("/api/v1/snapshots", new SnapshotUploadRequest
        {
            BatchId = $"event-impact-cutoff-{Guid.NewGuid():N}",
            ServerId = "demo-server-01",
            CapturedAtUtc = new DateTimeOffset(2025, 6, 24, 0, 0, 0, TimeSpan.Zero),
            Source = "event-test",
            Observations =
            [
                new ListingObservationDto
                {
                    ItemId = "demo-item-01",
                    Price = 999999,
                    Quantity = 999999,
                    ObservedAtUtc = new DateTimeOffset(2025, 6, 24, 0, 1, 0, TimeSpan.Zero)
                }
            ]
        });
        var after = await client.GetFromJsonAsync<EventImpactResponse>(utcPath);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Equal(before, equivalent);
        Assert.Equal(before, after);
    }

    private async Task AddEventsAsync(params Event[] events)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDbContext>();
        db.Events.AddRange(events);
        await db.SaveChangesAsync();
    }

    private static Event Event(
        string id,
        string? itemId,
        MarketEventType type,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc)
        => new()
        {
            Id = id,
            ServerId = "demo-server-01",
            ItemId = itemId,
            Type = type,
            Label = id,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            CatalogKind = CatalogKind.Demo
        };
}
