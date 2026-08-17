using System.Text.Json;
using MH.Collector;
using MH.Collector.Ocr;
using MH.Core.OfflineReplay;

namespace MH.Tests;

public sealed class CollectorReplayCheckpointTests
{
    private const string CheckpointFileName = ".offline-replay-checkpoint.json";
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 17, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CancellationResumesOnlyUnfinishedFramesAndCleansCheckpoint()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImagesAsync();
        await replay.WriteManifestAsync("original");
        using var cancellation = new CancellationTokenSource();
        var pausingRecognizer = new PausingRecognizer("frame-2");

        var replayTask = new OfflineReplayService(pausingRecognizer)
            .ReplayAsync(replay.Path, cancellation.Token);
        await pausingRecognizer.Paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replayTask);
        Assert.True(File.Exists(replay.CheckpointPath));

        var resumedRecognizer = new CountingRecognizer();
        var result = await new OfflineReplayService(resumedRecognizer).ReplayAsync(replay.Path);

        Assert.Equal(["frame-2"], resumedRecognizer.FrameIds);
        Assert.Equal(["frame-1", "frame-2"], result.Frames.Select(frame => frame.FrameId));
        Assert.Equal(2, result.SucceededCount);
        Assert.False(File.Exists(replay.CheckpointPath));
    }

    [Fact]
    public async Task ChangedManifestInvalidatesCheckpointAndReplaysEveryFrame()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImagesAsync();
        await replay.WriteManifestAsync("original");
        await CreateCancelledCheckpointAsync(replay);
        await replay.WriteManifestAsync("changed");
        var recognizer = new CountingRecognizer();

        var result = await new OfflineReplayService(recognizer).ReplayAsync(replay.Path);

        Assert.Equal(["frame-1", "frame-2"], recognizer.FrameIds);
        Assert.Equal(2, result.SucceededCount);
        Assert.False(File.Exists(replay.CheckpointPath));
    }

    [Fact]
    public async Task CorruptCheckpointIsIgnoredAndRemovedAfterSuccessfulReplay()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteImagesAsync();
        await replay.WriteManifestAsync("original");
        await File.WriteAllTextAsync(replay.CheckpointPath, "{ not-json");
        var recognizer = new CountingRecognizer();

        var result = await new OfflineReplayService(recognizer).ReplayAsync(replay.Path);

        Assert.Equal(["frame-1", "frame-2"], recognizer.FrameIds);
        Assert.Equal(2, result.SucceededCount);
        Assert.False(File.Exists(replay.CheckpointPath));
    }

    private static async Task CreateCancelledCheckpointAsync(ReplayDirectory replay)
    {
        using var cancellation = new CancellationTokenSource();
        var recognizer = new PausingRecognizer("frame-2");
        var replayTask = new OfflineReplayService(recognizer)
            .ReplayAsync(replay.Path, cancellation.Token);
        await recognizer.Paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replayTask);
        Assert.True(File.Exists(replay.CheckpointPath));
    }

    private static OcrRecognitionResult Completed(OcrRecognitionRequest request)
        => new(
            OcrRecognitionStatus.Completed,
            request.RawTextHint,
            [],
            request.CandidateHints);

    private sealed class CountingRecognizer : IOcrRecognizer
    {
        public List<string> FrameIds { get; } = [];

        public ValueTask<OcrRecognitionResult> RecognizeAsync(
            OcrRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FrameIds.Add(request.FrameId);
            return ValueTask.FromResult(Completed(request));
        }
    }

    private sealed class PausingRecognizer(string pausedFrameId) : IOcrRecognizer
    {
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<OcrRecognitionResult> RecognizeAsync(
            OcrRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!string.Equals(request.FrameId, pausedFrameId, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(Completed(request));
            }

            Paused.TrySetResult();
            return new ValueTask<OcrRecognitionResult>(WaitForCancellationAsync(request, cancellationToken));
        }

        private static async Task<OcrRecognitionResult> WaitForCancellationAsync(
            OcrRecognitionRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Completed(request);
        }
    }

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

        public string CheckpointPath => System.IO.Path.Combine(Path, CheckpointFileName);

        public static ReplayDirectory Create()
            => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MHCollectorCheckpoint-{Guid.NewGuid():N}"));

        public async Task WriteImagesAsync()
        {
            var framesPath = System.IO.Path.Combine(Path, "frames");
            Directory.CreateDirectory(framesPath);
            await File.WriteAllBytesAsync(System.IO.Path.Combine(framesPath, "one.png"), [1]);
            await File.WriteAllBytesAsync(System.IO.Path.Combine(framesPath, "two.png"), [2]);
        }

        public async Task WriteManifestAsync(string rawText)
        {
            var manifest = new
            {
                version = OfflineReplayContract.ManifestVersion,
                replayId = "checkpoint-replay",
                createdAtUtc = CreatedAtUtc,
                frames = new[]
                {
                    Frame("frame-1", "frames/one.png", CreatedAtUtc.AddSeconds(1), rawText),
                    Frame("frame-2", "frames/two.png", CreatedAtUtc.AddSeconds(2), rawText)
                }
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

        private static object Frame(
            string frameId,
            string relativeImagePath,
            DateTimeOffset capturedAtUtc,
            string rawText)
            => new
            {
                frameId,
                relativeImagePath,
                capturedAtUtc,
                rawText,
                candidates = new[]
                {
                    new
                    {
                        itemId = $"item-{frameId}",
                        displayName = frameId,
                        confidence = 0.95m,
                        isConfirmed = true
                    }
                }
            };
    }
}
