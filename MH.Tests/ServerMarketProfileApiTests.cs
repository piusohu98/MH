using System.Net;
using System.Net.Http.Json;
using MH.Core.Contracts;
using MH.Core.Simulation;

namespace MH.Tests;

public sealed class ServerMarketProfileApiTests(MarketApiFactory factory) : IClassFixture<MarketApiFactory>
{
    private const string ProfilePath = "/api/v1/servers/demo-server-01/market-profile?asOfUtc=2025-06-30T00:00:00Z";

    [Fact]
    public async Task DemoProfileReturnsVersionedProxyMetrics()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ProfilePath);
        var profile = await response.Content.ReadFromJsonAsync<ServerMarketProfileResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(profile);
        Assert.Equal(DemoGenerator.ServerId, profile!.ServerId);
        Assert.Equal("server-market-profile-v1", profile.StatisticsVersion);
        Assert.Equal(ServerProxyAvailability.Available, profile.Activity.Availability);
        Assert.Equal(ServerProxyAvailability.Available, profile.HighValueDemand.Availability);
        Assert.InRange(profile.Activity.Score!.Value, 0m, 100m);
        Assert.InRange(profile.HighValueDemand.Score!.Value, 0m, 100m);
        Assert.Contains("不代表真实在线人数", profile.ScopeNotice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProfileRejectsInvalidParametersAndUnknownServer()
    {
        using var client = factory.CreateClient();

        var missingAsOf = await client.GetAsync("/api/v1/servers/demo-server-01/market-profile");
        var invalidAsOf = await client.GetAsync("/api/v1/servers/demo-server-01/market-profile?asOfUtc=not-a-date");
        var missingOffset = await client.GetAsync("/api/v1/servers/demo-server-01/market-profile?asOfUtc=2025-06-30T00:00:00");
        var unknownServer = await client.GetAsync("/api/v1/servers/missing/market-profile?asOfUtc=2025-06-30T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, missingAsOf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidAsOf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingOffset.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownServer.StatusCode);
    }

    [Fact]
    public async Task ProfileIsUtcEquivalentAndIgnoresFutureObservations()
    {
        using var client = factory.CreateClient();
        var baseline = await client.GetFromJsonAsync<ServerMarketProfileResponse>(ProfilePath);

        var upload = await client.PostAsJsonAsync("/api/v1/snapshots", new SnapshotUploadRequest
        {
            BatchId = $"test-profile-future-{Guid.NewGuid():N}",
            ServerId = DemoGenerator.ServerId,
            CapturedAtUtc = new DateTimeOffset(2025, 6, 30, 1, 0, 0, TimeSpan.Zero),
            Source = "test",
            Observations =
            [
                new ListingObservationDto
                {
                    ItemId = "demo-item-24",
                    Price = 999999,
                    Quantity = 1
                }
            ]
        });
        var after = await client.GetFromJsonAsync<ServerMarketProfileResponse>(ProfilePath);
        var equivalent = await client.GetFromJsonAsync<ServerMarketProfileResponse>(
            "/api/v1/servers/demo-server-01/market-profile?asOfUtc=2025-06-30T08:00:00%2B08:00");

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        AssertProfilesEqual(baseline, after);
        AssertProfilesEqual(baseline, equivalent);
    }

    [Fact]
    public async Task ProfileStopsScoringStaleData()
    {
        using var client = factory.CreateClient();

        var profile = await client.GetFromJsonAsync<ServerMarketProfileResponse>(
            "/api/v1/servers/demo-server-01/market-profile?asOfUtc=2025-07-03T00:00:00Z");

        Assert.NotNull(profile);
        Assert.Equal(ServerProxyAvailability.Stale, profile!.Activity.Availability);
        Assert.Equal(ServerProxyAvailability.Stale, profile.HighValueDemand.Availability);
        Assert.Null(profile.Activity.Score);
        Assert.Null(profile.HighValueDemand.Score);
    }

    private static void AssertProfilesEqual(
        ServerMarketProfileResponse? expected,
        ServerMarketProfileResponse? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected!.ServerId, actual!.ServerId);
        Assert.Equal(expected.AsOfUtc, actual.AsOfUtc);
        Assert.Equal(expected.Activity.Score, actual.Activity.Score);
        Assert.Equal(expected.Activity.Confidence, actual.Activity.Confidence);
        Assert.True(expected.Activity.Evidence.SequenceEqual(actual.Activity.Evidence));
        Assert.Equal(expected.HighValueDemand.Score, actual.HighValueDemand.Score);
        Assert.Equal(expected.HighValueDemand.Confidence, actual.HighValueDemand.Confidence);
        Assert.True(expected.HighValueDemand.Evidence.SequenceEqual(actual.HighValueDemand.Evidence));
    }
}
