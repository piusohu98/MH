using Microsoft.EntityFrameworkCore;
using MH.Server.Data;

namespace MH.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task InitialMigrationCreatesCurrentSchemaOnEmptyDatabase()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"MHMigrationTests-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(directoryPath, "market.db");
        Directory.CreateDirectory(directoryPath);

        try
        {
            var options = new DbContextOptionsBuilder<MarketDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using var db = new MarketDbContext(options);

            await db.Database.MigrateAsync();

            var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains("20260817122914_InitialCreate", appliedMigrations);
            Assert.False(await db.Servers.AnyAsync());
            Assert.False(await db.Items.AnyAsync());
            Assert.False(await db.SnapshotBatches.AnyAsync());
            Assert.False(await db.ListingObservations.AnyAsync());
            Assert.False(await db.Events.AnyAsync());
            Assert.False(await db.Recommendations.AnyAsync());
            Assert.False(await db.TradeJournals.AnyAsync());
            Assert.False(await db.ServerIndexes.AnyAsync());
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }
}
