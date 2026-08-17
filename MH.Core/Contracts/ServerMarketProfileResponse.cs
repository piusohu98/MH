namespace MH.Core.Contracts;

public enum ServerProxyAvailability
{
    Available = 0,
    InsufficientData = 1,
    Stale = 2
}

public enum ServerProxyLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public sealed record ServerProxyEvidence(
    string Code,
    decimal Value,
    string Unit);

public sealed record ServerProxyMetric(
    ServerProxyAvailability Availability,
    decimal? Score,
    ServerProxyLevel Level,
    decimal Confidence,
    int ObservationCount,
    int ObservedItemCount,
    int TransitionCount,
    decimal? DataAgeHours,
    IReadOnlyList<ServerProxyEvidence> Evidence,
    string? UnavailableReason);

public sealed record ServerMarketProfileResponse(
    string ServerId,
    DateTimeOffset AsOfUtc,
    int WindowDays,
    string StatisticsVersion,
    ServerProxyMetric Activity,
    ServerProxyMetric HighValueDemand,
    string ScopeNotice);
