namespace MH.Core.Models;

public sealed class Server
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Region { get; init; }
    public CatalogKind CatalogKind { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}
