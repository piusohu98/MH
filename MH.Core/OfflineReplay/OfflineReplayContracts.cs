namespace MH.Core.OfflineReplay;

public static class OfflineReplayContract
{
    public const string ManifestVersion = "offline-replay-v1";
    public const decimal DefaultMinimumConfidence = 0.85m;

    public static IReadOnlySet<string> SupportedImageExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp",
            ".jpeg",
            ".jpg",
            ".png"
        };
}

public sealed record OfflineReplayManifest(
    string Version,
    string ReplayId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<OfflineReplayFrame> Frames);

public sealed record OfflineReplayFrame(
    string FrameId,
    string RelativeImagePath,
    DateTimeOffset CapturedAtUtc);

public sealed record OfflineReplayCandidate(
    string ItemId,
    string? DisplayName,
    decimal Confidence,
    bool IsConfirmed);

public enum OfflineReplayStatus
{
    Accepted = 0,
    ReviewRequired = 1,
    Rejected = 2
}

public sealed record OfflineReplayIssue(string Code, string Detail);

public sealed record OfflineReplayResult(
    string ReplayId,
    string FrameId,
    OfflineReplayStatus Status,
    string? RawText,
    IReadOnlyList<OfflineReplayCandidate> Candidates,
    IReadOnlyList<OfflineReplayIssue> Issues);

public sealed record OfflineReplayValidationResult(IReadOnlyList<OfflineReplayIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
