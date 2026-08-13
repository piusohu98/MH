namespace MH.Core.Contracts;

public sealed class SnapshotUploadRequest
{
    public string? BatchId { get; init; }
    public string? ServerId { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public string? Source { get; init; }
    public List<ListingObservationDto>? Observations { get; init; }
}
