namespace MH.Core.Contracts;

public sealed record PriceBarDto(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int Open,
    int High,
    int Low,
    int Close,
    int Volume,
    bool HasOcrAnomaly);

public sealed record MarketSeriesResponse(
    string ServerId,
    string ItemId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    IReadOnlyList<PriceBarDto> Bars);
