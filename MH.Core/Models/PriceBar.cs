namespace MH.Core.Models;

public sealed record PriceBar(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int Open,
    int High,
    int Low,
    int Close,
    int Volume,
    bool HasOcrAnomaly);
