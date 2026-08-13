using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MH.Core.Models;
using CoreServer = MH.Core.Models.Server;

namespace MH.Server.Data;

public sealed class MarketDbContext : DbContext
{
    private const string ConnectionPragmaCommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";

    public MarketDbContext(DbContextOptions<MarketDbContext> options)
        : base(options)
    {
        Database.GetDbConnection().StateChange += ConfigureSqliteConnection;
    }

    public DbSet<CoreServer> Servers => Set<CoreServer>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<SnapshotBatch> SnapshotBatches => Set<SnapshotBatch>();

    public DbSet<ListingObservation> ListingObservations => Set<ListingObservation>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Recommendation> Recommendations => Set<Recommendation>();

    public DbSet<TradeJournal> TradeJournals => Set<TradeJournal>();

    public DbSet<ServerIndex> ServerIndexes => Set<ServerIndex>();

    private static void ConfigureSqliteConnection(object? sender, StateChangeEventArgs eventArgs)
    {
        if (eventArgs.CurrentState != ConnectionState.Open || sender is not DbConnection connection)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = ConnectionPragmaCommandText;
        command.ExecuteNonQuery();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CoreServer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CatalogKind);
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CatalogKind, x.Category });
        });

        modelBuilder.Entity<SnapshotBatch>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PayloadHash).IsUnique();
            entity.HasIndex(x => new { x.ServerId, x.CapturedAtUtc });
            entity.HasOne<CoreServer>()
                .WithMany()
                .HasForeignKey(x => x.ServerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ListingObservation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerId, x.ItemId, x.ObservedAtUtc });
            entity.HasIndex(x => new { x.SnapshotBatchId, x.ItemId }).IsUnique();
            entity.HasOne<SnapshotBatch>()
                .WithMany(x => x.Observations)
                .HasForeignKey(x => x.SnapshotBatchId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CoreServer>()
                .WithMany()
                .HasForeignKey(x => x.ServerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Item>()
                .WithMany()
                .HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerId, x.ItemId, x.StartsAtUtc });
            entity.HasOne<CoreServer>()
                .WithMany()
                .HasForeignKey(x => x.ServerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Item>()
                .WithMany()
                .HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Recommendation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerId, x.ItemId, x.GeneratedAtUtc });
            entity.HasOne<CoreServer>()
                .WithMany()
                .HasForeignKey(x => x.ServerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Item>()
                .WithMany()
                .HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TradeJournal>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerId, x.ItemId, x.OccurredAtUtc });
            entity.HasOne<CoreServer>()
                .WithMany()
                .HasForeignKey(x => x.ServerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Item>()
                .WithMany()
                .HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ServerIndex>(entity =>
        {
            entity.HasKey(x => x.ServerId);
            entity.HasIndex(x => x.LastObservedAtUtc);
            entity.HasOne<CoreServer>()
                .WithOne()
                .HasForeignKey<ServerIndex>(x => x.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, string>
    {
        public UtcDateTimeOffsetConverter()
            : base(
                value => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None).ToUniversalTime())
        {
        }
    }
}
