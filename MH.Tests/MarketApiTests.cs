using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MH.Core.Contracts;
using MH.Core.Simulation;
using MH.Server.Data;

namespace MH.Tests;

public sealed class MarketApiFactory : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"MHMarketTests-{Guid.NewGuid():N}", "market.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Path", DatabasePath);
    }
}

public sealed class MarketApiTests(MarketApiFactory factory) : IClassFixture<MarketApiFactory>
{
    [Fact]
    public void DemoGeneratorIsDeterministicAndComplete()
    {
        var first = DemoGenerator.Generate(42);
        var second = DemoGenerator.Generate(42);

        Assert.Equal(DemoGenerator.ItemCount, first.Items.Count);
        Assert.Equal(DemoGenerator.HistoryDays * 4, first.Snapshots.Count);
        Assert.Equal(DemoGenerator.ItemCount * DemoGenerator.HistoryDays * 4, first.Snapshots.Sum(x => x.Observations.Count));
        Assert.Contains(first.Events, x => x.Type == MH.Core.Models.MarketEventType.Holiday);
        Assert.Contains(first.Events, x => x.Type == MH.Core.Models.MarketEventType.SupplyChange);
        Assert.Contains(first.Snapshots.SelectMany(x => x.Observations), x => x.IsOcrAnomaly);
        Assert.Equal(first.Items.Select(x => x.Id), second.Items.Select(x => x.Id));
        Assert.Equal(first.Snapshots.SelectMany(x => x.Observations).Select(x => (x.ItemId, x.ObservedAtUtc, x.Price, x.Quantity, x.IsOcrAnomaly)),
            second.Snapshots.SelectMany(x => x.Observations).Select(x => (x.ItemId, x.ObservedAtUtc, x.Price, x.Quantity, x.IsOcrAnomaly)));
    }

    [Fact]
    public async Task FirstDatabaseCreatesSchemaAndDemoCatalog()
    {
        using var client = factory.CreateClient();
        var catalog = await client.GetFromJsonAsync<CatalogResponse>("/api/v1/catalog");

        Assert.NotNull(catalog);
        Assert.True(File.Exists(factory.DatabasePath));
        Assert.Equal(MH.Core.Models.CatalogKind.Demo, catalog!.CatalogKind);
        Assert.Single(catalog.Servers);
        Assert.Equal(DemoGenerator.ItemCount, catalog.Items.Count);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDbContext>();
        Assert.True(await db.Database.CanConnectAsync());
        Assert.NotEmpty(await db.SnapshotBatches.ToListAsync());
    }

    [Fact]
    public async Task NewDatabaseConnectionsKeepSqlitePragmas()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDbContext>();
        await db.Database.OpenConnectionAsync();
        var connection = db.Database.GetDbConnection();

