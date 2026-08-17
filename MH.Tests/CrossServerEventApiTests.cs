using System.Net;
using System.Net.Http.Json;
using MH.Core;
using MH.Core.Contracts;
using MH.Core.Models;

namespace MH.Tests;

public sealed class CrossServerEventApiTests(MarketApiFactory factory) : IClassFixture<MarketApiFactory>
{
    [Fact]
    public async Task DemoOnlyOneServerReturnsSafeUnavailableCrossServerFacts()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/items/demo-item-01/events/cross-server-summary?type=Holiday&asOfUtc=2025-06-30T00:00:00Z&windowDays=7&historyDays=180&maxServers=1&maxEventsPerServer=5");
        var summary = await response.Content.ReadFromJsonAsync<CrossServerEventStandardizationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(summary);
        Assert.Equal("cross-server-event-standardization-v1", summary!.StatisticsVersion);
        Assert.Equal("per-server-event-median-equal-weight-v1", summary.StandardizationMethod);
        Assert.Equal(1, summary.SampleServerCount);
        Assert.Equal(1, summary.MaxServers);
        Assert.Equal(5, summary.MaxEventsPerServer);
        Assert.False(summary.DuringPrice.Available);
        Assert.Equal("comparable-servers<2", summary.DuringPrice.UnavailableReason);
    }

    [Fact]
    public async Task ServersWithoutEligibleEventsAreNotCountedAsSamples()
    {
        using var client = factory.CreateClient();

        var summary = await client.GetFromJsonAsync<CrossServerEventStandardizationResponse>(
            "/api/v1/items/demo-item-01/events/cross-server-summary?type=Holiday&asOfUtc=2024-01-01T00:00:00Z&historyDays=30");

        Assert.NotNull(summary);
        Assert.Equal(0, summary.SampleServerCount);
        Assert.Equal(0, summary.DuringPrice.ComparableServerCount);
        Assert.False(summary.DuringPrice.Available);
    }

    [Theory]
    [InlineData("type=DayNight")]
    [InlineData("type=Unknown")]
    [InlineData("")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&windowDays=2")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&historyDays=29")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&historyDays=367")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&maxServers=0")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&maxServers=51")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&maxEventsPerServer=0")]
    [InlineData("type=Holiday&asOfUtc=2025-06-30T00:00:00Z&maxEventsPerServer=101")]
    public async Task CrossServerSummaryRejectsInvalidParameters(string query)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/v1/items/demo-item-01/events/cross-server-summary?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CrossServerSummaryRejectsUnknownItemAndNormalizesUtc()
    {
        using var client = factory.CreateClient();
        const string path = "/api/v1/items/demo-item-01/events/cross-server-summary?type=Holiday&windowDays=7&historyDays=180&maxServers=1&maxEventsPerServer=5";

        var unknown = await client.GetAsync(
            "/api/v1/items/not-in-demo-catalog/events/cross-server-summary?type=Holiday&asOfUtc=2025-06-30T00:00:00Z");
        var utc = await client.GetFromJsonAsync<CrossServerEventStandardizationResponse>(
            $"{path}&asOfUtc=2025-06-30T00:00:00Z");
        var offset = await client.GetFromJsonAsync<CrossServerEventStandardizationResponse>(
            $"{path}&asOfUtc=2025-06-30T08%3A00%3A00%2B08%3A00");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.NotNull(utc);
        Assert.Equal(utc, offset);
    }
}
