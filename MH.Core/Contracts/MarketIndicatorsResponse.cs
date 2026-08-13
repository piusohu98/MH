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
    int InlierCount30Days,
    decimal? Return7Days,
    decimal? Return30Days,
    decimal? Ewma7Days,
    decimal? Ewma30Days,
    decimal? Volatility7Days,
    decimal? Volatility30Days);
