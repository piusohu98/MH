using System.Text.Json;
using MH.Collector;
using MH.Core.OfflineReplay;

namespace MH.Tests;

public sealed class CollectorReplayProgressTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 17, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReportsReadingEachFrameAndCompletedInOrder()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteAsync(0.95m, frameCount: 2);
        var events = new List<OfflineReplayProgress>();

        var result = await new OfflineReplayService().ReplayAsync(
            replay.Path,
            progress: new InlineProgress<OfflineReplayProgress>(events.Add));

        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(
            [
                OfflineReplayProgressState.Reading,
                OfflineReplayProgressState.Recognizing,
                OfflineReplayProgressState.Recognizing,
                OfflineReplayProgressState.Completed
            ],
            events.Select(progress => progress.State));
        Assert.Equal((0, 2, "frame-1"), ProgressValues(events[1]));
        Assert.Equal((1, 2, "frame-2"), ProgressValues(events[2]));
        Assert.Equal((2, 2, null), ProgressValues(events[3]));
    }

    [Fact]
    public async Task ReportsReviewRequiredAndFailedTerminalStates()
    {
        using var replay = ReplayDirectory.Create();
        await replay.WriteAsync(0.80m, frameCount: 1);
        var reviewEvents = new List<OfflineReplayProgress>();

        var review = await new OfflineReplayService().ReplayAsync(
            replay.Path,
            progress: new InlineProgress<OfflineReplayProgress>(reviewEvents.Add));
        var missingEvents = new List<OfflineReplayProgress>();
        var missingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MHMissing-{Guid.NewGuid():N}");
        var missing = await new OfflineReplayService().ReplayAsync(
            missingPath,
            progress: new InlineProgress<OfflineReplayProgress>(missingEvents.Add));

        Assert.Equal(1, review.ReviewRequiredCount);
        Assert.Equal(OfflineReplayProgressState.ReviewRequired, reviewEvents[^1].State);
        Assert.NotNull(missing.Error);
        Assert.Equal(
            [OfflineReplayProgressState.Reading, OfflineReplayProgressState.Failed],
            missingEvents.Select(progress => progress.State));
    }

    private static (int Processed, int Total, string? FrameId) ProgressValues(OfflineReplayProgress progress)
        => (progress.ProcessedCount, progress.TotalCount, progress.CurrentFrameId);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
            => report(value);
    }

    private sealed class ReplayDirectory : IDisposable
    {
        private ReplayDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public static ReplayDirectory Create()
            => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MHCollectorProgress-{Guid.NewGuid():N}"));

        public async Task WriteAsync(decimal confidence, int frameCount)
        {
            var framesPath = System.IO.Path.Combine(Path, "frames");
            Directory.CreateDirectory(framesPath);
            var frames = new List<object>(frameCount);
            for (var index = 1; index <= frameCount; index++)
            {
                var frameId = $"frame-{index}";
                var imageName = $"{index}.png";
                await File.WriteAllBytesAsync(System.IO.Path.Combine(framesPath, imageName), [1]);
                frames.Add(new
                {
                    frameId,
                    relativeImagePath = $"frames/{imageName}",
                    capturedAtUtc = CreatedAtUtc.AddSeconds(index),
                    rawText = frameId,
                    candidates = new[]
                    {
                        new
                        {
                            itemId = $"item-{index}",
                            displayName = frameId,
                            confidence,
                            isConfirmed = true
                        }
                    }
                });
            }

            var manifest = new
            {
                version = OfflineReplayContract.ManifestVersion,
                replayId = "progress-replay",
                createdAtUtc = CreatedAtUtc,
                frames
            };
            await using var stream = File.Create(System.IO.Path.Combine(Path, "manifest.json"));
            await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
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
