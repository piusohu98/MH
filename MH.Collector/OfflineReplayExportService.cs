using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MH.Core.OfflineReplay;

namespace MH.Collector;

public sealed class OfflineReplayExportService
{
    public const string ExportFileName = ".offline-replay-export.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly OfflineReplayReviewStore reviewStore;

    public OfflineReplayExportService(OfflineReplayReviewStore? reviewStore = null)
    {
        this.reviewStore = reviewStore ?? new OfflineReplayReviewStore();
    }

    public async Task<OfflineReplayExportDocument> ExportAsync(
        string directoryPath,
        OfflineReplayScanResult scanResult,
        DateTimeOffset exportedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(scanResult);
        if (scanResult.Error is not null
            || string.IsNullOrWhiteSpace(scanResult.ReplayId)
            || string.IsNullOrWhiteSpace(scanResult.ManifestSha256)
            || exportedAtUtc == default
            || exportedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("离线回放结果不具备可导出的完整证据边界。");
        }

        var directory = GetVerifiedDirectoryPath(directoryPath);
        var manifestSha256 = await reviewStore
            .GetManifestSha256Async(directory, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(manifestSha256, scanResult.ManifestSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("manifest 已变化，不能导出旧回放证据。");
        }

        var review = await reviewStore
            .LoadForExportAsync(directory, scanResult.Frames, cancellationToken)
            .ConfigureAwait(false);
        if (!review.IsValid)
        {
            throw new InvalidOperationException("人工复核 sidecar 无法验证，不能生成不完整导出。");
        }

        var decisions = review.Decisions.ToDictionary(
            decision => decision.FrameId,
            StringComparer.Ordinal);
        var orderedFrames = scanResult.Frames
            .OrderBy(frame => frame.CapturedAtUtc)
            .ThenBy(frame => frame.ImagePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(frame => frame.FrameId, StringComparer.Ordinal)
            .ToArray();
        var seenFrameIds = new HashSet<string>(StringComparer.Ordinal);
        var exportFrames = new List<OfflineReplayExportFrame>(orderedFrames.Length);
        foreach (var frame in orderedFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(frame.FrameId)
                || !seenFrameIds.Add(frame.FrameId)
                || string.IsNullOrWhiteSpace(frame.ImagePath)
                || frame.CapturedAtUtc == default
                || frame.CapturedAtUtc.Offset != TimeSpan.Zero
                || !Enum.IsDefined(frame.Status)
                || frame.Reason is null
                || frame.Candidates is null
                || frame.Issues is null)
            {
                throw new InvalidOperationException("回放帧缺少可审计字段。");
            }

            var imagePath = GetImagePath(directory, frame.ImagePath);
            byte[] imageBytes;
            try
            {
                imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException($"无法读取回放帧图片：{frame.FrameId}", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new InvalidOperationException($"无权读取回放帧图片：{frame.FrameId}", exception);
            }

            if (imageBytes.Length == 0)
            {
                throw new InvalidOperationException($"回放帧图片为空：{frame.FrameId}");
            }

            var (effectiveDecision, effectiveCandidateItemId, isDecisionAccepted) =
                ResolveDecision(frame, decisions);
            exportFrames.Add(new OfflineReplayExportFrame(
                frame.FrameId,
                frame.ImagePath,
                Convert.ToHexString(SHA256.HashData(imageBytes)),
                frame.CapturedAtUtc,
                frame.RawText,
                frame.Candidates,
                frame.Issues,
                new OfflineReplayExportClassification(frame.Status, frame.Reason),
                effectiveDecision,
                effectiveCandidateItemId,
                isDecisionAccepted));
        }

        var finalManifestSha256 = await reviewStore
            .GetManifestSha256Async(directory, cancellationToken)
            .ConfigureAwait(false);
        var finalReview = await reviewStore
            .LoadForExportAsync(directory, scanResult.Frames, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(finalManifestSha256, manifestSha256, StringComparison.Ordinal)
            || !finalReview.IsValid
            || finalReview.Present != review.Present
            || !string.Equals(finalReview.SidecarSha256, review.SidecarSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("导出期间本地回放证据发生变化。");
        }

        var document = new OfflineReplayExportDocument(
            OfflineReplayExportContract.Version,
            OfflineReplayExportContract.DefaultSourceKind,
            scanResult.ReplayId!,
            exportedAtUtc,
            manifestSha256,
            review.Present,
            review.SidecarSha256,
            exportFrames);
        await WriteAsync(directory, document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    public static string GetExportPath(string directoryPath)
    {
        var directory = GetVerifiedDirectoryPath(directoryPath);
        var path = Path.GetFullPath(Path.Combine(directory, ExportFileName));
        var relativePath = Path.GetRelativePath(directory, path);
        if (!string.Equals(relativePath, ExportFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("导出文件必须位于已校验的回放目录内。");
        }

        return path;
    }

    private static async Task WriteAsync(
        string verifiedDirectoryPath,
        OfflineReplayExportDocument document,
        CancellationToken cancellationToken)
    {
        var exportPath = GetExportPath(verifiedDirectoryPath);
        var temporaryPath = exportPath + ".tmp";
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
                    document,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, exportPath, overwrite: true);
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

    private static (OfflineReplayEffectiveDecision Decision, string? CandidateItemId, bool IsMarketEligible)
        ResolveDecision(
            OfflineReplayFrameResult frame,
            IReadOnlyDictionary<string, OfflineReplayReviewDecision> decisions)
    {
        if (frame.Status == OfflineReplayStatus.Accepted)
        {
            if (frame.Candidates.Count != 1 || string.IsNullOrWhiteSpace(frame.Candidates[0].ItemId))
            {
                throw new InvalidOperationException($"自动接受帧候选不唯一：{frame.FrameId}");
            }

            return (
                OfflineReplayEffectiveDecision.AutoAccepted,
                frame.Candidates[0].ItemId,
                true);
        }

        if (frame.Status != OfflineReplayStatus.ReviewRequired
            || !decisions.TryGetValue(frame.FrameId, out var decision))
        {
            return (OfflineReplayEffectiveDecision.Unprocessed, null, false);
        }

        return decision.Kind switch
        {
            OfflineReplayReviewDecisionKind.Accepted => (
                OfflineReplayEffectiveDecision.ManuallyAccepted,
                decision.CandidateItemId,
                true),
            OfflineReplayReviewDecisionKind.Rejected => (
                OfflineReplayEffectiveDecision.ManuallyRejected,
                null,
                false),
            _ => throw new InvalidOperationException("复核决定类型未知。")
        };
    }

    private static string GetImagePath(string verifiedDirectoryPath, string relativeImagePath)
    {
        if (!OfflineReplayValidator.IsSafeRelativeImagePath(relativeImagePath))
        {
            throw new InvalidOperationException("导出图片路径不安全。");
        }

        var fullImagePath = Path.GetFullPath(Path.Combine(verifiedDirectoryPath, relativeImagePath));
        var root = verifiedDirectoryPath.EndsWith(Path.DirectorySeparatorChar)
            ? verifiedDirectoryPath
            : verifiedDirectoryPath + Path.DirectorySeparatorChar;
        if (!fullImagePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("导出图片必须位于回放目录内。");
        }

        if (!OfflineReplayContract.SupportedImageExtensions.Contains(Path.GetExtension(fullImagePath)))
        {
            throw new InvalidOperationException("导出图片格式不受支持。");
        }

        return fullImagePath;
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
}
