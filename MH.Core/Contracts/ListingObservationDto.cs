namespace MH.Core.Contracts;

public sealed class ListingObservationDto
{
    public string? ItemId { get; init; }
    public int Price { get; init; }
    public int Quantity { get; init; }
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public bool IsOcrAnomaly { get; init; }
}
