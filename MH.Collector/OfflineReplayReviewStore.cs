using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MH.Core.OfflineReplay;

namespace MH.Collector;

public sealed class OfflineReplayReviewStore
{
    public const string SidecarFileName = ".offline-replay-review.json";
    private const string SidecarVersion = "offline-replay-review-v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<OfflineReplayReviewDecision>> LoadAsync(
        string directoryPath,
        IReadOnlyList<OfflineReplayFrameResult> frames,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(frames);

        var directory = GetVerifiedDirectoryPath(directoryPath);
        var sidecarPath = GetSidecarPathFromVerifiedDirectory(directory);
        if (!File.Exists(sidecarPath))
        {
            return [];
        }

        try
        {
            var manifestSha256 = await ComputeManifestSha256Async(directory, cancellationToken).ConfigureAwait(false);
            var json = await File.ReadAllTextAsync(sidecarPath, cancellationToken).ConfigureAwait(false);
            var sidecar = JsonSerializer.Deserialize<OfflineReplayReviewSidecar>(json, JsonOptions);
            if (sidecar is null
                || !string.Equals(sidecar.Version, SidecarVersion, StringComparison.Ordinal)
                || !string.Equals(sidecar.ManifestSha256, manifestSha256, StringComparison.Ordinal)
                || sidecar.Decisions is null)
            {
                return [];
            }

            return ValidateAndOrderDecisions(frames, sidecar.Decisions);
        }
        catch (JsonException)
        {
            return [];
        }
        catch (NotSupportedException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public async Task SaveAsync(
        string directoryPath,
        IReadOnlyList<OfflineReplayFrameResult> frames,
        IReadOnlyList<OfflineReplayReviewDecision> decisions,
        CancellationToken cancellationToken = default,
        string? expectedManifestSha256 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(decisions);

        var directory = GetVerifiedDirectoryPath(directoryPath);
        var manifestSha256 = await ComputeManifestSha256Async(directory, cancellationToken).ConfigureAwait(false);
        if (expectedManifestSha256 is not null
            && !string.Equals(expectedManifestSha256, manifestSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("manifest 已变化，不能保存旧回放的复核结果。");
        }

        var orderedDecisions = ValidateAndOrderDecisions(frames, decisions);
        var sidecarPath = GetSidecarPathFromVerifiedDirectory(directory);
        var temporaryPath = sidecarPath + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new OfflineReplayReviewSidecar
                    {
                        Version = SidecarVersion,
                        ManifestSha256 = manifestSha256,
                        Decisions = orderedDecisions
                    },
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, sidecarPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static string GetSidecarPath(string directoryPath)
    {
        var directory = GetVerifiedDirectoryPath(directoryPath);
        return GetSidecarPathFromVerifiedDirectory(directory);
    }

    public async Task<string> GetManifestSha256Async(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var directory = GetVerifiedDirectoryPath(directoryPath);
        return await ComputeManifestSha256Async(directory, cancellationToken).ConfigureAwait(false);
    }

    private static string GetSidecarPathFromVerifiedDirectory(string verifiedDirectoryPath)
    {
        var sidecarPath = Path.GetFullPath(Path.Combine(verifiedDirectoryPath, SidecarFileName));
        var relativePath = Path.GetRelativePath(verifiedDirectoryPath, sidecarPath);
        if (!string.Equals(relativePath, SidecarFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("复核结果必须写入已校验的回放目录内。");
        }

        return sidecarPath;
    }

    private static string GetVerifiedDirectoryPath(string directoryPath)
    {
        var directory = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("回放目录不存在。");
        }

        return directory;
    }

    private static async Task<string> ComputeManifestSha256Async(
        string verifiedDirectoryPath,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(verifiedDirectoryPath, "manifest.json");
        var bytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static IReadOnlyList<OfflineReplayReviewDecision> ValidateAndOrderDecisions(
        IReadOnlyList<OfflineReplayFrameResult> frames,
        IReadOnlyList<OfflineReplayReviewDecision> decisions)
    {
        var framesById = frames
            .GroupBy(frame => frame.FrameId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var frameOrder = frames
            .Select((frame, index) => (frame.FrameId, index))
            .ToDictionary(item => item.FrameId, item => item.index, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var decision in decisions)
        {
            if (decision is null
                || string.IsNullOrWhiteSpace(decision.FrameId)
                || !Enum.IsDefined(decision.Kind)
                || !seen.Add(decision.FrameId)
                || !framesById.TryGetValue(decision.FrameId, out var frame)
                || frame.Status != OfflineReplayStatus.ReviewRequired
                || frame.Candidates is null)
            {
                throw new InvalidOperationException("复核决定与当前回放帧不匹配。");
            }

            switch (decision.Kind)
            {
                case OfflineReplayReviewDecisionKind.Accepted:
                    if (string.IsNullOrWhiteSpace(decision.CandidateItemId)
                        || !frame.Candidates.Any(candidate => string.Equals(
                            candidate.ItemId,
                            decision.CandidateItemId,
                            StringComparison.Ordinal)))
                    {
                        throw new InvalidOperationException("复核接受决定必须选择当前帧已有候选。");
                    }

                    break;
                case OfflineReplayReviewDecisionKind.Rejected:
                    if (!string.IsNullOrWhiteSpace(decision.CandidateItemId))
                    {
                        throw new InvalidOperationException("拒绝复核帧不能携带候选商品。");
                    }

                    break;
            }
        }

        return decisions
            .OrderBy(decision => frameOrder[decision.FrameId])
            .ThenBy(decision => decision.FrameId, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class OfflineReplayReviewSidecar
    {
        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("manifestSha256")]
        public string? ManifestSha256 { get; init; }

        [JsonPropertyName("decisions")]
        public IReadOnlyList<OfflineReplayReviewDecision>? Decisions { get; init; }
    }
}
