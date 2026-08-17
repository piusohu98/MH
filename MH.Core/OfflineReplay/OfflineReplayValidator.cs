namespace MH.Core.OfflineReplay;

public static class OfflineReplayValidator
{
    public static OfflineReplayValidationResult ValidateManifest(OfflineReplayManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var issues = new List<OfflineReplayIssue>();
        if (!string.Equals(manifest.Version, OfflineReplayContract.ManifestVersion, StringComparison.Ordinal))
        {
            issues.Add(new("unsupported-version", $"Manifest version must be {OfflineReplayContract.ManifestVersion}."));
        }

        ValidateIdentifier(manifest.ReplayId, "replay-id", issues);
        ValidateUtc(manifest.CreatedAtUtc, "manifest-time", issues);

        if (manifest.Frames is null)
        {
            issues.Add(new("frames-missing", "Manifest frames must be provided."));
            return new(issues);
        }

        var frameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var frame in manifest.Frames)
        {
            if (frame is null)
            {
                issues.Add(new("frame-missing", "Manifest cannot contain a null frame."));
                continue;
            }

            ValidateIdentifier(frame.FrameId, "frame-id", issues);
            if (!string.IsNullOrEmpty(frame.FrameId) && !frameIds.Add(frame.FrameId))
            {
                issues.Add(new("duplicate-frame-id", $"Frame id '{frame.FrameId}' occurs more than once."));
            }

            ValidateUtc(frame.CapturedAtUtc, "frame-time", issues);

            if (!TryNormalizeRelativePath(frame.RelativeImagePath, out var normalizedPath))
            {
                issues.Add(new("unsafe-image-path", $"Image path '{frame.RelativeImagePath}' must be a normalized relative path."));
            }
            else
            {
                if (!imagePaths.Add(normalizedPath))
                {
                    issues.Add(new("duplicate-image-path", $"Image path '{frame.RelativeImagePath}' occurs more than once."));
                }

                if (!OfflineReplayContract.SupportedImageExtensions.Contains(Path.GetExtension(normalizedPath)))
                {
                    issues.Add(new("unsupported-image-extension", $"Image path '{frame.RelativeImagePath}' has an unsupported extension."));
                }
            }
        }

        return new(issues);
    }

    public static OfflineReplayResult Classify(
        string replayId,
        OfflineReplayFrame frame,
        IReadOnlyList<OfflineReplayCandidate>? candidates,
        string? rawText = null,
        decimal minimumConfidence = OfflineReplayContract.DefaultMinimumConfidence)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateMinimumConfidence(minimumConfidence);

        var issues = new List<OfflineReplayIssue>();
        ValidateIdentifier(replayId, "replay-id", issues);
        ValidateIdentifier(frame.FrameId, "frame-id", issues);
        ValidateUtc(frame.CapturedAtUtc, "frame-time", issues);
        if (!TryNormalizeRelativePath(frame.RelativeImagePath, out var normalizedPath))
        {
            issues.Add(new("unsafe-image-path", $"Image path '{frame.RelativeImagePath}' must be a normalized relative path."));
        }
        else if (!OfflineReplayContract.SupportedImageExtensions.Contains(Path.GetExtension(normalizedPath)))
        {
            issues.Add(new("unsupported-image-extension", $"Image path '{frame.RelativeImagePath}' has an unsupported extension."));
        }

        var safeCandidates = candidates ?? [];
        var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in safeCandidates)
        {
            if (candidate is null)
            {
                issues.Add(new("candidate-missing", "Candidate list cannot contain a null candidate."));
                continue;
            }

            ValidateIdentifier(candidate.ItemId, "candidate-id", issues);
            if (!string.IsNullOrEmpty(candidate.ItemId) && !candidateIds.Add(candidate.ItemId))
            {
                issues.Add(new("duplicate-candidate-id", $"Candidate id '{candidate.ItemId}' occurs more than once."));
            }

            if (candidate.Confidence is < 0m or > 1m)
            {
                issues.Add(new("invalid-confidence", $"Candidate '{candidate.ItemId}' confidence must be between 0 and 1."));
            }
        }

        OfflineReplayStatus status;
        if (issues.Count > 0 || safeCandidates.Count == 0)
        {
            if (safeCandidates.Count == 0 && issues.Count == 0)
            {
                issues.Add(new("candidate-missing", "At least one item candidate is required."));
            }

            status = OfflineReplayStatus.Rejected;
        }
        else if (safeCandidates.Count == 1
            && safeCandidates[0].IsConfirmed
            && safeCandidates[0].Confidence >= minimumConfidence)
        {
            status = OfflineReplayStatus.Accepted;
        }
        else
        {
            if (safeCandidates.Any(candidate => !candidate.IsConfirmed))
            {
                issues.Add(new("candidate-unconfirmed", "An item candidate requires human confirmation."));
            }

            if (safeCandidates.Any(candidate => candidate.Confidence < minimumConfidence))
            {
                issues.Add(new("candidate-low-confidence", $"Candidate confidence must reach {minimumConfidence:0.##} for automatic acceptance."));
            }

            if (safeCandidates.Count > 1)
            {
                issues.Add(new("candidate-ambiguous", "More than one item candidate requires human selection."));
            }

            status = OfflineReplayStatus.ReviewRequired;
        }

        return new(replayId, frame.FrameId, status, rawText, safeCandidates, issues);
    }

    public static bool IsSafeRelativeImagePath(string relativePath)
        => TryNormalizeRelativePath(relativePath, out _);

    private static void ValidateIdentifier(string? value, string code, ICollection<OfflineReplayIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > 128
            || value.Any(char.IsControl)
            || value.Contains('/')
            || value.Contains('\\'))
        {
            issues.Add(new($"invalid-{code}", $"{code} must be a non-empty path-free identifier."));
        }
    }

    private static void ValidateUtc(DateTimeOffset value, string code, ICollection<OfflineReplayIssue> issues)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
        {
            issues.Add(new($"invalid-{code}", $"{code} must be a non-default UTC timestamp."));
        }
    }

    private static bool TryNormalizeRelativePath(string? relativePath, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath != relativePath.Trim())
        {
            return false;
        }

        if (relativePath[0] is '/' or '\\'
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':'))
        {
            return false;
        }

        var segments = relativePath.Replace('\\', '/').Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Any(char.IsControl)))
        {
            return false;
        }

        normalizedPath = string.Join('/', segments);
        return true;
    }

    private static void ValidateMinimumConfidence(decimal minimumConfidence)
    {
        if (minimumConfidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence), "Minimum confidence must be between 0 and 1.");
        }
    }
}
