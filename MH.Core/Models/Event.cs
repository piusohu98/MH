namespace MH.Core.Models;

public sealed class Event
{
    public required string Id { get; init; }
    public required string ServerId { get; init; }
    public string? ItemId { get; init; }
    public MarketEventType Type { get; init; }
    public required string Label { get; init; }
    public DateTimeOffset StartsAtUtc { get; init; }
    public DateTimeOffset EndsAtUtc { get; init; }
    public CatalogKind CatalogKind { get; init; }
}
