namespace MH.Core.OfflineReplay;

public static class OfflineReplayExportContract
{
    public const string Version = "offline-replay-export-v1";
    public const string DefaultSourceKind = "offline-replay";
}

public enum OfflineReplayEffectiveDecision
{
    AutoAccepted = 0,
    ManuallyAccepted = 1,
    ManuallyRejected = 2,
    Unprocessed = 3
}

public sealed record OfflineReplayExportClassification(
    OfflineReplayStatus Status,
    string Reason);

public sealed record OfflineReplayExportFrame(
    string FrameId,
    string RelativeImagePath,
    string ImageSha256,
    DateTimeOffset CapturedAtUtc,
    string? RawText,
    IReadOnlyList<OfflineReplayCandidate> Candidates,
    IReadOnlyList<OfflineReplayIssue> Issues,
    OfflineReplayExportClassification OriginalClassification,
    OfflineReplayEffectiveDecision EffectiveDecision,
    string? EffectiveCandidateItemId,
    bool IsDecisionAccepted);

public sealed record OfflineReplayExportDocument(
    string Version,
    string SourceKind,
    string ReplayId,
    DateTimeOffset ExportedAtUtc,
    string ManifestSha256,
    bool ReviewSidecarPresent,
    string ReviewSidecarSha256,
    IReadOnlyList<OfflineReplayExportFrame> Frames);
