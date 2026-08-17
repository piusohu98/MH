using System.Net;
using System.Net.Http.Json;
using MH.Core;
using MH.Core.Contracts;

namespace MH.Tests;

public sealed class EventPatternSummaryApiTests(MarketApiFactory factory) : IClassFixture<MarketApiFactory>
{
    [Fact]
    public async Task SummaryReturnsVersionedFactsWithBoundedInput()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/markets/demo-server-01/demo-item-01/events/summary?type=Holiday&asOfUtc=2025-06-30T00:00:00Z&windowDays=7&historyDays=180&maxEvents=3");
        var summary = await response.Content.ReadFromJsonAsync<EventPatternSummaryResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(summary);
        Assert.Equal("event-pattern-summary-v1", summary!.StatisticsVersion);
        Assert.Equal("demo-server-01", summary.ServerId);
        Assert.Equal("demo-item-01", summary.ItemId);
        Assert.Equal(MH.Core.Models.MarketEventType.Holiday, summary.EventType);
        Assert.Equal(7, summary.WindowDays);
        Assert.Equal(180, summary.HistoryDays);
        Assert.Equal(3, summary.MaxEvents);
        Assert.InRange(summary.SampleEventCount, 0, 3);
        Assert.Equal(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), summary.InputStartUtc);
        Assert.Equal(new DateTimeOffset(2025, 6, 30, 0, 0, 0, TimeSpan.Zero), summary.InputEndUtc);
    }

    [Theory]
    [InlineData("type=DayNight")]
    [InlineData("type=Unknown")]
    [InlineData("")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00&windowDays=7")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&windowDays=2")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&historyDays=29")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&historyDays=367")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&maxEvents=0")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&maxEvents=101")]
    public async Task SummaryRejectsInvalidParameters(string query)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/v1/markets/demo-server-01/demo-item-01/events/summary?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SummaryRequiresQueryableMarket()
    {
        using var client = factory.CreateClient();
        const string query = "type=Holiday&asOfUtc=2025-06-30T00:00:00Z";

        var response = await client.GetAsync(
            $"/api/v1/markets/not-a-server/demo-item-01/events/summary?{query}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
