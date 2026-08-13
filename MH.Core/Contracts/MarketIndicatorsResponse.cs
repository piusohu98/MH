namespace MH.Core.Contracts;

public sealed record MarketIndicatorsResponse(
    string ServerId,
    string ItemId,
    DateTimeOffset CutoffUtc,
    decimal? RobustMedian7Days,
    decimal? RobustMedian30Days,
    decimal? Mad7Days,
    decimal? Mad30Days,
    int SampleCount7Days,
    int SampleCount30Days,
    int InlierCount7Days,
    int InlierCount30Days);
