namespace MH.Core.Models;

public sealed class ListingObservation
{
    public long Id { get; set; }
    public required string SnapshotBatchId { get; set; }
    public required string ServerId { get; init; }
    public required string ItemId { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
    public int Price { get; init; }
    public int Quantity { get; init; }
    public bool IsOcrAnomaly { get; init; }
}
