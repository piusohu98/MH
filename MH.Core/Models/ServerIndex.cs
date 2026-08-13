namespace MH.Core.Models;

public sealed class ServerIndex
{
    public required string ServerId { get; init; }
    public int ActiveItemCount { get; init; }
    public DateTimeOffset LastObservedAtUtc { get; init; }
    public int CoveragePercent { get; init; }
}
