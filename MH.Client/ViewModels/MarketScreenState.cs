using MH.Core.Contracts;

namespace MH.Client.ViewModels;

public enum MarketViewState
{
    Idle = 0,
    Loading = 1,
    Ready = 2,
    Offline = 3,
    Error = 4
}

public sealed record MarketScreenSnapshot(
    CatalogResponse Catalog,
    MarketSeriesResponse Series,
    MarketIndicatorsResponse Indicators,
    RecommendationPreviewResponse Recommendation,
    IReadOnlyList<MarketEventDto> RelevantEvents,
    EventImpactResponse? SelectedEventImpact,
    string? EventResearchError);
