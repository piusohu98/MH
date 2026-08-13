namespace MH.Core.Models;

public sealed class TradeJournal
{
    public required string Id { get; init; }
    public required string ServerId { get; init; }
    public required string ItemId { get; init; }
    public int Quantity { get; init; }
    public int UnitPrice { get; init; }
    public required string Side { get; init; }
    public required string Notes { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
}
