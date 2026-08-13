namespace MH.Core.Contracts;

public sealed record SnapshotUploadResponse(
    string BatchId,
    bool AlreadyExists,
    int AcceptedObservations);
