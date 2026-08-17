using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MH.Collector.Ocr;
using MH.Core.OfflineReplay;

namespace MH.Collector;

public sealed class OfflineReplayService
{
    private readonly IOcrRecognizer ocrRecognizer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public OfflineReplayService(IOcrRecognizer? ocrRecognizer = null)
    {
        this.ocrRecognizer = ocrRecognizer ?? new DeterministicFakeOcrRecognizer();
    }

    public async Task<OfflineReplayScanResult> ReplayAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        var fullDirectoryPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectoryPath))
        {
            return OfflineReplayScanResult.Failed("目录不存在。");
        }

        var manifestPath = Path.Combine(fullDirectoryPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return OfflineReplayScanResult.Failed("未找到 manifest.json。");
        }

        OfflineReplayDocument? document;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            document = await JsonSerializer.DeserializeAsync<OfflineReplayDocument>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            return OfflineReplayScanResult.Failed($"manifest.json 格式错误：{exception.Message}");
        }
        catch (IOException exception)
        {
            return OfflineReplayScanResult.Failed($"无法读取 manifest.json：{exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return OfflineReplayScanResult.Failed($"无权读取 manifest.json：{exception.Message}");
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
            return OfflineReplayScanResult.Failed($"manifest 校验失败：{reason}");
        }

        var orderedFrames = frames
            .OrderBy(frame => frame.CapturedAtUtc)
            .ThenBy(frame => frame.RelativeImagePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(frame => frame.FrameId, StringComparer.Ordinal)
            .ToArray();
        var results = new List<OfflineReplayFrameResult>(orderedFrames.Length);
        foreach (var frame in orderedFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ReplayFrameAsync(fullDirectoryPath, manifest.ReplayId, frame, cancellationToken)
                .ConfigureAwait(false));
        }

        return new OfflineReplayScanResult(new ReadOnlyCollection<OfflineReplayFrameResult>(results), null);
    }

    private async Task<OfflineReplayFrameResult> ReplayFrameAsync(
        string directoryPath,
        string replayId,
        OfflineReplayDocumentFrame frame,
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

        var candidates = (recognition.Candidates ?? [])
            .Select(candidate => new MH.Core.OfflineReplay.OfflineReplayCandidate(
                candidate.ItemId,
                candidate.DisplayName,
                candidate.Confidence,
                candidate.IsConfirmed))
            .ToArray();
        var classified = OfflineReplayValidator.Classify(
            replayId,
            new MH.Core.OfflineReplay.OfflineReplayFrame(
                frame.FrameId!,
                relativePath,
                frame.CapturedAtUtc!.Value),
            candidates,
            recognition.RawText);

        return OfflineReplayFrameResult.From(frame, classified);
    }
}

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
