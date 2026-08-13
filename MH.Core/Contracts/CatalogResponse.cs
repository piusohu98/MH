using MH.Core.Models;

namespace MH.Core.Contracts;

public sealed record ServerDto(
    string Id,
    string Name,
    string Region,
    CatalogKind CatalogKind,
    DateTimeOffset CreatedAtUtc);

public sealed record ItemDto(
    string Id,
    string Name,
    string Category,
    string Unit,
    CatalogKind CatalogKind,
    DateTimeOffset CreatedAtUtc);

public sealed record CatalogResponse(
    CatalogKind CatalogKind,
    IReadOnlyList<ServerDto> Servers,
    IReadOnlyList<ItemDto> Items);
