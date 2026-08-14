using MH.Core.Models;
using MH.Core.Recommendations;

namespace MH.Core.Simulation;

public sealed record RecommendationValidationScenario(
    string Name,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<PriceBar> Bars,
    RecommendationAction ExpectedAction);

public static class RecommendationValidationScenarios
{
    private static readonly DateTimeOffset AsOfUtc = new(2025, 2, 1, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<RecommendationValidationScenario> All { get; } = Create();

    public static IReadOnlyList<RecommendationValidationScenario> Create()
        =>
        [
            new(
                "uptrend-with-supply-contraction",
                AsOfUtc,
                TrendBars(day => 100 + day * 2, day => 120 - day * 3),
                RecommendationAction.CandidateBuy),
            new(
                "downtrend-with-supply-expansion",
                AsOfUtc,
                TrendBars(day => 160 - day * 2, day => 20 + day * 6),
                RecommendationAction.CandidateSell),
            new(
                "short-medium-trend-conflict",
                AsOfUtc,
                ConflictBars(),
                RecommendationAction.Observe),
            new(
                "high-volatility",
                AsOfUtc,
                TrendBars(day => day % 2 == 0 ? 100 : 200, _ => 100),
                RecommendationAction.Avoid),
            new(
                "insufficient-data",
                AsOfUtc,
                [Bar(AsOfUtc.AddDays(-1), 100, 100), Bar(AsOfUtc, 101, 100)],
                RecommendationAction.DataInsufficient)
        ];

    private static IReadOnlyList<PriceBar> TrendBars(
        Func<int, int> close,
        Func<int, int> volume)
        => Enumerable.Range(0, 30)
            .Select(day => Bar(AsOfUtc.AddDays(-29 + day), close(day), volume(day)))
            .ToArray();

    private static IReadOnlyList<PriceBar> ConflictBars()
        => Enumerable.Range(0, 30)
            .Select(day =>
            {
                var close = day < 23
                    ? 100 + day * 2
                    : 144 - (day - 22) * 4;
                return Bar(AsOfUtc.AddDays(-29 + day), close, 100);
            })
            .ToArray();

    private static PriceBar Bar(DateTimeOffset endUtc, int close, int volume)
        => new(
            endUtc.AddDays(-1),
            endUtc,
            close,
            close,
            close,
            close,
            volume,
            false);
}
