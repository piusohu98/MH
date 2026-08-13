using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
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
