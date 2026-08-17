using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using MH.Core.Simulation;

namespace MH.Server.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDbContext>();

        // Keep existing EnsureCreated databases compatible until a verified baseline
        // migration path is available for databases without __EFMigrationsHistory.
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await ConfigureSqliteAsync(db, cancellationToken);

        if (await HasDataAsync(db, cancellationToken))
        {
            return;
        }

        var demo = DemoGenerator.Generate();
        db.Servers.Add(demo.Server);
        db.Items.AddRange(demo.Items);
        db.SnapshotBatches.AddRange(demo.Snapshots.Select(x => x.Batch));
        db.Events.AddRange(demo.Events);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task ConfigureSqliteAsync(MarketDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        await ExecutePragmaAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
        await ExecutePragmaAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
        await ExecutePragmaAsync(connection, "PRAGMA busy_timeout = 5000;", cancellationToken);
    }

    private static async Task ExecutePragmaAsync(DbConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> HasDataAsync(MarketDbContext db, CancellationToken cancellationToken)
    {
        return await db.Servers.AnyAsync(cancellationToken)
            || await db.Items.AnyAsync(cancellationToken)
            || await db.SnapshotBatches.AnyAsync(cancellationToken)
            || await db.ListingObservations.AnyAsync(cancellationToken)
            || await db.Events.AnyAsync(cancellationToken)
            || await db.Recommendations.AnyAsync(cancellationToken)
            || await db.TradeJournals.AnyAsync(cancellationToken)
            || await db.ServerIndexes.AnyAsync(cancellationToken);
    }
}
