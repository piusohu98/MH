namespace MH.Core.Models;

public sealed class SnapshotBatch
{
    public required string Id { get; init; }
    public required string ServerId { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public DateTimeOffset UploadedAtUtc { get; init; }
    public required string Source { get; init; }
    public required string PayloadHash { get; init; }
    public CatalogKind CatalogKind { get; init; }
    public List<ListingObservation> Observations { get; init; } = [];
}
