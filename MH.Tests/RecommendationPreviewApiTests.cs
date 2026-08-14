using System.Net;
using System.Text.Json;
using System.Net.Http.Json;
using MH.Core.Backtesting;
using MH.Core.Contracts;
using MH.Core.Recommendations;
using MH.Core.Simulation;

namespace MH.Tests;

public sealed class RecommendationPreviewApiTests(MarketApiFactory factory) : IClassFixture<MarketApiFactory>
{
    private const string PreviewPath = "/api/v1/markets/demo-server-01/demo-item-01/recommendation?asOfUtc=2025-06-30T00:00:00Z";

    [Fact]
    public async Task MissingOrInvalidAsOfReturnsProblemDetails()
    {
        using var client = factory.CreateClient();

        var missing = await client.GetAsync("/api/v1/markets/demo-server-01/demo-item-01/recommendation");
        var invalid = await client.GetAsync("/api/v1/markets/demo-server-01/demo-item-01/recommendation?asOfUtc=2025-06-30T00:00:00");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownServerOrItemReturnsNotFound()
    {
        using var client = factory.CreateClient();

        var unknownServer = await client.GetAsync(
            "/api/v1/markets/not-a-server/demo-item-01/recommendation?asOfUtc=2025-06-30T00:00:00Z");
        var unknownItem = await client.GetAsync(
            "/api/v1/markets/demo-server-01/not-an-item/recommendation?asOfUtc=2025-06-30T00:00:00Z");

        Assert.Equal(HttpStatusCode.NotFound, unknownServer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownItem.StatusCode);
        Assert.Equal("application/problem+json", unknownServer.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/problem+json", unknownItem.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ReturnsReadOnlyRecommendationAndFixedResearchAssumptions()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(PreviewPath);
        var preview = await response.Content.ReadFromJsonAsync<RecommendationPreviewResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(preview);
        Assert.Equal("demo-server-01", preview!.ServerId);
        Assert.Equal("demo-item-01", preview.ItemId);
        Assert.Equal(new DateTimeOffset(2025, 6, 30, 0, 0, 0, TimeSpan.Zero), preview.AsOfUtc);
        Assert.Equal(TimeSpan.Zero, preview.AsOfUtc.Offset);
        Assert.NotNull(preview.Decision);
        Assert.Equal(BacktestQualityGate.GateVersion, preview.QualityGate.GateVersion);
        Assert.Equal(RecommendationRule.RuleVersion, preview.QualityGate.RuleVersion);
        Assert.Equal(3, preview.QualityGate.Summary.WindowCount);
        Assert.Equal(100_000m, preview.ResearchAssumptions.InitialCapital);
        Assert.Equal(0.01m, preview.ResearchAssumptions.TradingCostRate);
        Assert.Equal(0.005m, preview.ResearchAssumptions.SlippageRate);
        Assert.Equal(3, preview.ResearchAssumptions.WindowCount);
        Assert.Equal(40, preview.ResearchAssumptions.WindowDays);
        Assert.False(preview.IsActionable && preview.QualityGate.Status != BacktestQualityStatus.TrialEligible);
        Assert.Equal(
            preview.QualityGate.Status == BacktestQualityStatus.TrialEligible
                && preview.Decision.Action is RecommendationAction.CandidateBuy or RecommendationAction.CandidateSell,
            preview.IsActionable);
    }

    [Fact]
    public async Task InsufficientDataStillReturnsOkAndIsNotActionable()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/markets/demo-server-01/demo-item-01/recommendation?asOfUtc=2025-01-03T00:00:00Z");
        var preview = await response.Content.ReadFromJsonAsync<RecommendationPreviewResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(preview);
        Assert.Equal(RecommendationAction.DataInsufficient, preview!.Decision.Action);
        Assert.Equal(BacktestQualityStatus.ResearchOnly, preview.QualityGate.Status);
        Assert.False(preview.IsActionable);
    }

    [Fact]
    public async Task EquivalentUtcOffsetsProduceTheSameNormalizedResponse()
    {
        using var client = factory.CreateClient();
        var utc = await client.GetStringAsync(PreviewPath);
        var offset = await client.GetStringAsync(
            "/api/v1/markets/demo-server-01/demo-item-01/recommendation?asOfUtc=2025-06-30T08%3A00%3A00%2B08%3A00");

        Assert.Equal(utc, offset);
    }

    [Fact]
    public async Task FutureSnapshotsCannotChangeTheCutoffResponse()
    {
        using var client = factory.CreateClient();
        var before = await client.GetStringAsync(PreviewPath);
        var upload = await client.PostAsJsonAsync("/api/v1/snapshots", new SnapshotUploadRequest
        {
            BatchId = $"test-recommendation-future-{Guid.NewGuid():N}",
            ServerId = DemoGenerator.ServerId,
            CapturedAtUtc = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero),
            Source = "test",
            Observations =
            [
                new ListingObservationDto
                {
                    ItemId = "demo-item-01",
                    Price = 999_999,
                    Quantity = 1,
                    ObservedAtUtc = new DateTimeOffset(2025, 7, 1, 0, 1, 0, TimeSpan.Zero)
                }
            ]
        });
        var after = await client.GetStringAsync(PreviewPath);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task OlderSnapshotsOutsideTheBoundedHistoryCannotChangeTheResponse()
    {
        using var client = factory.CreateClient();
        var before = await client.GetStringAsync(PreviewPath);
        var upload = await client.PostAsJsonAsync("/api/v1/snapshots", new SnapshotUploadRequest
        {
            BatchId = $"test-recommendation-old-{Guid.NewGuid():N}",
            ServerId = DemoGenerator.ServerId,
            CapturedAtUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Source = "test",
            Observations =
            [
                new ListingObservationDto
                {
                    ItemId = "demo-item-01",
                    Price = 1,
                    Quantity = 1,
                    ObservedAtUtc = new DateTimeOffset(2024, 1, 1, 0, 1, 0, TimeSpan.Zero)
                }
            ]
        });
        var after = await client.GetStringAsync(PreviewPath);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task SameInputProducesTheSamePreviewResponse()
    {
        using var client = factory.CreateClient();

        var first = await client.GetStringAsync(PreviewPath);
        var second = await client.GetStringAsync(PreviewPath);

        using var firstJson = JsonDocument.Parse(first);
        using var secondJson = JsonDocument.Parse(second);
        Assert.True(JsonElement.DeepEquals(firstJson.RootElement, secondJson.RootElement));
    }
}
