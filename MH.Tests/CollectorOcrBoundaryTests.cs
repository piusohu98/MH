using System.Text.Json;
using MH.Collector;
using MH.Collector.Ocr;
using MH.Core.OfflineReplay;

namespace MH.Tests;

public sealed class CollectorOcrBoundaryTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 17, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReplayInjectsManifestFieldsIntoRecognizerRequest()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImageAsync("frames/one.png");
        await replay.WriteManifestAsync(
            Frame(
                "frame-one",
                "frames/one.png",
                CreatedAtUtc.AddSeconds(1),
                "矿石识别",
                [Candidate("item-1", "铜矿", 0.95m, true)]));

        var recognizer = new ScriptedRecognizer(CompletedFromRequest);
        var result = await new OfflineReplayService(recognizer).ReplayAsync(replay.Path);

        Assert.Null(result.Error);
        Assert.Equal(OfflineReplayStatus.Accepted, Assert.Single(result.Frames).Status);
        var request = Assert.Single(recognizer.Requests);
        Assert.Equal("frame-one", request.FrameId);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(replay.Path, "frames/one.png")),
            request.ImagePath);
        Assert.Equal("矿石识别", request.RawTextHint);
        Assert.Equal(
            new OcrRecognitionCandidate("item-1", "铜矿", 0.95m, true),
            Assert.Single(request.CandidateHints));
    }

    [Fact]
    public async Task FailedRecognitionRejectsOnlyTheCurrentFrame()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImageAsync("frames/failed.png");
        await replay.WriteImageAsync("frames/accepted.png");
        await replay.WriteManifestAsync(
            Frame(
                "frame-failed",
                "frames/failed.png",
                CreatedAtUtc.AddSeconds(1),
                null,
                [Candidate("item-failed", "失败候选", 0.95m, true)]),
            Frame(
                "frame-accepted",
                "frames/accepted.png",
                CreatedAtUtc.AddSeconds(2),
                null,
                [Candidate("item-accepted", "成功候选", 0.95m, true)]));

        var recognizer = new ScriptedRecognizer(request => request.FrameId == "frame-failed"
            ? new(OcrRecognitionStatus.Failed, null, [], [], "recognizer failed")
            : Completed(
                request.RawTextHint,
                [new OcrRecognitionCandidate("item-accepted", "成功候选", 0.95m, true)]));
        var result = await new OfflineReplayService(recognizer).ReplayAsync(replay.Path);

        Assert.Null(result.Error);
        Assert.Equal(["frame-failed", "frame-accepted"], recognizer.Requests.Select(request => request.FrameId));
        Assert.Equal(OfflineReplayStatus.Rejected, result.Frames[0].Status);
        Assert.Contains("recognizer failed", result.Frames[0].Reason, StringComparison.Ordinal);
        Assert.Equal(OfflineReplayStatus.Accepted, result.Frames[1].Status);
        Assert.Equal(1, result.RejectedCount);
        Assert.Equal(1, result.SucceededCount);
    }

    [Fact]
    public async Task OrdinaryRecognitionExceptionRejectsOnlyTheCurrentFrame()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImageAsync("frames/throws.png");
        await replay.WriteImageAsync("frames/accepted.png");
        await replay.WriteManifestAsync(
            Frame("frame-throws", "frames/throws.png", CreatedAtUtc.AddSeconds(1), null, []),
            Frame(
                "frame-accepted",
                "frames/accepted.png",
                CreatedAtUtc.AddSeconds(2),
                null,
                [Candidate("item-accepted", "成功候选", 0.95m, true)]));

        var recognizer = new ScriptedRecognizer(request => request.FrameId == "frame-throws"
            ? throw new InvalidOperationException("recognizer exploded")
            : Completed(
                request.RawTextHint,
                [new OcrRecognitionCandidate("item-accepted", "成功候选", 0.95m, true)]));
        var result = await new OfflineReplayService(recognizer).ReplayAsync(replay.Path);

        Assert.Null(result.Error);
        Assert.Equal(["frame-throws", "frame-accepted"], recognizer.Requests.Select(request => request.FrameId));
        Assert.Equal(OfflineReplayStatus.Rejected, result.Frames[0].Status);
        Assert.Contains("OCR 识别失败：recognizer exploded", result.Frames[0].Reason, StringComparison.Ordinal);
        Assert.Equal(OfflineReplayStatus.Accepted, result.Frames[1].Status);
        Assert.Equal(1, result.RejectedCount);
        Assert.Equal(1, result.SucceededCount);
    }

    [Fact]
    public async Task RecognitionCancellationIsPropagated()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImageAsync("frames/cancelled.png");
        await replay.WriteManifestAsync(
            Frame("frame-cancelled", "frames/cancelled.png", CreatedAtUtc.AddSeconds(1), null, []));
        using var cancellation = new CancellationTokenSource();
        var recognizer = new CancellingRecognizer(cancellation);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => new OfflineReplayService(recognizer).ReplayAsync(replay.Path, cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(cancellation.Token, recognizer.ObservedToken);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Single(recognizer.Requests);
    }

    [Fact]
    public async Task DeterministicFakeSortsHintsWithoutReadingImageContent()
    {
        var imagePath = Path.Combine(
            Path.GetTempPath(),
            $"MHCollectorMissingImage-{Guid.NewGuid():N}",
            "unreadable.png");
        Assert.False(File.Exists(imagePath));

        var result = await new DeterministicFakeOcrRecognizer().RecognizeAsync(
            new OcrRecognitionRequest(
                "frame-fake",
                imagePath,
                "OCR text",
                [
                    new("item-z", "Z", 0.9m, true),
                    new("item-low", "Low", 0.5m, true),
                    new("item-a", "z-name", 0.9m, true),
                    new("ITEM-A", "A-name", 0.9m, true)
                ]));

        Assert.Equal(OcrRecognitionStatus.Completed, result.Status);
        Assert.Equal("OCR text", result.RawText);
        Assert.Equal(["ITEM-A", "item-a", "item-z", "item-low"], result.Candidates.Select(candidate => candidate.ItemId));
        Assert.Equal(["A-name", "z-name", "Z", "Low"], result.Candidates.Select(candidate => candidate.DisplayName));
        Assert.Equal(0.8m, Assert.Single(result.Lines).Confidence);
        Assert.False(File.Exists(imagePath));
    }

    private static OcrRecognitionResult CompletedFromRequest(OcrRecognitionRequest request)
        => Completed(request.RawTextHint, request.CandidateHints);

    private static OcrRecognitionResult Completed(
        string? rawText,
        IReadOnlyList<OcrRecognitionCandidate> candidates)
        => new(OcrRecognitionStatus.Completed, rawText, [], candidates);

    private static ReplayFrame Frame(
        string frameId,
        string relativeImagePath,
        DateTimeOffset capturedAtUtc,
        string? rawText,
        IReadOnlyList<ReplayCandidate> candidates)
        => new(frameId, relativeImagePath, capturedAtUtc, rawText, candidates);

    private static ReplayCandidate Candidate(
        string itemId,
        string displayName,
        decimal confidence,
        bool isConfirmed)
        => new(itemId, displayName, confidence, isConfirmed);

    private sealed class ScriptedRecognizer(
        Func<OcrRecognitionRequest, OcrRecognitionResult> resultFactory) : IOcrRecognizer
    {
        public List<OcrRecognitionRequest> Requests { get; } = [];

        public ValueTask<OcrRecognitionResult> RecognizeAsync(
            OcrRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(resultFactory(request));
        }
    }

    private sealed class CancellingRecognizer(CancellationTokenSource cancellation) : IOcrRecognizer
    {
        public List<OcrRecognitionRequest> Requests { get; } = [];

        public CancellationToken ObservedToken { get; private set; }

        public ValueTask<OcrRecognitionResult> RecognizeAsync(
            OcrRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            ObservedToken = cancellationToken;
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Completed(null, []));
        }
    }

    private sealed record ReplayFrame(
        string FrameId,
        string RelativeImagePath,
        DateTimeOffset CapturedAtUtc,
        string? RawText,
        IReadOnlyList<ReplayCandidate> Candidates);

    private sealed record ReplayCandidate(
        string ItemId,
        string DisplayName,
        decimal Confidence,
        bool IsConfirmed);

    private sealed class ReplayDirectory : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private ReplayDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public static ReplayDirectory Create()
            => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MHCollectorOcr-{Guid.NewGuid():N}"));

        public async Task WriteImageAsync(string relativePath)
        {
            var imagePath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            var imageDirectory = System.IO.Path.GetDirectoryName(imagePath);
            Assert.NotNull(imageDirectory);
            Directory.CreateDirectory(imageDirectory!);
            await File.WriteAllBytesAsync(imagePath, [0x89, 0x50, 0x4E, 0x47]);
        }

        public async Task WriteManifestAsync(params ReplayFrame[] frames)
        {
            var manifest = new
            {
                version = OfflineReplayContract.ManifestVersion,
                replayId = "replay-ocr-boundary",
                createdAtUtc = CreatedAtUtc,
                frames
            };

            await using var stream = File.Create(System.IO.Path.Combine(Path, "manifest.json"));
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
