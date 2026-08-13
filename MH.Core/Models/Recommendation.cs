namespace MH.Core.Models;

public sealed class Recommendation
{
    public required string Id { get; init; }
    public required string ServerId { get; init; }
    public required string ItemId { get; init; }
    public required string Action { get; init; }
    public int ConfidencePercent { get; init; }
    public required string Rationale { get; init; }
    public DateTimeOffset GeneratedAtUtc { get; init; }
}
