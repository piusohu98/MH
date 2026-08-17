using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MH.Collector.Ocr;
using MH.Core.OfflineReplay;
using MH.Core.Models;

namespace MH.Collector;

public sealed class OfflineReplayService
{
    private const string CheckpointFileName = ".offline-replay-checkpoint.json";
    private const string CheckpointVersion = "offline-replay-checkpoint-v1";

    private readonly IOcrRecognizer ocrRecognizer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions CheckpointJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public OfflineReplayService(IOcrRecognizer? ocrRecognizer = null)
    {
        this.ocrRecognizer = ocrRecognizer ?? new DeterministicFakeOcrRecognizer();
    }

    public async Task<OfflineReplayScanResult> ReplayAsync(
        string directoryPath,
        CancellationToken cancellationToken = default,
        IProgress<OfflineReplayProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        progress?.Report(new(OfflineReplayProgressState.Reading, 0, 0, null));

        var fullDirectoryPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectoryPath))
        {
            return Failed("目录不存在。", progress);
        }

        var manifestPath = Path.Combine(fullDirectoryPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return Failed("未找到 manifest.json。", progress);
        }

        byte[] manifestBytes;
        OfflineReplayDocument? document;
        try
        {
            manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            document = JsonSerializer.Deserialize<OfflineReplayDocument>(manifestBytes, JsonOptions);
        }
        catch (JsonException exception)
        {
            return Failed($"manifest.json 格式错误：{exception.Message}", progress);
        }
        catch (IOException exception)
        {
            return Failed($"无法读取 manifest.json：{exception.Message}", progress);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failed($"无权读取 manifest.json：{exception.Message}", progress);
        }

        var frames = document?.Frames ?? [];
        var manifest = new MH.Core.OfflineReplay.OfflineReplayManifest(
            document?.Version ?? string.Empty,
            document?.ReplayId ?? string.Empty,
            document?.CreatedAtUtc ?? default,
            frames.Select(frame => new MH.Core.OfflineReplay.OfflineReplayFrame(
                frame.FrameId ?? string.Empty,
                frame.RelativeImagePath ?? string.Empty,
                frame.CapturedAtUtc ?? default)).ToArray());
        var manifestValidation = OfflineReplayValidator.ValidateManifest(manifest);
        if (!manifestValidation.IsValid)
        {
            var reason = string.Join("；", manifestValidation.Issues.Select(issue => issue.Detail));
            return Failed($"manifest 校验失败：{reason}", progress);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var catalog = document?.Catalog is null
            ? null
            : document.Catalog
                .Select(item => new Item
                {
                    Id = item.Id ?? string.Empty,
                    Name = item.Name ?? string.Empty,
                    Category = string.Empty,
                    Unit = string.Empty
                })
                .ToArray();

        var orderedFrames = frames
            .OrderBy(frame => frame.CapturedAtUtc)
            .ThenBy(frame => frame.RelativeImagePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(frame => frame.FrameId, StringComparer.Ordinal)
            .ToArray();

        var checkpointPath = GetCheckpointPath(fullDirectoryPath);
        var manifestSha256 = Convert.ToHexString(SHA256.HashData(manifestBytes));
        var resumedResults = await LoadCheckpointAsync(
            checkpointPath,
            manifestSha256,
            orderedFrames,
            cancellationToken).ConfigureAwait(false);
        var results = new List<OfflineReplayFrameResult>(resumedResults.Count + orderedFrames.Length);
        results.AddRange(resumedResults);
        for (var index = results.Count; index < orderedFrames.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = orderedFrames[index];
            progress?.Report(new(
                OfflineReplayProgressState.Recognizing,
                results.Count,
                orderedFrames.Length,
                frame.FrameId));
            var result = await ReplayFrameAsync(fullDirectoryPath, manifest.ReplayId, frame, catalog, cancellationToken)
                .ConfigureAwait(false);
            results.Add(result);
            await WriteCheckpointAsync(checkpointPath, manifestSha256, results).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(checkpointPath);

        var scanResult = new OfflineReplayScanResult(new ReadOnlyCollection<OfflineReplayFrameResult>(results), null);
        progress?.Report(new(
            scanResult.ReviewRequiredCount > 0
                ? OfflineReplayProgressState.ReviewRequired
                : OfflineReplayProgressState.Completed,
            results.Count,
            orderedFrames.Length,
            null));
        return scanResult;
    }

    private static OfflineReplayScanResult Failed(
        string error,
        IProgress<OfflineReplayProgress>? progress)
    {
        progress?.Report(new(OfflineReplayProgressState.Failed, 0, 0, null));
        return OfflineReplayScanResult.Failed(error);
    }

    private static string GetCheckpointPath(string verifiedDirectoryPath)
    {
        var checkpointPath = Path.GetFullPath(Path.Combine(verifiedDirectoryPath, CheckpointFileName));
        var relativePath = Path.GetRelativePath(verifiedDirectoryPath, checkpointPath);
        if (!string.Equals(relativePath, CheckpointFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("回放 checkpoint 路径必须位于已校验的目录内。");
        }

        return checkpointPath;
    }

    private static async Task<IReadOnlyList<OfflineReplayFrameResult>> LoadCheckpointAsync(
        string checkpointPath,
        string manifestSha256,
        IReadOnlyList<OfflineReplayDocumentFrame> orderedFrames,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(checkpointPath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(checkpointPath, cancellationToken).ConfigureAwait(false);
            var checkpoint = JsonSerializer.Deserialize<OfflineReplayCheckpoint>(json, CheckpointJsonOptions);
            return IsUsableCheckpoint(checkpoint, manifestSha256, orderedFrames)
                ? checkpoint!.CompletedFrames!
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (NotSupportedException)
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

    private static bool IsUsableCheckpoint(
        OfflineReplayCheckpoint? checkpoint,
        string manifestSha256,
        IReadOnlyList<OfflineReplayDocumentFrame> orderedFrames)
    {
        if (checkpoint is null
            || !string.Equals(checkpoint.Version, CheckpointVersion, StringComparison.Ordinal)
            || !string.Equals(checkpoint.ManifestSha256, manifestSha256, StringComparison.Ordinal)
            || checkpoint.CompletedFrames is null
            || checkpoint.CompletedFrames.Count > orderedFrames.Count)
        {
            return false;
        }

        for (var index = 0; index < checkpoint.CompletedFrames.Count; index++)
        {
            var result = checkpoint.CompletedFrames[index];
            var frame = orderedFrames[index];
            if (result is null
                || !string.Equals(result.FrameId, frame.FrameId, StringComparison.Ordinal)
                || !string.Equals(result.ImagePath, frame.RelativeImagePath, StringComparison.Ordinal)
                || frame.CapturedAtUtc is null
                || result.CapturedAtUtc != frame.CapturedAtUtc.Value
                || result.Reason is null
                || result.CandidateText is null
                || !Enum.IsDefined(result.Status))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task WriteCheckpointAsync(
        string checkpointPath,
        string manifestSha256,
        IReadOnlyList<OfflineReplayFrameResult> completedFrames)
    {
        var temporaryPath = checkpointPath + ".tmp";
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
                    new OfflineReplayCheckpoint
                    {
                        Version = CheckpointVersion,
                        ManifestSha256 = manifestSha256,
                        CompletedFrames = completedFrames.ToArray()
                    },
                    CheckpointJsonOptions).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            File.Move(temporaryPath, checkpointPath, overwrite: true);
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

    private async Task<OfflineReplayFrameResult> ReplayFrameAsync(
        string directoryPath,
        string replayId,
        OfflineReplayDocumentFrame frame,
        IReadOnlyList<Item>? catalog,
        CancellationToken cancellationToken)
    {
        if (!OfflineReplayValidator.IsSafeRelativeImagePath(frame.RelativeImagePath ?? string.Empty))
        {
            return OfflineReplayFrameResult.Rejected(frame, "图片路径必须是目录内的规范相对路径。");
        }

        var relativePath = frame.RelativeImagePath!;
        string fullImagePath;
        try
        {
            fullImagePath = Path.GetFullPath(Path.Combine(directoryPath, relativePath));
            var directoryRoot = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullImagePath.StartsWith(directoryRoot, StringComparison.OrdinalIgnoreCase))
            {
                return OfflineReplayFrameResult.Rejected(frame, "图片路径必须位于所选目录内。");
            }
        }
        catch (ArgumentException)
        {
            return OfflineReplayFrameResult.Rejected(frame, "图片路径无效。");
        }

        if (!OfflineReplayContract.SupportedImageExtensions.Contains(Path.GetExtension(fullImagePath)))
        {
            return OfflineReplayFrameResult.Rejected(frame, "不支持的图片格式。");
        }

        try
        {
            await using var stream = File.OpenRead(fullImagePath);
            if (stream.Length == 0)
            {
                return OfflineReplayFrameResult.Rejected(frame, "图片文件为空。");
            }
        }
        catch (IOException)
        {
            return OfflineReplayFrameResult.Rejected(frame, "图片文件无法读取。");
        }
        catch (UnauthorizedAccessException)
        {
            return OfflineReplayFrameResult.Rejected(frame, "无权读取图片文件。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var candidateHints = (frame.Candidates ?? [])
            .Select(candidate => new OcrRecognitionCandidate(
                candidate.ItemId ?? string.Empty,
                candidate.DisplayName,
                candidate.Confidence,
                candidate.IsConfirmed))
            .ToArray();
        OcrRecognitionResult recognition;
        try
        {
            recognition = await ocrRecognizer.RecognizeAsync(
                new OcrRecognitionRequest(frame.FrameId!, fullImagePath, frame.RawText, candidateHints),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return OfflineReplayFrameResult.Rejected(frame, $"OCR 识别失败：{exception.Message}");
        }

        if (recognition is null)
        {
            return OfflineReplayFrameResult.Rejected(frame, "OCR 识别器未返回结果。");
        }

        if (recognition.Status == OcrRecognitionStatus.Failed)
        {
            return OfflineReplayFrameResult.Rejected(
                frame,
                recognition.Error ?? "OCR 识别失败。");
        }

        IReadOnlyList<MH.Core.OfflineReplay.OfflineReplayCandidate> candidates;
        IReadOnlyList<OfflineReplayIssue> matcherIssues = [];
        if (catalog is null)
        {
            candidates = (recognition.Candidates ?? [])
                .Select(candidate => new MH.Core.OfflineReplay.OfflineReplayCandidate(
                    candidate.ItemId,
                    candidate.DisplayName,
                    candidate.Confidence,
                    candidate.IsConfirmed))
                .ToArray();
        }
        else
        {
            var match = CatalogCandidateMatcher.Match(recognition.RawText, catalog);
            candidates = match.Candidates;
            matcherIssues = match.Issues;
        }
        var classified = OfflineReplayValidator.Classify(
            replayId,
            new MH.Core.OfflineReplay.OfflineReplayFrame(
                frame.FrameId!,
                relativePath,
                frame.CapturedAtUtc!.Value),
            candidates,
            recognition.RawText);

        if (matcherIssues.Count > 0)
        {
            classified = classified with
            {
                Issues = matcherIssues.Concat(classified.Issues).ToArray()
            };
        }

        return OfflineReplayFrameResult.From(frame, classified);
    }

    private sealed class OfflineReplayCheckpoint
    {
        [JsonConstructor]
        public OfflineReplayCheckpoint()
        {
        }

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("manifestSha256")]
        public string? ManifestSha256 { get; init; }

        [JsonPropertyName("completedFrames")]
        public IReadOnlyList<OfflineReplayFrameResult>? CompletedFrames { get; init; }
    }
}

public enum OfflineReplayProgressState
{
    Reading = 0,
    Recognizing = 1,
    ReviewRequired = 2,
    Completed = 3,
    Failed = 4
}

public sealed record OfflineReplayProgress(
    OfflineReplayProgressState State,
    int ProcessedCount,
    int TotalCount,
    string? CurrentFrameId);

public sealed record OfflineReplayScanResult(
    IReadOnlyList<OfflineReplayFrameResult> Frames,
    string? Error)
{
    public int SucceededCount => Frames.Count(frame => frame.Status == OfflineReplayStatus.Accepted);

    public int ReviewRequiredCount => Frames.Count(frame => frame.Status == OfflineReplayStatus.ReviewRequired);

    public int RejectedCount => Frames.Count(frame => frame.Status == OfflineReplayStatus.Rejected);

    public static OfflineReplayScanResult Failed(string error)
        => new([], error);
}

public sealed record OfflineReplayFrameResult(
    string FrameId,
    string ImagePath,
    DateTimeOffset CapturedAtUtc,
    OfflineReplayStatus Status,
    string Reason,
    string CandidateText)
{
    public string CapturedAtText => CapturedAtUtc == default
        ? "未提供"
        : CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string StatusText => Status switch
    {
        OfflineReplayStatus.Accepted => "成功",
        OfflineReplayStatus.ReviewRequired => "需人工复核",
        OfflineReplayStatus.Rejected => "拒绝",
        _ => "未知"
    };

    internal static OfflineReplayFrameResult From(
        OfflineReplayDocumentFrame frame,
        MH.Core.OfflineReplay.OfflineReplayResult result)
        => new(
            frame.FrameId!,
            frame.RelativeImagePath!,
            frame.CapturedAtUtc!.Value,
            result.Status,
            result.Issues.Count == 0
                ? "离线回放结果可用"
                : string.Join("；", result.Issues.Select(issue => issue.Detail)),
            result.Candidates.Count == 0
                ? "无候选"
                : string.Join("、", result.Candidates.Select(candidate => candidate.DisplayName ?? candidate.ItemId)));

    internal static OfflineReplayFrameResult Rejected(OfflineReplayDocumentFrame frame, string reason)
        => new(
            frame.FrameId ?? "未命名",
            frame.RelativeImagePath ?? string.Empty,
            frame.CapturedAtUtc ?? default,
            OfflineReplayStatus.Rejected,
            reason,
            "无候选");
}

internal sealed class OfflineReplayDocument
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("replayId")]
    public string? ReplayId { get; init; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset? CreatedAtUtc { get; init; }

    [JsonPropertyName("frames")]
    public List<OfflineReplayDocumentFrame>? Frames { get; init; }

    [JsonPropertyName("catalog")]
    public List<OfflineReplayDocumentCatalogItem>? Catalog { get; init; }
}

internal sealed class OfflineReplayDocumentCatalogItem
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class OfflineReplayDocumentFrame
{
    [JsonPropertyName("frameId")]
    public string? FrameId { get; init; }

    [JsonPropertyName("relativeImagePath")]
    public string? RelativeImagePath { get; init; }

    [JsonPropertyName("capturedAtUtc")]
    public DateTimeOffset? CapturedAtUtc { get; init; }

    [JsonPropertyName("rawText")]
    public string? RawText { get; init; }

    [JsonPropertyName("candidates")]
    public List<OfflineReplayDocumentCandidate>? Candidates { get; init; }
}

internal sealed class OfflineReplayDocumentCandidate
{
    [JsonPropertyName("itemId")]
    public string? ItemId { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("confidence")]
    public decimal Confidence { get; init; }

    [JsonPropertyName("isConfirmed")]
    public bool IsConfirmed { get; init; }
}