        static async Task<object?> ScalarAsync(DbConnection connection, string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync();
        }

        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "PRAGMA foreign_keys;")));
        Assert.Equal("wal", Convert.ToString(await ScalarAsync(connection, "PRAGMA journal_mode;"))?.ToLowerInvariant());
        Assert.Equal(5000L, Convert.ToInt64(await ScalarAsync(connection, "PRAGMA busy_timeout;")));
    }

    [Fact]
    public async Task CatalogAndSeriesAcceptValidParameters()
    {
        using var client = factory.CreateClient();
        var catalogResponse = await client.GetAsync("/api/v1/catalog?kind=demo");
        var seriesResponse = await client.GetAsync("/api/v1/markets/demo-server-01/demo-item-01/series");

        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, seriesResponse.StatusCode);
        var series = await seriesResponse.Content.ReadFromJsonAsync<MarketSeriesResponse>();
        Assert.NotNull(series);
        Assert.Equal(DemoGenerator.HistoryDays, series!.Bars.Count);
        Assert.All(series.Bars, bar => Assert.Equal(TimeSpan.Zero, bar.StartUtc.Offset));
    }

    [Fact]
    public async Task IndicatorsReturnDemoMetricsThroughUtcCutoff()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/markets/demo-server-01/demo-item-01/indicators?asOfUtc=2025-06-30T00:00:00Z");
        var indicators = await response.Content.ReadFromJsonAsync<MarketIndicatorsResponse>();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(indicators);
        Assert.Equal("demo-server-01", indicators!.ServerId);
        Assert.Equal("demo-item-01", indicators.ItemId);
        Assert.Equal(new DateTimeOffset(2025, 6, 30, 0, 0, 0, TimeSpan.Zero), indicators.CutoffUtc);
        Assert.Equal(TimeSpan.Zero, indicators.CutoffUtc.Offset);
        Assert.NotNull(indicators.RobustMedian7Days);
        Assert.NotNull(indicators.RobustMedian30Days);
        Assert.NotNull(indicators.Mad7Days);
        Assert.NotNull(indicators.Mad30Days);
        Assert.NotNull(indicators.Return7Days);
        Assert.NotNull(indicators.Return30Days);
        Assert.NotNull(indicators.Ewma7Days);
        Assert.NotNull(indicators.Ewma30Days);
        Assert.NotNull(indicators.Volatility7Days);
        Assert.NotNull(indicators.Volatility30Days);
        AssertDecimalPropertyMatches(json.RootElement, "visibleSupplyChange7Days", indicators.VisibleSupplyChange7Days);
        AssertDecimalPropertyMatches(json.RootElement, "visibleSupplyChange30Days", indicators.VisibleSupplyChange30Days);
        AssertDecimalPropertyMatches(json.RootElement, "dataAgeHours", indicators.DataAgeHours);
        Assert.False(json.RootElement.TryGetProperty("VisibleSupplyChange7Days", out _));
        Assert.True(indicators.InlierCount7Days <= indicators.SampleCount7Days);
        Assert.True(indicators.InlierCount30Days <= indicators.SampleCount30Days);
    }

    [Fact]
    public async Task IndicatorsIgnoreSnapshotsAfterRequestedAsOf()
    {
        using var client = factory.CreateClient();
        const string path = "/api/v1/markets/demo-server-01/demo-item-24/indicators?asOfUtc=2025-06-30T00:00:00Z";
        var before = await client.GetFromJsonAsync<MarketIndicatorsResponse>(path);

        var upload = await client.PostAsJsonAsync("/api/v1/snapshots", new SnapshotUploadRequest
        {
            BatchId = $"test-indicators-future-{Guid.NewGuid():N}",
            ServerId = DemoGenerator.ServerId,
            CapturedAtUtc = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero),
            Source = "test",
            Observations =
            [
                new ListingObservationDto
                {
                    ItemId = "demo-item-24",
                    Price = 999999,
                    Quantity = 1,
                    ObservedAtUtc = new DateTimeOffset(2025, 7, 1, 0, 1, 0, TimeSpan.Zero)
                }
            ]
        });

        var after = await client.GetFromJsonAsync<MarketIndicatorsResponse>(path);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.NotNull(before);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task IndicatorsIgnoreSnapshotsOlderThanThirtyDayWindow()
    {
        using var client = factory.CreateClient();
        const string path = "/api/v1/markets/demo-server-01/demo-item-24/indicators?asOfUtc=2025-06-30T00:00:00Z";
        var before = await client.GetFromJsonAsync<MarketIndicatorsResponse>(path);

        var upload = await client.PostAsJsonAsync("/api/v1/snapshots", new SnapshotUploadRequest
        {
            BatchId = $"test-indicators-old-{Guid.NewGuid():N}",
            ServerId = DemoGenerator.ServerId,
            CapturedAtUtc = new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero),
            Source = "test",
            Observations =
            [
                new ListingObservationDto
                {
                    ItemId = "demo-item-24",
                    Price = 1,
                    Quantity = 1,
                    ObservedAtUtc = new DateTimeOffset(2024, 12, 1, 0, 1, 0, TimeSpan.Zero)
                }
            ]
        });

        var after = await client.GetFromJsonAsync<MarketIndicatorsResponse>(path);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.NotNull(before);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task IndicatorsUseLatestOutsideWindowHistoryOnlyForDataAge()
    {
        using var client = factory.CreateClient();
        var historicalObservationUtc = new DateTimeOffset(2024, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var upload = await client.PostAsJsonAsync("/api/v1/snapshots", new SnapshotUploadRequest
        {
            BatchId = $"test-indicators-age-anchor-{Guid.NewGuid():N}",
            ServerId = DemoGenerator.ServerId,
            CapturedAtUtc = historicalObservationUtc,
            Source = "test",
            Observations =
            [
                new ListingObservationDto
                {
                    ItemId = "demo-item-23",
                    Price = 1234,
                    Quantity = 7,
                    ObservedAtUtc = historicalObservationUtc
                }
            ]
        });
        var indicators = await client.GetFromJsonAsync<MarketIndicatorsResponse>(
            "/api/v1/markets/demo-server-01/demo-item-23/indicators?asOfUtc=2025-01-01T12:30:00Z");

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.NotNull(indicators);
        Assert.Equal(0, indicators!.SampleCount7Days);
        Assert.Equal(0, indicators.SampleCount30Days);
        Assert.Null(indicators.VisibleSupplyChange7Days);
        Assert.Null(indicators.VisibleSupplyChange30Days);
        Assert.Equal(1068.5m, indicators.DataAgeHours);
    }

    [Fact]
    public async Task IndicatorsNormalizeEquivalentUtcOffsets()
    {
        using var client = factory.CreateClient();
        var utc = await client.GetFromJsonAsync<MarketIndicatorsResponse>(
            "/api/v1/markets/demo-server-01/demo-item-01/indicators?asOfUtc=2025-06-30T00:00:00Z");
        var offset = await client.GetFromJsonAsync<MarketIndicatorsResponse>(
            "/api/v1/markets/demo-server-01/demo-item-01/indicators?asOfUtc=2025-06-30T08%3A00%3A00%2B08%3A00");

        Assert.NotNull(utc);
        Assert.Equal(utc, offset);
    }

    [Fact]
    public async Task IndicatorsRequireValidAsOfAndReturnProblemDetails()
    {
        using var client = factory.CreateClient();
        var missing = await client.GetAsync("/api/v1/markets/demo-server-01/demo-item-01/indicators");
        var invalid = await client.GetAsync("/api/v1/markets/demo-server-01/demo-item-01/indicators?asOfUtc=not-a-timestamp");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task IndicatorsReturnNotFoundForUnknownMarketEntity()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/markets/demo-server-01/not-in-demo-catalog/indicators?asOfUtc=2025-06-30T00:00:00Z");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task IndicatorsKeepAnalyzerNullAndCountSemanticsWhenEarlyDataIsInsufficient()
    {
        using var client = factory.CreateClient();
        var indicators = await client.GetFromJsonAsync<MarketIndicatorsResponse>(
            "/api/v1/markets/demo-server-01/demo-item-01/indicators?asOfUtc=2025-01-03T00:00:00Z");

        Assert.NotNull(indicators);
        Assert.Equal(2, indicators!.SampleCount7Days);
        Assert.Equal(2, indicators.SampleCount30Days);
        Assert.Equal(0, indicators.InlierCount7Days);
        Assert.Equal(0, indicators.InlierCount30Days);
        Assert.Null(indicators.RobustMedian7Days);
        Assert.Null(indicators.RobustMedian30Days);
        Assert.Null(indicators.Mad7Days);
        Assert.Null(indicators.Mad30Days);
        Assert.Null(indicators.Return7Days);
        Assert.Null(indicators.Return30Days);
        Assert.Null(indicators.Ewma7Days);
        Assert.Null(indicators.Ewma30Days);
        Assert.Null(indicators.Volatility7Days);
        Assert.Null(indicators.Volatility30Days);
    }

    private static void AssertDecimalPropertyMatches(JsonElement json, string propertyName, decimal? expected)
    {
        var property = json.GetProperty(propertyName);
        if (expected.HasValue)
        {
            Assert.Equal(expected.Value, property.GetDecimal());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, property.ValueKind);
        }
    }

    [Fact]
    public async Task InvalidParametersReturnProblemDetails()
    {
        using var client = factory.CreateClient();
        var catalogResponse = await client.GetAsync("/api/v1/catalog?kind=unknown");
        var seriesResponse = await client.GetAsync("/api/v1/markets/demo-server-01/demo-item-01/series?fromUtc=2030-01-02T00:00:00Z&toUtc=2030-01-01T00:00:00Z");
        var catalogContentType = catalogResponse.Content.Headers.ContentType?.MediaType;

        Assert.Equal(HttpStatusCode.BadRequest, catalogResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, seriesResponse.StatusCode);
        Assert.Equal("application/problem+json", catalogContentType);
    }

    [Fact]
    public async Task SnapshotUploadIsIdempotent()
    {
        using var client = factory.CreateClient();
        var request = new SnapshotUploadRequest
        {
            BatchId = "test-idempotent-batch",
            ServerId = DemoGenerator.ServerId,
            CapturedAtUtc = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Source = "test",
            Observations =
            [
                new ListingObservationDto
                {
                    ItemId = "demo-item-01",
                    Price = 1234,
                    Quantity = 7,
                    ObservedAtUtc = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero)
                }
            ]
        };

        var first = await client.PostAsJsonAsync("/api/v1/snapshots", request);
        var second = await client.PostAsJsonAsync("/api/v1/snapshots", request);
        var firstBody = await first.Content.ReadFromJsonAsync<SnapshotUploadResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<SnapshotUploadResponse>();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotNull(firstBody);
        Assert.NotNull(secondBody);
        Assert.False(firstBody!.AlreadyExists);
        Assert.True(secondBody!.AlreadyExists);
        Assert.Equal(firstBody.BatchId, secondBody.BatchId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDbContext>();
        Assert.Equal(1, await db.SnapshotBatches.CountAsync(x => x.Id == request.BatchId));
        Assert.Equal(1, await db.ListingObservations.CountAsync(x => x.SnapshotBatchId == request.BatchId));
    }

    [Fact]
    public async Task SnapshotUtcOffsetRoundTripsAsUtc()
    {
        using var client = factory.CreateClient();
        var request = new SnapshotUploadRequest
        {
            BatchId = "test-utc-batch",
            ServerId = DemoGenerator.ServerId,
            CapturedAtUtc = new DateTimeOffset(2031, 2, 3, 12, 0, 0, TimeSpan.FromHours(8)),
            Source = "test",
            Observations =
            [
                new ListingObservationDto
                {
                    ItemId = "demo-item-02",
                    Price = 2345,
                    Quantity = 3,
                    ObservedAtUtc = new DateTimeOffset(2031, 2, 3, 12, 30, 0, TimeSpan.FromHours(8))
                }
            ]
        };

        var upload = await client.PostAsJsonAsync("/api/v1/snapshots", request);
        var series = await client.GetFromJsonAsync<MarketSeriesResponse>("/api/v1/markets/demo-server-01/demo-item-02/series?fromUtc=2031-02-03T00:00:00Z&toUtc=2031-02-04T00:00:00Z");

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.NotNull(series);
        var bar = Assert.Single(series!.Bars);
        Assert.Equal(new DateTimeOffset(2031, 2, 3, 0, 0, 0, TimeSpan.Zero), bar.StartUtc);
    }
}
