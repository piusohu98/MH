namespace MH.Core.Models;

public sealed class Item
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Unit { get; init; }
    public CatalogKind CatalogKind { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
